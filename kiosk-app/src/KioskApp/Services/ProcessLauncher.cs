using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpNoActivate = 0x0010;
        private const int SwRestore = 9;
        private const int GwlStyle = -16;

        // מסירים את שלושת "כפתורי הבקרה" הרגילים של חלון Windows - מזעור, הגדלה,
        // ותפריט המערכת (שכולל את כפתור הסגירה X, וגם חוסם ברוב התוכנות את Alt+F4).
        // ראו סעיף "מסך מלא ללא אפשרות יציאה" - הוחלט לעשות זאת ברמת ה-Window Style
        // של Windows, לא ברמת קוד התוכנה החיצונית (שאין לנו שליטה עליו). כפתור
        // "ניתוק" ב-Overlay (שנשאר Topmost תמיד) ממשיך להיות דרך היציאה היחידה.
        private const long WsMinimizeBox = 0x00020000L;
        private const long WsMaximizeBox = 0x00010000L;
        private const long WsSysMenu = 0x00080000L;

        private static void RemoveWindowControls(IntPtr handle)
        {
            var style = GetWindowLongPtr64(handle, GwlStyle).ToInt64();
            style &= ~(WsMinimizeBox | WsMaximizeBox | WsSysMenu);
            SetWindowLongPtr64(handle, GwlStyle, new IntPtr(style));

            // SWP_FRAMECHANGED מכריח את Windows "לצייר" מחדש את מסגרת החלון כדי
            // שהשינוי בסטייל אכן יופיע ויתעדכן ויזואלית, לא רק ברמת הזיכרון הפנימי.
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoZOrder | SwpNoActivate | SwpFrameChanged | 0x0001 | 0x0002); // SWP_NOSIZE | SWP_NOMOVE
        }

        // רשת ביטחון: עוקבים אחרי *כל* התהליכים שהופעלו במהלך ה-session (לא רק
        // האחרון) - כדי שניתוק תמיד יסגור את כולם, גם אם משתמש הצליח ללחוץ פעמיים
        // בטעות על כפתור בחירת תוכנה (למשל בגלל עיכוב פוקוס בין חלונות) ונוצרו שני
        // תהליכים בו-זמנית. קריטי בעמדה ציבורית: אסור שתוכנה/מסמך של משתמש קודם
        // יישארו פתוחים למשתמש הבא, בשום תרחיש.
        private static readonly ConcurrentBag<Process> TrackedProcesses = new();

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

            TrackedProcesses.Add(process);
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
                    RemoveWindowControls(handle);
                    return;
                }

                await Task.Delay(300, ct);
            }
        }

        /// <summary>
        /// סוגר תהליך בודד בעת ניתוק/החלפת תוכנה - קריטי בעמדה ציבורית: אסור
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

        /// <summary>
        /// רשת הביטחון: סוגר את *כל* התהליכים שהופעלו במהלך ה-session הנוכחי,
        /// לא רק את זה שה-Overlay עוקב אחריו כרגע. יש לקרוא לזה בניתוק (Logout) -
        /// גם אם בגלל תקלה (למשל לחיצה כפולה על כפתור בחירת תוכנה) נוצרו כמה
        /// תהליכים שה-Overlay לא ידע עליהם, כולם ייסגרו בכל זאת.
        /// </summary>
        public static async Task CloseAllTrackedAsync(TimeSpan? timeout = null)
        {
            // מעתיקים לרשימה קבועה כדי לא "לרדוף" אחרי ConcurrentBag שמשתנה תוך כדי איטרציה
            var snapshot = TrackedProcesses.ToArray();
            foreach (var process in snapshot)
            {
                await CloseGracefullyAsync(process, timeout);
            }

            // מנקים את הרשימה - תהליכים שנסגרו (או שכשלה סגירתם ונזרקו) לא צריכים
            // להישאר בזיכרון לנצח לאורך כל ימי חיי העמדה
            while (TrackedProcesses.TryTake(out _)) { }
        }
    }
}
