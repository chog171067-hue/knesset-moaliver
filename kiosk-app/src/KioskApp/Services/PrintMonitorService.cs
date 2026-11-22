using System;
using System.Linq;
using System.Management;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using KioskApp.Core;

namespace KioskApp.Services
{
    public class PrintChargedEventArgs : EventArgs
    {
        public int Pages { get; init; }
        public decimal Cost { get; init; }
        public decimal NewBalance { get; init; }
    }

    public class PrintRefundedEventArgs : EventArgs
    {
        public decimal NewBalance { get; init; }
    }

    public class PrintBlockedEventArgs : EventArgs
    {
        public decimal Balance { get; init; }
        public decimal Cost { get; init; }
    }

    /// <summary>
    /// עוקב אחרי תור ההדפסה של Windows (WMI, Win32_PrintJob - ראו סעיף 3.6 באפיון)
    /// ומחייב את המשתמש המחובר עבור כל עבודת הדפסה. מנסה לבטל את העבודה אם היתרה
    /// לא מספיקה.
    ///
    /// ⚠️ מגבלה חשובה שיש לבדוק בפועל מול המדפסת שתבחרו: אירוע ה-WMI נורה כשעבודת
    /// ההדפסה כבר נוצרת בתור, לא לפני. הביטול כאן הוא best-effort ומהיר (מתבצע תוך
    /// שבריר שנייה מרגע היצירה), אבל אין לו ערבות מוחלטת למנוע הדפסה פיזית במדפסות
    /// מהירות/עבודות של עמוד אחד - זו מגבלה מובנית של Windows Print Spooler ולא של
    /// הקוד הזה. ראו kiosk-app/README.md.
    /// </summary>
    public class PrintMonitorService : IDisposable
    {
        private readonly KioskApiClient _apiClient;
        private readonly Func<string> _getCurrentUserId;
        private ManagementEventWatcher? _watcher;
        private CancellationTokenSource _stopCts = new();

        // עבודות שכבר חויבו (לפי "מדפסת|מספר עבודה"). Windows מדווח לפעמים על אותה עבודה
        // יותר מפעם אחת (בעיקר כשהמדפסת מנותקת) - בלי זה כל דיווח נוסף היה חיוב נוסף.
        // סטטי: משותף לכל מופעי השירות, למקרה שבמעבר בין תוכנות שניים חופפים לרגע.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> ChargedJobs = new();

        // מעקב אחרי עבודה שכבר חויבה. בעיה = המדפסת דיווחה על תקלה (מוצג מיד, עם הסיבה),
        // או שההדפסה לא התחילה תוך NoProgressTimeout (יש מדפסות - למשל Canon בחיבור USB -
        // שלא מדווחות ל-Windows כלום כשהן מנותקות). על בעיה שואלים את המשתמש: "בטל הדפסה"
        // (ביטול + החזר כסף) או "המשך לחכות" (ואז שואלים שוב אחרי RepromptInterval).
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan NoProgressTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan RepromptInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MaxWatchTime = TimeSpan.FromMinutes(15);

        private const string NoProgressReason =
            "ההדפסה לא מתחילה. ייתכן שהמדפסת כבויה, מנותקת או שאין בה נייר.";

        /// <summary>
        /// שואל את המשתמש אם לבטל הדפסה שנתקעה (מקבל את תיאור הבעיה, מחזיר true לביטול).
        /// מוגדר ע"י ה-Overlay ונקרא על ה-UI thread. אם לא הוגדר - מבטלים אוטומטית.
        /// </summary>
        public Func<string, bool>? AskCancelStuckPrint { get; set; }

        public event EventHandler<PrintChargedEventArgs>? PrintCharged;
        public event EventHandler<PrintBlockedEventArgs>? PrintBlocked;
        public event EventHandler<PrintRefundedEventArgs>? PrintRefunded;
        public event EventHandler<string>? MonitorError;

        public PrintMonitorService(KioskApiClient apiClient, Func<string> getCurrentUserId)
        {
            _apiClient = apiClient;
            _getCurrentUserId = getCurrentUserId;
        }

