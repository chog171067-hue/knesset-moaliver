using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace KioskApp.Services
{
    /// <summary>מפעיל תוכנה חיצונית (למשל אוצר החכמה) וממקם אותה מתחת לסרגל ה-Overlay.</summary>
    public static class ProcessLauncher
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const uint SwpNoZOrder = 0x0004;
        private const int SwRestore = 9;

        public static Process Launch(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                throw new InvalidOperationException(
                    $"קובץ ההרצה לא נמצא בעמדה זו:\n{exePath}\n\nיש לוודא מול הצוות הטכני שהתוכנה מותקנת ושהנתיב בהגדרות (פאנל הניהול) נכון.");
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true
            });

            if (process == null)
            {
                throw new InvalidOperationException("הפעלת התוכנה נכשלה מסיבה לא ידועה.");
            }

            return process;
        }

        /// <summary>
        /// ממקם את חלון התוכנה שהופעלה כך שתתפוס את כל המסך מתחת לסרגל ה-Overlay
        /// (ראו סעיף 3.4 באפיון: לא Fullscreen אמיתי, אלא "Maximized" באזור הפנוי).
        /// Best-effort: יש תוכנות שלוקח להן זמן ליצור את החלון הראשי אחרי ההפעלה,
        /// ותוכנות בודדות מתעלמות מ-SetWindowPos (למשל אם הן פותחות Fullscreen אמיתי
        /// משל עצמן) - אם זה קורה בפועל עם "אוצר החכמה", ייתכן שיידרש כיוונון נוסף.
        /// </summary>
        public static async Task PositionBelowOverlayAsync(
            Process process, int overlayHeight, int screenWidth, int screenHeight, CancellationToken ct = default)
        {
            for (var attempt = 0; attempt < 20 && !ct.IsCancellationRequested; attempt++)
            {
                process.Refresh();
                if (process.HasExited) return;

                var handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SwRestore);
                    SetWindowPos(handle, IntPtr.Zero, 0, overlayHeight, screenWidth, screenHeight - overlayHeight, SwpNoZOrder);
                    return;
                }

                await Task.Delay(300, ct);
            }
        }

        /// <summary>
        /// סוגר את התוכנה שהופעלה בעת ניתוק/החלפת תוכנה - קריטי בעמדה ציבורית: אסור
        /// שהתוכנה או המסמכים של משתמש קודם יישארו פתוחים למשתמש הבא. ראשית מנסים
        /// סגירה "מנומסת" (נותנת לתוכנה הזדמנות להציג "לשמור שינויים?" וכו'), ורק אם
        /// היא לא נסגרת בזמן סביר - Kill כפוי.
        /// </summary>
        public static async Task CloseGracefullyAsync(Process process, TimeSpan? timeout = null)
        {
            try
            {
                process.Refresh();
                if (process.HasExited) return;

                process.CloseMainWindow();

                using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(4));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // התהליך כבר לא קיים - אין מה לעשות
            }
        }
    }
}
