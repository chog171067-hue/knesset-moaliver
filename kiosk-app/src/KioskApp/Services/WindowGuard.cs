using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace KioskApp.Services
{
    /// <summary>
    /// "שומר החלונות": כל חצי שנייה עובר על כל החלונות הראשיים שנפתחו מאז שהמשתמש
    /// התחבר, ומחזיר כל אחד מהם למצב הנדרש - ממלא את המסך מתחת ל-Overlay, לא ממוזער,
    /// בלי כפתורי מזעור/הגדלה/סגירה ובלי אפשרות לשנות גודל.
    ///
    /// למה לא מספיק לטפל רק בתהליך שהפעלנו: יש תוכנות (למשל פנקס הרשימות של Windows 11,
    /// ותוכנות רבות עם "Launcher") שבהן התהליך שאנחנו מפעילים רק פותח תהליך אחר ונסגר,
    /// והחלון האמיתי שייך לתהליך שלא הכרנו. לכן השומר מזהה חלונות לפי "מי לא היה קיים
    /// לפני ההתחברות" (Baseline), ולא לפי מזהה התהליך שהפעלנו.
    ///
    /// למה בדיקה חוזרת ולא פעם אחת: תוכנות מודרניות מציירות לעצמן את כפתורי החלון
    /// ומתעלמות מהסרת ה-Window Style, ותוכנות אחרות פותחות חלונות נוספים בהמשך.
    /// כל עוד השומר פועל, כל מזעור/הזזה מתוקנים תוך חצי שנייה.
    /// </summary>
    public static class WindowGuard
    {
        private static HashSet<int> _baselinePids = new();
        private static readonly int OwnPid = Environment.ProcessId;

        // תהליכים שלעולם לא נוגעים בהם, גם אם נפתחו אחרי ההתחברות
        private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "msedgewebview2",      // חלון הטענת הכסף של הקיוסק עצמו
            "explorer",            // שולחן העבודה/שורת המשימות (רלוונטי רק במחשב שעוד לא ננעל)
            "ShellExperienceHost",
            "StartMenuExperienceHost",
            "SearchHost",
            "TextInputHost",
            "ApplicationFrameHost",
            "LockApp",
            "dwm",
        };

        /// <summary>
        /// נקרא ברגע ההתחברות: כל תהליך שקיים כבר עכשיו (שירותי מערכת, ובמחשב שלא ננעל גם
        /// הדפדפן ושאר מה שפתוח) - לא שייך למשתמש ולא ייגעו בו.
        /// </summary>
        public static void CaptureBaseline()
        {
            try
            {
                _baselinePids = Process.GetProcesses().Select(p => p.Id).ToHashSet();
            }
            catch (Exception ex)
            {
                App.LogError("WindowGuard.CaptureBaseline", ex);
                _baselinePids = new HashSet<int>();
            }
        }

        /// <summary>מחזיר את כל החלונות הראשיים של תוכנות שנפתחו מאז ההתחברות.</summary>
        public static List<IntPtr> FindUserWindows()
        {
            var result = new List<IntPtr>();
            EnumWindows((hWnd, _) =>
            {
                if (IsUserAppWindow(hWnd)) result.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        /// <summary>
        /// מאכף את מצב כל החלונות. overlayHeightPx ומידות המסך - בפיקסלים פיזיים
        /// (לא ביחידות של WPF), כדי שהחישוב יהיה נכון גם כשהתצוגה מוגדלת ל-125%/150%.
        /// </summary>
        public static void Enforce(int overlayHeightPx)
        {
            var screenW = GetSystemMetrics(SmCxScreen);
            var screenH = GetSystemMetrics(SmCyScreen);
            var targetX = 0;
            var targetY = overlayHeightPx;
            var targetW = screenW;
            var targetH = screenH - overlayHeightPx;

            foreach (var hWnd in FindUserWindows())
            {
                try
                {
                    if (IsIconic(hWnd) || IsZoomed(hWnd))
                    {
                        // ממוזער - מחזירים. "מוגדל" של Windows מכסה גם את אזור ה-Overlay,
                        // לכן גם אותו מחזירים למצב רגיל וממקמים ידנית.
                        ShowWindow(hWnd, SwRestore);
                    }

                    var style = GetWindowLongPtr64(hWnd, GwlStyle).ToInt64();
                    var wantedStyle = style & ~(WsMinimizeBox | WsMaximizeBox | WsSysMenu | WsThickFrame);
                    var styleChanged = wantedStyle != style;
                    if (styleChanged)
                    {
                        SetWindowLongPtr64(hWnd, GwlStyle, new IntPtr(wantedStyle));
                    }

                    GetWindowRect(hWnd, out var r);
                    var positionWrong = r.Left != targetX || r.Top != targetY
                                        || r.Right - r.Left != targetW || r.Bottom - r.Top != targetH;

                    if (positionWrong || styleChanged)
                    {
                        SetWindowPos(hWnd, IntPtr.Zero, targetX, targetY, targetW, targetH,
                            SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
                    }
                }
                catch (Exception ex)
                {
                    App.LogError("WindowGuard.Enforce", ex);
                }
            }
        }

        /// <summary>
        /// סוגר את כל התוכנות שנפתחו מאז ההתחברות (בניתוק). קודם סגירה רגילה, ואם לא
        /// נסגרו תוך כמה שניות - סגירה כפויה. קריטי בעמדה ציבורית: אסור שמסמך של
        /// משתמש קודם יישאר פתוח למשתמש הבא.
        /// </summary>
        public static async Task CloseUserAppsAsync()
        {
            var pids = FindUserWindows()
                .Select(h => { GetWindowThreadProcessId(h, out var pid); return (int)pid; })
                .Distinct()
                .ToList();

            foreach (var pid in pids)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    await ProcessLauncher.CloseGracefullyAsync(p);
                }
                catch (ArgumentException)
                {
                    // התהליך כבר נסגר
                }
                catch (Exception ex)
                {
                    App.LogError("WindowGuard.CloseUserAppsAsync", ex);
                }
            }
        }

        private static bool IsUserAppWindow(IntPtr hWnd)
        {
            if (!IsWindowVisible(hWnd)) return false;
            if (GetWindow(hWnd, GwOwner) != IntPtr.Zero) return false; // דיאלוג (שמירה/הדפסה) - לא נוגעים

            var exStyle = GetWindowLongPtr64(hWnd, GwlExStyle).ToInt64();
            if ((exStyle & WsExToolWindow) != 0) return false;

            var style = GetWindowLongPtr64(hWnd, GwlStyle).ToInt64();
            if ((style & WsChild) != 0) return false;

            if (DwmGetWindowAttribute(hWnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false; // חלון "מוסתר" של אפליקציית חנות שרצה ברקע

            GetWindowThreadProcessId(hWnd, out var pidRaw);
            var pid = (int)pidRaw;
            if (pid == 0 || pid == OwnPid || _baselinePids.Contains(pid)) return false;

            try
            {
                using var p = Process.GetProcessById(pid);
                if (IgnoredProcessNames.Contains(p.ProcessName)) return false;
            }
            catch
            {
                return false;
            }

            // חלון בלי שום גודל ממשי (חלונות עזר נסתרים) - לא רלוונטי
            GetWindowRect(hWnd, out var r);
            if (r.Right - r.Left < 50 || r.Bottom - r.Top < 50) return false;

            return true;
        }

        // --- Win32 ---

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        private const uint GwOwner = 4;
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const int SwRestore = 9;
        private const int SmCxScreen = 0;
        private const int SmCyScreen = 1;
        private const int DwmwaCloaked = 14;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const long WsChild = 0x40000000L;
        private const long WsThickFrame = 0x00040000L;
        private const long WsSysMenu = 0x00080000L;
        private const long WsMinimizeBox = 0x00020000L;
        private const long WsMaximizeBox = 0x00010000L;
        private const long WsExToolWindow = 0x00000080L;
    }
}