        public void Start()
        {
            if (_watcher != null) return;
            _stopCts = new CancellationTokenSource();

            try
            {
                var query = new WqlEventQuery(
                    "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_PrintJob'");
                _watcher = new ManagementEventWatcher(query);
                _watcher.EventArrived += OnPrintJobCreated;
                _watcher.Start();
            }
            catch (Exception ex)
            {
                // אין WMI זמין / אין הרשאה - לא מפילים את כל האפליקציה בגלל זה,
                // רק מדווחים כדי שמנהל המערכת יוכל לבדוק (חיוב הדפסה פשוט לא יעבוד)
                MonitorError?.Invoke(this, $"ניטור הדפסה לא הופעל: {ex.Message}");
            }
        }

        public void Stop()
        {
            // עבודות שעדיין במעקב יבצעו בדיקה אחרונה (ביטול+החזר אם הן תקועות בתקלה)
            _stopCts.Cancel();

            if (_watcher == null) return;
            _watcher.EventArrived -= OnPrintJobCreated;
            _watcher.Stop();
            _watcher.Dispose();
            _watcher = null;
        }

        private async void OnPrintJobCreated(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                var jobId = Convert.ToInt32(targetInstance["JobId"]);
                // ב-Win32_PrintJob השדה Name הוא "שם המדפסת, מספר העבודה" - משאירים רק את שם המדפסת
                var printerName = ExtractPrinterName(Convert.ToString(targetInstance["Name"]) ?? "");
                var totalPagesRaw = targetInstance["TotalPages"];
                var pages = totalPagesRaw != null ? Convert.ToInt32(totalPagesRaw) : 0;
                if (pages <= 0) pages = 1; // חלק מדרייברים לא מדווחים TotalPages - עדיף לחייב עמוד אחד מאשר לא לחייב כלל

                // מדפסות וירטואליות (שמירה כ-PDF/XPS וכו') - לא מדפסת אמיתית, לא מחייבים כהדפסה
                if (IsVirtualPrinter(printerName)) return;

                var userId = _getCurrentUserId();
                if (string.IsNullOrEmpty(userId)) return; // אין משתמש מחובר כרגע (לא אמור לקרות, שכבת הגנה בלבד)

                // ניקוי רשומות ישנות (מספרי עבודות מתאפסים כש-Windows מאתחל את שירות ההדפסה)
                foreach (var old in ChargedJobs.Where(kv => DateTime.Now - kv.Value > TimeSpan.FromHours(2)).ToList())
                    ChargedJobs.TryRemove(old.Key, out _);

                var jobKey = $"{printerName}|{jobId}";
                if (!ChargedJobs.TryAdd(jobKey, DateTime.Now))
                {
                    App.LogInfo($"הדפסה: דיווח כפול על אותה עבודה ({jobKey}) - לא מחייבים שוב");
                    return;
                }
                App.LogInfo($"הדפסה: עבודה חדשה {jobKey}, {pages} עמודים - מחייב");

                try
                {
                    var result = await _apiClient.ChargePrintAsync(userId, pages);
                    Application.Current.Dispatcher.Invoke(() =>
                        PrintCharged?.Invoke(this, new PrintChargedEventArgs { Pages = pages, Cost = result.Cost, NewBalance = result.Balance }));

                    var transactionId = result.Transaction?.Id;
                    if (!string.IsNullOrEmpty(transactionId))
                    {
                        _ = WatchChargedJobAsync(printerName, jobId, userId, transactionId, _stopCts.Token);
                    }
                }
                catch (InsufficientFundsException ex)
                {
                    TryCancelPrintJob(printerName, jobId);
                    Application.Current.Dispatcher.Invoke(() =>
                        PrintBlocked?.Invoke(this, new PrintBlockedEventArgs { Balance = ex.Balance, Cost = ex.Cost }));
                }
            }
            catch (Exception ex)
            {
                App.LogError("שגיאה בטיפול באירוע הדפסה", ex);
            }
        }

