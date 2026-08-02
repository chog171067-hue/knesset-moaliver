using System;
using System.Management;
using System.Printing;
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

        public event EventHandler<PrintChargedEventArgs>? PrintCharged;
        public event EventHandler<PrintBlockedEventArgs>? PrintBlocked;
        public event EventHandler<string>? MonitorError;

        public PrintMonitorService(KioskApiClient apiClient, Func<string> getCurrentUserId)
        {
            _apiClient = apiClient;
            _getCurrentUserId = getCurrentUserId;
        }

        public void Start()
        {
            if (_watcher != null) return;

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
                var printerName = Convert.ToString(targetInstance["Name"]) ?? "";
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
