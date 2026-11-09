using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KioskApp.Services
{
    /// <summary>מפעיל תוכנה חיצונית (למשל אוצר החכמה). מיקום החלון ונעילתו - ב-WindowGuard.</summary>
    public static class ProcessLauncher
    {
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