        /// <summary>
        /// עוקב אחרי עבודת הדפסה שכבר חויבה. אם היא נתקעת בתקלה בלי שהודפס ממנה אף עמוד -
        /// מבטל אותה ומחזיר למשתמש את הכסף. עבודה שנעלמה מהתור בלי שנראתה בה תקלה
        /// נחשבת כהודפסה (כך מתנהגת הדפסה רגילה ותקינה).
        /// </summary>
        private async Task WatchChargedJobAsync(string printerName, int jobId, string userId, string transactionId, CancellationToken stopToken)
        {
            var started = DateTime.Now;
            var nextPromptAt = DateTime.MinValue;
            var problemSeen = false; // הייתה תקלה מדווחת, או שהמשתמש כבר ראה את חלון הבעיה

            try
            {
                while (true)
                {
                    var stopping = false;
                    try { await Task.Delay(PollInterval, stopToken); }
                    catch (OperationCanceledException) { stopping = true; }

                    var (state, reason) = ReadJobState(printerName, jobId);

                    if (state == JobState.Printed) return;
                    if (state == JobState.Gone)
                    {
                        // העבודה יצאה מהתור. אם לא הייתה שום בעיה - זו הדפסה רגילה שהסתיימה.
                        // אם הייתה בעיה - היא בוטלה/נזרקה בלי להיות מודפסת: מחזירים.
                        if (problemSeen) await RefundAsync(userId, transactionId, notify: !stopping);
                        return;
                    }

                    string? problem = null;
                    if (state == JobState.Error)
                    {
                        problemSeen = true;
                        problem = reason;
                    }
                    else if (DateTime.Now - started >= NoProgressTimeout)
                    {
                        problem = NoProgressReason;
                    }

                    // בניתוק: עבודה בעייתית לא תחכה למשתמש הבא - מבטלים ומחזירים בשקט
                    if (stopping)
                    {
                        if (problem != null) await CancelAndRefundAsync(printerName, jobId, userId, transactionId, notify: false);
                        return;
                    }

                    // רשת ביטחון: עבודה שלא הודפסה גם אחרי זמן רב - מבטלים ומחזירים
                    if (DateTime.Now - started >= MaxWatchTime)
                    {
                        await CancelAndRefundAsync(printerName, jobId, userId, transactionId, notify: true);
                        return;
                    }

                    if (problem == null || DateTime.Now < nextPromptAt) continue;

                    problemSeen = true;
                    var cancel = AskUser(problem);

                    // בזמן שהחלון היה פתוח ייתכן שהבעיה תוקנה וההדפסה יצאה בפועל - רק אז לא מחזירים.
                    // (עבודה שסתם נעלמה מהתור אחרי שהיתה בעיה - לא נחשבת כמודפסת.)
                    var (after, _) = ReadJobState(printerName, jobId);
                    if (after == JobState.Printed) return;

                    if (cancel || after == JobState.Gone)
                    {
                        await CancelAndRefundAsync(printerName, jobId, userId, transactionId, notify: true);
                        return;
                    }
                    nextPromptAt = DateTime.Now + RepromptInterval;
                }
            }
            catch (Exception ex)
            {
                App.LogError($"שגיאה במעקב אחרי עבודת הדפסה (מדפסת: {printerName}, jobId: {jobId})", ex);
            }
        }

        private bool AskUser(string problem)
        {
            var ask = AskCancelStuckPrint;
            if (ask == null) return true;
            try
            {
                return Application.Current.Dispatcher.Invoke(() => ask(problem));
            }
            catch (Exception ex)
            {
                App.LogError("כשל בהצגת חלון בעיית הדפסה", ex);
                return true;
            }
        }

        private async Task CancelAndRefundAsync(string printerName, int jobId, string userId, string transactionId, bool notify)
        {
            TryCancelPrintJob(printerName, jobId);
            await RefundAsync(userId, transactionId, notify);
        }

        private enum JobState { Gone, Printed, Error, Waiting }

        private static (JobState State, string Reason) ReadJobState(string printerName, int jobId)
        {
            try
            {
                using var printServer = new PrintServer();
                using var queue = printServer.GetPrintQueue(printerName);
                queue.Refresh();

                // מחפשים את העבודה ברשימת העבודות של התור. "נעלמה" רק אם הרשימה נקראה בהצלחה
                // והעבודה לא בה - תקלה רגעית בקריאה לא תיחשב כ"נעלמה".
                using var jobs = queue.GetPrintJobInfoCollection();
                PrintSystemJobInfo? job = null;
                foreach (var j in jobs)
                {
                    if (j.JobIdentifier == jobId) job = j;
                }
                if (job == null) return (JobState.Gone, "");

                job.Refresh();
                if (job.IsPrinted || job.NumberOfPagesPrinted > 0) return (JobState.Printed, "");
                if (job.IsDeleted || job.IsDeleting) return (JobState.Gone, "");

                var reason = DescribeError(queue, job);
                if (reason != null) return (JobState.Error, reason);
                return (JobState.Waiting, "");
            }
            catch (Exception ex)
            {
                App.LogError($"כשל בקריאת מצב עבודת הדפסה (מדפסת: {printerName}, jobId: {jobId})", ex);
                return (JobState.Waiting, "");
            }
        }

