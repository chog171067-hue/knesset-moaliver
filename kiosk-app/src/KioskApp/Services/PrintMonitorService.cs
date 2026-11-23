using System;
using System.Linq;
using System.Management;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using KioskApp.Core;
using KioskApp.Views;

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
                WindowGuard.DriverProblemDetected += OnDriverProblem;
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
            WindowGuard.DriverProblemDetected -= OnDriverProblem;

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

                // הדפסה מעורך הטקסט שכבר אושרה וחויבה מראש (PrepayAsync) - לא שואלים ולא מחייבים שוב,
                // רק עוקבים אחריה (תקלה ← ביטול + החזר)
                var document = Convert.ToString(targetInstance["Document"]) ?? "";
                if (PrepaidJobs.TryRemove(document, out var prepaid))
                {
                    ChargedJobs.TryAdd($"{printerName}|{jobId}", DateTime.Now);
                    _lastCharged = (printerName, jobId, prepaid.UserId, prepaid.TransactionId, DateTime.Now);
                    _lastChargedRefunded = false;
                    App.LogInfo($"הדפסה: עבודה {jobId} מעורך הטקסט - כבר אושרה וחויבה");
                    _ = WatchChargedJobAsync(printerName, jobId, prepaid.UserId, prepaid.TransactionId, _stopCts.Token);
                    return;
                }

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
                // עוצרים את העבודה בתור עד שהמשתמש מאשר את העלות (חלון האישור של הקיוסק)
                var paused = TryPauseJob(printerName, jobId);
                pages = await WaitForSpoolingAsync(printerName, jobId, pages);
                var price = App.Session.Config?.PricePerPage ?? 0;
                var cost = Math.Round(pages * price, 2);
                App.LogInfo($"הדפסה: עבודה חדשה {jobKey}, {pages} עמודים, {cost:0.00} ₪ - ממתין לאישור (נעצרה: {paused})");

                try { App.LogInfo($"הדפסה: חלונות פתוחים לפני האישור:{WindowGuard.DescribeVisibleWindows()}"); }
                catch { /* רק לאבחון */ }
                // כמה שניות לחלון "מצב הדפסה" של הדרייבר לדווח על תקלה (אין נייר וכו')
                await Task.Delay(2500);
                if (!WaitForPrinterReady(printerName) || !ConfirmPrint(pages, cost)
                    || (RecentDriverProblem() != null && !WaitForPrinterReady(printerName)))
                {
                    App.LogInfo($"הדפסה: המשתמש ביטל את {jobKey}");
                    TryCancelPrintJob(printerName, jobId);
                    return;
                }

                try
                {
                    var result = await _apiClient.ChargePrintAsync(userId, pages);
                    Application.Current.Dispatcher.Invoke(() =>
                        PrintCharged?.Invoke(this, new PrintChargedEventArgs { Pages = pages, Cost = result.Cost, NewBalance = result.Balance }));
                    if (!string.IsNullOrEmpty(result.Transaction?.Id))
                    {
                        _lastCharged = (printerName, jobId, userId, result.Transaction!.Id, DateTime.Now);
                        _lastChargedRefunded = false;
                    }
                    if (paused) TryResumeJob(printerName, jobId);
                    App.LogInfo($"הדפסה: {jobKey} חויבה ({result.Cost:0.00} ₪) ונשלחה למדפסת");
                    _ = Application.Current.Dispatcher.InvokeAsync(async () =>
                        await KioskMessageDialog.ShowTimedAsync(App.ActiveWindow(), KioskMessageKind.Success,
                            "נשלח להדפסה", $"{pages} {(pages == 1 ? "עמוד" : "עמודים")} · חויבו {result.Cost:0.00} ₪", 2.5));

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
                catch (Exception ex)
                {
                    // החיוב נכשל (למשל אין אינטרנט) - לא מדפיסים בלי חיוב
                    App.LogError($"הדפסה: חיוב {jobKey} נכשל - העבודה מבוטלת", ex);
                    TryCancelPrintJob(printerName, jobId);
                    Application.Current.Dispatcher.Invoke(() =>
                        KioskMessageDialog.ShowMessage(App.ActiveWindow(), KioskMessageKind.Error, "ההדפסה בוטלה",
                            "לא ניתן היה לחייב את ההדפסה כרגע (אין חיבור לשרת). לא חויבתם. אפשר לנסות שוב בעוד רגע."));
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
            var queueProblem = DescribeQueueProblem(queue);
            if (queueProblem != null) return queueProblem;
            if (job.IsPaperOut) return PrinterProblems.PaperOut;
            if (job.IsOffline) return PrinterProblems.Offline;
            if (job.IsInError || job.IsBlocked || job.IsUserInterventionRequired) return PrinterProblems.General;
            return null;
        }

        /// <summary>תקלה במדפסת עצמה (לא בעבודה מסוימת) - עם הסבר מה לעשות. null = אין תקלה.</summary>
        private static string? DescribeQueueProblem(PrintQueue queue)
        {
            if (queue.IsOutOfPaper) return PrinterProblems.PaperOut;
            if (queue.IsPaperJammed) return PrinterProblems.PaperJam;
            if (queue.IsDoorOpened) return PrinterProblems.DoorOpen;
            if (queue.IsManualFeedRequired) return PrinterProblems.ManualFeed;
            if (queue.IsOutputBinFull) return PrinterProblems.OutputFull;
            if (queue.IsOutOfMemory) return PrinterProblems.OutOfMemory;
            if (queue.IsOffline || queue.IsNotAvailable) return PrinterProblems.Offline;
            if (queue.IsInError || queue.NeedUserIntervention)
                return queue.IsTonerLow ? PrinterProblems.Toner : PrinterProblems.General;
            return null;
        }

        // ===== תקלה שדווחה בחלון של הדרייבר (WindowGuard.DriverProblemDetected) =====
        // Windows לא יודע על "אין נייר" - רק התוכנה של Samsung. הקיוסק קורא את החלון שלה,
        // סוגר אותו, ומציג את החלון שלו: לפני האישור - לא מחייבים עד שהתקלה תוקנה; אחרי חיוב -
        // "בטל הדפסה" מבטל ומחזיר את הכסף.
        private string? _driverProblem;
        private DateTime _driverProblemAt = DateTime.MinValue;
        private static readonly TimeSpan DriverProblemValidFor = TimeSpan.FromSeconds(90);
        private (string Printer, int JobId, string UserId, string TransactionId, DateTime At)? _lastCharged;
        private bool _lastChargedRefunded;
        private bool _problemDialogOpen;

        private string? RecentDriverProblem()
        {
            var p = _driverProblem;
            return p != null && DateTime.Now - _driverProblemAt < DriverProblemValidFor ? p : null;
        }

        private void OnDriverProblem(string problem)
        {
            _driverProblem = problem;
            _driverProblemAt = DateTime.Now;

            // הדפסה שכבר חויבה ונשלחה (בדקות האחרונות) - מציעים לבטל ולקבל את הכסף בחזרה
            var charged = _lastCharged;
            if (charged == null || _lastChargedRefunded || _problemDialogOpen) return;
            if (DateTime.Now - charged.Value.At > TimeSpan.FromMinutes(5)) return;
            _ = Task.Run(async () =>
            {
                var cancel = AskProblem(problem);
                if (!cancel || _lastChargedRefunded) return;
                _lastChargedRefunded = true;
                TryCancelPrintJob(charged.Value.Printer, charged.Value.JobId);
                await RefundAsync(charged.Value.UserId, charged.Value.TransactionId, notify: true);
            });
        }

        /// <summary>חלון "בעיה במדפסת". מחזיר true = בטל הדפסה.</summary>
        private bool AskProblem(string problem)
        {
            if (_problemDialogOpen) return false;
            _problemDialogOpen = true;
            try
            {
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    var dialog = new PrintProblemDialog(problem);
                    var owner = App.ActiveWindow();
                    if (owner != null && owner.IsVisible && owner != dialog) dialog.Owner = owner;
                    dialog.ShowDialog();
                    return dialog.CancelRequested;
                });
            }
            finally
            {
                _problemDialogOpen = false;
            }
        }

        /// <summary>מצב המדפסת כרגע (לפני אישור ההדפסה). null = אין תקלה / לא ידוע.</summary>
        private static string? CheckPrinterProblem(string printerName)
        {
            try
            {
                using var printServer = new PrintServer();
                using var queue = printServer.GetPrintQueue(printerName);
                queue.Refresh();
                return DescribeQueueProblem(queue);
            }
            catch (Exception ex)
            {
                App.LogError($"הדפסה: כשל בבדיקת מצב המדפסת ({printerName})", ex);
                return null;
            }
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
            "Microsoft Print to PDF", "Microsoft XPS Document Writer", "OneNote", "Fax", "Send To OneNote", "Adobe PDF"
        };

        public static bool IsVirtualPrinter(string printerName)
        {
            // כל "מדפסת" ששומרת לקובץ (PDF / XPS) - לא מדפסת אמיתית
            if (printerName.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0
                || printerName.IndexOf("XPS", StringComparison.OrdinalIgnoreCase) >= 0) return true;
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

        /// <summary>
        /// חלון האישור של הקיוסק לפני הדפסה: מספר עמודים, שחור-לבן, צד אחד, והסכום לחיוב.
        /// יתרה לא מספיקה - "להטענה" (ואז בודקים שוב) או ביטול. מחזיר true = להדפיס.
        /// </summary>
        // הדפסות שאושרו וחויבו מראש (עורך הטקסט): שם המסמך הייחודי ← המשתמש והעסקה
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string UserId, string TransactionId)> PrepaidJobs = new();

        /// <summary>
        /// עורך הטקסט: אישור וחיוב **לפני** ששולחים להדפסה (מספר העמודים ידוע מראש). עבודה קטנה
        /// מהעורך מגיעה למדפסת מהר יותר ממה שהמעקב מספיק לעצור אותה - לכן לא סומכים על העצירה.
        /// מחזיר שם מסמך ייחודי לעבודה (להעביר להדפסה), או null אם בוטל / לא חויב.
        /// נקרא על ה-UI thread.
        /// </summary>
        public async Task<string?> PrepayAsync(string printerName, int pages)
        {
            var userId = _getCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return null;
            var price = App.Session.Config?.PricePerPage ?? 0;
            var cost = Math.Round(pages * price, 2);

            if (!WaitForPrinterReady(printerName) || !ConfirmPrint(pages, cost)) return null;

            try
            {
                var result = await _apiClient.ChargePrintAsync(userId, pages);
                PrintCharged?.Invoke(this, new PrintChargedEventArgs { Pages = pages, Cost = result.Cost, NewBalance = result.Balance });
                var transactionId = result.Transaction?.Id ?? "";
                var documentName = $"עורך טקסט - {Guid.NewGuid():N}";
                if (transactionId.Length > 0)
                {
                    PrepaidJobs[documentName] = (userId, transactionId);
                    // תקלה מהדרייבר (אין נייר) עוד לפני שהמעקב ראה את העבודה - "בטל" יחזיר את הכסף
                    _lastCharged = (printerName, -1, userId, transactionId, DateTime.Now);
                    _lastChargedRefunded = false;
                }
                App.LogInfo($"הדפסה מעורך הטקסט: {pages} עמודים, חויבו {result.Cost:0.00} ₪");
                return documentName;
            }
            catch (InsufficientFundsException ex)
            {
                PrintBlocked?.Invoke(this, new PrintBlockedEventArgs { Balance = ex.Balance, Cost = ex.Cost });
                return null;
            }
            catch (Exception ex)
            {
                App.LogError("הדפסה מעורך הטקסט: החיוב נכשל", ex);
                KioskMessageDialog.ShowMessage(App.ActiveWindow(), KioskMessageKind.Error, "ההדפסה בוטלה",
                    "לא ניתן היה לחייב את ההדפסה כרגע (אין חיבור לשרת). לא חויבתם. אפשר לנסות שוב בעוד רגע.");
                return null;
            }
        }

        /// <summary>עורך הטקסט: ההדפסה נכשלה אחרי החיוב (לא נשלחה) - מחזירים את הכסף</summary>
        public async Task RefundPrepaidAsync(string documentName)
        {
            if (PrepaidJobs.TryRemove(documentName, out var prepaid))
            {
                _lastChargedRefunded = true;
                await RefundAsync(prepaid.UserId, prepaid.TransactionId, notify: true);
            }
        }

        private static bool ConfirmPrint(int pages, decimal cost)
        {
            return Application.Current.Dispatcher.Invoke(() =>
            {
                while (App.Session.IsActive)
                {
                    var balance = App.Session.CurrentUser?.Balance ?? 0;
                    var pagesText = pages == 1 ? "עמוד אחד" : $"{pages} עמודים";
                    if (balance < cost)
                    {
                        var topup = KioskMessageDialog.ShowChoice(App.ActiveWindow(), KioskMessageKind.Info, "אין מספיק יתרה להדפסה",
                            $"הדפסה של {pagesText} עולה {cost:0.00} ₪, והיתרה בארנק היא {balance:0.00} ₪.",
                            "להטענה", "ביטול ההדפסה");
                        if (!topup) return false;
                        var owner = App.ActiveWindow();
                        var window = new TopupWindow();
                        if (owner != null && owner.IsVisible) window.Owner = owner;
                        window.ShowDialog();
                        continue; // בודקים שוב אחרי ההטענה
                    }
                    return KioskMessageDialog.ShowChoice(App.ActiveWindow(), KioskMessageKind.Info, "אישור הדפסה",
                        $"הינך עומד להדפיס {pagesText}\nשחור-לבן · צד אחד בלבד\n\nסכום לחיוב: {cost:0.00} ₪",
                        "אישור והדפסה", "ביטול");
                }
                return false;
            });
        }

        /// <summary>
        /// לפני אישור ההדפסה: אם יש תקלה במדפסת (אין נייר, מכסה פתוח...) - חלון של הקיוסק עם
        /// הסבר מה לעשות: "טענתי - המשך" (בודקים שוב) או "בטל הדפסה". מחזיר false = לבטל.
        /// </summary>
        private bool WaitForPrinterReady(string printerName)
        {
            for (var round = 0; round < 20; round++)
            {
                var problem = CheckPrinterProblem(printerName) ?? RecentDriverProblem();
                if (problem == null) return true;
                App.LogInfo($"הדפסה: תקלה במדפסת לפני אישור - {problem.Replace("\n", " ")}");
                var cancel = AskProblem(problem);
                if (cancel || !App.Session.IsActive) return false;
                // "טענתי - המשך": שוכחים את התקלה הקודמת, ונותנים לדרייבר כמה שניות לדווח שוב אם
                // היא עדיין קיימת
                _driverProblem = null;
                Thread.Sleep(3000);
            }
            return false;
        }

        /// <summary>
        /// מחכים שהתוכנה תסיים לשלוח את העבודה לתור (עד 30 שניות) - רק אז מספר העמודים סופי.
        /// </summary>
        private static async Task<int> WaitForSpoolingAsync(string printerName, int jobId, int pagesSoFar)
        {
            var pages = pagesSoFar;
            for (var i = 0; i < 60; i++)
            {
                try
                {
                    using var printServer = new PrintServer();
                    using var queue = printServer.GetPrintQueue(printerName);
                    using var job = queue.GetJob(jobId);
                    job.Refresh();
                    if (job.NumberOfPages > pages) pages = job.NumberOfPages;
                    if (!job.IsSpooling) break;
                }
                catch
                {
                    break; // העבודה כבר לא בתור / אין גישה - נשארים עם מה שידוע
                }
                await Task.Delay(500);
            }
            return Math.Max(1, pages);
        }

        private static bool TryPauseJob(string printerName, int jobId)
        {
            try
            {
                using var printServer = new PrintServer();
                using var queue = printServer.GetPrintQueue(printerName);
                using var job = queue.GetJob(jobId);
                job.Pause();
                return true;
            }
            catch (Exception ex)
            {
                App.LogError($"הדפסה: כשל בעצירת העבודה לאישור (מדפסת: {printerName}, jobId: {jobId})", ex);
                return false;
            }
        }

        private static void TryResumeJob(string printerName, int jobId)
        {
            // משחררים, ובודקים שהעבודה באמת כבר לא עצורה (עד 3 ניסיונות) - עם רישום ליומן
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var printServer = new PrintServer();
                    using var queue = printServer.GetPrintQueue(printerName);
                    using var job = queue.GetJob(jobId);
                    job.Resume();
                    Thread.Sleep(700);
                    job.Refresh();
                    App.LogInfo($"הדפסה: שחרור עבודה {jobId} (ניסיון {attempt}) - מצב: {job.JobStatus}");
                    if (!job.IsPaused) return;
                }
                catch (Exception ex)
                {
                    // העבודה כבר לא בתור = כבר הודפסה / נשלחה למדפסת
                    App.LogInfo($"הדפסה: עבודה {jobId} כבר לא בתור אחרי השחרור ({ex.GetType().Name})");
                    return;
                }
            }
            App.LogInfo($"הדפסה: עבודה {jobId} עדיין עצורה אחרי 3 ניסיונות שחרור");
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
