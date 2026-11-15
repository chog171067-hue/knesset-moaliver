using System;
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

        // מעקב אחרי עבודה שכבר חויבה: אם היא תקועה בתקלה (מדפסת מנותקת, נגמר נייר...)
        // ולא הודפס ממנה אף עמוד - אחרי ErrorTimeout מבטלים אותה ומחזירים את הכסף.
        // StuckTimeout - רשת ביטחון לעבודה שלא זזה בכלל גם בלי סימון תקלה מפורש.
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ErrorTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan StuckTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan MaxWatchTime = TimeSpan.FromMinutes(15);

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

                var userId = _getCurrentUserId();
                if (string.IsNullOrEmpty(userId)) return; // אין משתמש מחובר כרגע (לא אמור לקרות, שכבת הגנה בלבד)

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
            DateTime? errorSince = null;
            var everInError = false;

            try
            {
                while (DateTime.Now - started < MaxWatchTime)
                {
                    var stopping = false;
                    try { await Task.Delay(PollInterval, stopToken); }
                    catch (OperationCanceledException) { stopping = true; }

                    var state = ReadJobState(printerName, jobId);

                    if (state == JobState.Gone)
                    {
                        // נעלמה אחרי תקלה (בוטלה ע"י המדפסת/המשתמש) - מחזירים. אחרת - הודפסה.
                        if (everInError) await RefundAsync(userId, transactionId, notify: !stopping);
                        return;
                    }
                    if (state == JobState.Printed) return;

                    if (state == JobState.Error)
                    {
                        everInError = true;
                        errorSince ??= DateTime.Now;
                    }
                    else
                    {
                        errorSince = null;
                    }

                    var errorTooLong = errorSince.HasValue && DateTime.Now - errorSince.Value >= ErrorTimeout;
                    var stuckTooLong = DateTime.Now - started >= StuckTimeout;

                    // בניתוק: עבודה שתקועה כרגע בתקלה לא תודפס למשתמש הבא - מבטלים ומחזירים מיד
                    if (errorTooLong || stuckTooLong || (stopping && state == JobState.Error))
                    {
                        TryCancelPrintJob(printerName, jobId);
                        await RefundAsync(userId, transactionId, notify: !stopping);
                        return;
                    }

                    if (stopping) return; // תקינה ועדיין בתור - תמשיך להדפיס כרגיל
                }
            }
            catch (Exception ex)
            {
                App.LogError($"שגיאה במעקב אחרי עבודת הדפסה (מדפסת: {printerName}, jobId: {jobId})", ex);
            }
        }

        private enum JobState { Gone, Printed, Error, Waiting }

        private static JobState ReadJobState(string printerName, int jobId)
        {
            try
            {
                using var printServer = new PrintServer();
                using var queue = printServer.GetPrintQueue(printerName);
                queue.Refresh();

                PrintSystemJobInfo job;
                try
                {
                    job = queue.GetJob(jobId);
                }
                catch
                {
                    return JobState.Gone;
                }

                using (job)
                {
                    job.Refresh();
                    if (job.IsPrinted || job.IsCompleted || job.NumberOfPagesPrinted > 0) return JobState.Printed;
                    if (job.IsDeleted || job.IsDeleting) return JobState.Gone;

                    var inError = job.IsInError || job.IsOffline || job.IsPaperOut || job.IsBlocked
                                  || job.IsUserInterventionRequired
                                  || queue.IsOffline || queue.IsInError || queue.IsNotAvailable
                                  || queue.IsPaperOut || queue.IsPaperJammed || queue.IsDoorOpened;
                    return inError ? JobState.Error : JobState.Waiting;
                }
            }
            catch (Exception ex)
            {
                App.LogError($"כשל בקריאת מצב עבודת הדפסה (מדפסת: {printerName}, jobId: {jobId})", ex);
                return JobState.Waiting;
            }
        }

        private async Task RefundAsync(string userId, string transactionId, bool notify)
        {
            try
            {
                var newBalance = await _apiClient.RefundPrintAsync(userId, transactionId);
                App.LogInfo($"הדפסה נכשלה - החיוב הוחזר (משתמש {userId}, עסקה {transactionId})");
                if (notify)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        PrintRefunded?.Invoke(this, new PrintRefundedEventArgs { NewBalance = newBalance }));
                }
            }
            catch (Exception ex)
            {
                App.LogError($"כשל בהחזר חיוב הדפסה (משתמש {userId}, עסקה {transactionId})", ex);
            }
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