        /// <summary>
        /// מתרגם את סימוני התקלה ש-Windows מקבל מהמדפסת להודעה בעברית. הסדר - מהספציפי לכללי.
        /// הערה: מצב הדיו לרוב לא מדווח ל-Windows (מדפסות כמו Canon מדווחות עליו רק בתוכנה
        /// שלהן), ולכן "נגמר הדיו" יופיע בדרך כלל כ"שגיאה במדפסת" הכללית.
        /// מחזיר null אם אין תקלה.
        /// </summary>
        private static string? DescribeError(PrintQueue queue, PrintSystemJobInfo job)
        {
            if (queue.IsOutOfPaper || job.IsPaperOut) return "נגמר הנייר במדפסת";
            if (queue.IsPaperJammed) return "נייר תקוע במדפסת";
            if (queue.IsDoorOpened) return "מכסה המדפסת פתוח";
            if (queue.IsOutOfMemory) return "זיכרון המדפסת מלא";
            if (queue.IsOffline || queue.IsNotAvailable || job.IsOffline) return "המדפסת כבויה או מנותקת";
            if (queue.IsInError || job.IsInError || job.IsBlocked || job.IsUserInterventionRequired
                || queue.IsManualFeedRequired || queue.IsOutputBinFull)
                return queue.IsTonerLow
                    ? "שגיאה במדפסת - כנראה נגמר הדיו"
                    : "שגיאה במדפסת (ייתכן שנגמר הדיו, או תקלה אחרת)";
            return null;
        }

        /// <summary>
        /// מבקש מהשרת החזר. עד 3 ניסיונות (הפרעה רגעית באינטרנט לא תבטל החזר). אם כל הניסיונות
        /// נכשלו - מודיעים למשתמש לפנות לצוות, והפרטים נרשמים ביומן לטיפול ידני.
        /// </summary>
        private async Task RefundAsync(string userId, string transactionId, bool notify)
        {
            Exception? lastError = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var newBalance = await _apiClient.RefundPrintAsync(userId, transactionId);
                    App.LogInfo($"הדפסה לא בוצעה - החיוב הוחזר (משתמש {userId}, עסקה {transactionId})");
                    if (notify)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            PrintRefunded?.Invoke(this, new PrintRefundedEventArgs { NewBalance = newBalance }));
                    }
                    return;
                }
                catch (KioskApiException ex) when (ex.Message.Contains("כבר הוחזר"))
                {
                    return; // כבר הוחזר בעבר - אין מה לעשות
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
                }
            }

            App.LogError($"כשל בהחזר חיוב הדפסה אחרי 3 ניסיונות (משתמש {userId}, עסקה {transactionId}) - נדרש החזר ידני", lastError!);
            if (notify)
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                    "ההדפסה בוטלה, אך החזרת הכסף ליתרה נכשלה.\nנא לפנות לגבאים - הפרטים נשמרו במערכת.",
                    "החזר לא בוצע", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        }

        private static readonly string[] VirtualPrinterNames =
        {
            "Microsoft Print to PDF", "Microsoft XPS Document Writer", "OneNote", "Fax", "Send To OneNote"
        };

        public static bool IsVirtualPrinter(string printerName)
        {
            foreach (var v in VirtualPrinterNames)
            {
                if (printerName.StartsWith(v, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string ExtractPrinterName(string wmiJobName)
        {
            var idx = wmiJobName.LastIndexOf(',');
            return idx > 0 ? wmiJobName.Substring(0, idx).Trim() : wmiJobName.Trim();
        }

        /// <summary>ראו הערת המגבלה בראש הקובץ - ניסיון ביטול מיידי, בלי ערבות שיצליח בזמן.</summary>
        private void TryCancelPrintJob(string printerName, int jobId)
        {
            try
            {
                using var printServer = new PrintServer();
                using var queue = printServer.GetPrintQueue(printerName);
                var job = queue.GetJob(jobId);
                job.Cancel();
            }
            catch (Exception ex)
            {
                App.LogError($"כשל בביטול עבודת הדפסה (מדפסת: {printerName}, jobId: {jobId})", ex);
            }
        }

        public void Dispose() => Stop();
    }
}
