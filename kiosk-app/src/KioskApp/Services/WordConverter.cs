using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace KioskApp.Services
{
    /// <summary>
    /// קובץ Word (doc/docx) מדיסק-און-קי → PDF, להדפסה דרך PdfPrinter. Word מופעל מוסתר לגמרי
    /// (המשתמש לא רואה אותו ולא יכול לגעת בו), בלי מאקרו, בלי הודעות, בלי "קבצים אחרונים".
    /// הקובץ מועתק קודם לתיקייה זמנית (לא עובדים ישירות מהדיסק-און-קי), והכול נמחק בסוף.
    /// אם Word לא מסיים בזמן (למשל הודעה נסתרת) - התהליך שלו נסגר בכוח.
    /// </summary>
    public static class WordConverter
    {
        public static readonly string[] Extensions = { ".doc", ".docx" };

        public static bool IsWordFile(string path) =>
            Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        public static Task<byte[]> ConvertToPdfAsync(string sourcePath, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var workDir = Path.Combine(Path.GetTempPath(), "kiosk-word-" + Guid.NewGuid().ToString("N"));
            var startedAt = DateTime.Now;

            // Office (COM) עובד ב-thread מסוג STA
            var thread = new Thread(() =>
            {
                try
                {
                    tcs.TrySetResult(Convert(sourcePath, workDir));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return WaitAsync();

            async Task<byte[]> WaitAsync()
            {
                var finished = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
                try
                {
                    if (finished != tcs.Task)
                    {
                        App.LogInfo("Word: ההמרה לא הסתיימה בזמן - Word נסגר בכוח");
                        KillWordStartedAfter(startedAt);
                        throw new InvalidOperationException("המרת קובץ ה-Word לא הסתיימה. אפשר לשמור אותו כ-PDF ולנסות שוב.");
                    }
                    return await tcs.Task;
                }
                finally
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); } catch { }
                    });
                }
            }
        }

        private static byte[] Convert(string sourcePath, string workDir)
        {
            Directory.CreateDirectory(workDir);
            var copy = Path.Combine(workDir, "doc" + Path.GetExtension(sourcePath));
            var pdf = Path.Combine(workDir, "doc.pdf");
            File.Copy(sourcePath, copy);

            var type = Type.GetTypeFromProgID("Word.Application")
                       ?? throw new InvalidOperationException("Word לא מותקן בעמדה - אפשר להדפיס רק קובצי PDF.");
            dynamic? word = null;
            dynamic? doc = null;
            try
            {
                word = Activator.CreateInstance(type)!;
                word.Visible = false;
                word.DisplayAlerts = 0;          // wdAlertsNone
                word.AutomationSecurity = 3;     // msoAutomationSecurityForceDisable - בלי מאקרו
                try
                {
                    // סיסמה שגויה במקום חלון שמבקש סיסמה: קובץ מוגן נכשל מיד
                    doc = word.Documents.Open(
                        FileName: copy, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false,
                        PasswordDocument: "kiosk-no-password", Visible: false, NoEncodingDialog: true);
                }
                catch (Exception ex)
                {
                    App.LogError("Word: פתיחת הקובץ נכשלה", ex);
                    throw new InvalidOperationException("לא ניתן לפתוח את קובץ ה-Word. ייתכן שהוא פגום או מוגן בסיסמה.");
                }
                doc.ExportAsFixedFormat(OutputFileName: pdf, ExportFormat: 17); // wdExportFormatPDF
                doc.Close(SaveChanges: 0);
                doc = null;
                return File.ReadAllBytes(pdf);
            }
            finally
            {
                try { doc?.Close(SaveChanges: 0); } catch { }
                try { word?.Quit(SaveChanges: 0); } catch { }
                if (word != null) { try { Marshal.FinalReleaseComObject(word); } catch { } }
            }
        }

        private static void KillWordStartedAfter(DateTime startedAt)
        {
            foreach (var p in Process.GetProcessesByName("WINWORD"))
            {
                using (p)
                {
                    try { if (p.StartTime >= startedAt.AddSeconds(-2)) p.Kill(); } catch { }
                }
            }
        }
    }
}
