using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace KioskApp.Services
{
    /// <summary>
    /// מעקב מיידי אחרי עבודות הדפסה חדשות - דרך התראות של שירות ההדפסה של Windows
    /// (FindFirstPrinterChangeNotification), לא דרך בדיקה פעם בשנייה (WMI). ברגע שעבודה נוצרת
    /// בתור של מדפסת אמיתית היא **נעצרת מיד** (תוך אלפיות שנייה, עוד לפני שעמוד הגיע למדפסת), ורק
    /// אז הקיוסק שואל ומחייב. בלי זה, עבודה קטנה (עמוד אחד) הספיקה להגיע למדפסת ולצאת מהתור לפני
    /// שהקיוסק ראה אותה - הודפסה בלי אישור ובלי חיוב.
    /// עבודה שהקיוסק עצמו שלח אחרי חיוב (isPrepaid לפי שם המסמך) - לא נעצרת.
    /// </summary>
    public sealed class SpoolerWatcher : IDisposable
    {
        /// <summary>עבודה חדשה: מדפסת, מספר עבודה, שם המסמך, עמודים, האם נעצרה</summary>
        public event Action<string, int, string, int, bool>? JobAdded;

        private readonly Func<string, bool> _isPrepaid;
        private readonly ManualResetEvent _stop = new(false);
        private readonly List<Thread> _threads = new();

        public SpoolerWatcher(Func<string, bool> isPrepaid)
        {
            _isPrepaid = isPrepaid;
        }

        public void Start()
        {
            foreach (var printer in PrinterSettings.InstalledPrinters.Cast<string>()
                         .Where(p => !PrintMonitorService.IsVirtualPrinter(p)))
            {
                var t = new Thread(() => Watch(printer)) { IsBackground = true, Name = "SpoolerWatcher " + printer };
                _threads.Add(t);
                t.Start();
            }
            App.LogInfo($"מעקב הדפסות מיידי פעיל ({_threads.Count} מדפסות)");
        }

        public void Dispose()
        {
            _stop.Set();
        }

        private void Watch(string printerName)
        {
            if (!OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
            {
                App.LogInfo($"מעקב הדפסות מיידי: לא ניתן לפתוח את המדפסת {printerName} (שגיאה {Marshal.GetLastWin32Error()})");
                return;
            }
            var hChange = IntPtr.Zero;
            try
            {
                hChange = FindFirstPrinterChangeNotification(hPrinter, PrinterChangeAddJob, 0, IntPtr.Zero);
                if (hChange == IntPtr.Zero || hChange == new IntPtr(-1))
                {
                    App.LogInfo($"מעקב הדפסות מיידי: אין התראות למדפסת {printerName} (שגיאה {Marshal.GetLastWin32Error()})");
                    return;
                }

                // עבודות שכבר היו בתור לפני שהמעקב התחיל - לא נוגעים בהן
                var seen = new HashSet<int>(EnumerateJobs(hPrinter).Select(j => j.JobId));
                using var changeEvent = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(hChange, false) };
                var handles = new WaitHandle[] { changeEvent, _stop };

                while (true)
                {
                    var signaled = WaitHandle.WaitAny(handles);
                    if (signaled == 1) return; // עצירה
                    FindNextPrinterChangeNotification(hChange, out _, IntPtr.Zero, IntPtr.Zero);

                    var jobs = EnumerateJobs(hPrinter);
                    foreach (var job in jobs)
                    {
                        if (!seen.Add(job.JobId)) continue;
                        var paused = false;
                        if (!_isPrepaid(job.Document))
                        {
                            // עוצרים מיד - לפני כל דבר אחר
                            paused = SetJob(hPrinter, (uint)job.JobId, 0, IntPtr.Zero, JobControlPause);
                            if (!paused) App.LogInfo($"מעקב הדפסות מיידי: עצירת עבודה {job.JobId} נכשלה (שגיאה {Marshal.GetLastWin32Error()})");
                        }
                        try { JobAdded?.Invoke(printerName, job.JobId, job.Document, job.Pages, paused); }
                        catch (Exception ex) { App.LogError("מעקב הדפסות מיידי: טיפול בעבודה", ex); }
                    }
                    // עבודות שכבר לא בתור - יוצאות מהרשימה (מספרי עבודות חוזרים אחרי הפעלה מחדש)
                    seen.IntersectWith(jobs.Select(j => j.JobId));
                }
            }
            catch (Exception ex)
            {
                App.LogError($"מעקב הדפסות מיידי: תקלה במדפסת {printerName}", ex);
            }
            finally
            {
                if (hChange != IntPtr.Zero && hChange != new IntPtr(-1)) FindClosePrinterChangeNotification(hChange);
                ClosePrinter(hPrinter);
            }
        }

        private readonly record struct JobInfo(int JobId, string Document, int Pages);

        private static List<JobInfo> EnumerateJobs(IntPtr hPrinter)
        {
            var result = new List<JobInfo>();
            EnumJobs(hPrinter, 0, 999, 1, IntPtr.Zero, 0, out var needed, out _);
            if (needed == 0) return result;
            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!EnumJobs(hPrinter, 0, 999, 1, buffer, needed, out _, out var returned)) return result;
                var size = Marshal.SizeOf<JobInfo1>();
                for (var i = 0; i < returned; i++)
                {
                    var info = Marshal.PtrToStructure<JobInfo1>(buffer + i * size);
                    result.Add(new JobInfo((int)info.JobId, info.pDocument ?? "", (int)info.TotalPages));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return result;
        }

        private const uint PrinterChangeAddJob = 0x00000100;
        private const uint JobControlPause = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JobInfo1
        {
            public uint JobId;
            public string pPrinterName;
            public string pMachineName;
            public string pUserName;
            public string pDocument;
            public string pDatatype;
            public string pStatus;
            public uint Status;
            public uint Priority;
            public uint Position;
            public uint TotalPages;
            public uint PagesPrinted;
            public SystemTime Submitted;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemTime
        {
            public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
        }

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern IntPtr FindFirstPrinterChangeNotification(IntPtr hPrinter, uint fdwFilter, uint fdwOptions, IntPtr pPrinterNotifyOptions);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool FindNextPrinterChangeNotification(IntPtr hChange, out uint pdwChange, IntPtr pPrinterNotifyOptions, IntPtr ppPrinterNotifyInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool FindClosePrinterChangeNotification(IntPtr hChange);

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumJobs(IntPtr hPrinter, uint firstJob, uint noJobs, uint level, IntPtr pJob, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool SetJob(IntPtr hPrinter, uint jobId, uint level, IntPtr pJob, uint command);
    }
}
