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
        /// נקרא פעם אחת בעליית הקיוסק: כל תהליך שקיים כבר עכשיו (שירותי מערכת, ובמחשב
        /// שלא ננעל גם הדפדפן ושאר מה שפתוח) - לא שייך למשתמשי הקיוסק ולא ייגעו בו.
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
        public static void Enforce(int overlayHeightPx) => Enforce(overlayHeightPx, null);

        // מטמון: האם תהליך שייך לתיקיית התוכנה (שלא נבדוק את הנתיב בכל חצי שנייה)
        private static readonly Dictionary<int, bool> PidInAppFolder = new();
        private static string? _pidCacheFolder;

        private static bool IsFromFolder(int pid, string folder)
        {
            if (_pidCacheFolder != folder) { PidInAppFolder.Clear(); _pidCacheFolder = folder; }
            if (PidInAppFolder.TryGetValue(pid, out var known)) return known;
            var result = false;
            try
            {
                using var p = Process.GetProcessById(pid);
                var path = p.MainModule?.FileName;
                result = path != null && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // אין גישה לנתיב - לא של התוכנה
            }
            if (PidInAppFolder.Count > 500) PidInAppFolder.Clear();
            PidInAppFolder[pid] = result;
            return result;
        }

        /// <summary>
        /// appFolder: תיקיית התוכנה שהופעלה. כשידועה - רק חלונות של תהליכים ממנה נמתחים למסך
        /// מלא. חלונות אחרים (למשל "מצב הדפסה" של הדרייבר של Samsung) נשארים בגודל שלהם.
        /// </summary>
        public static void Enforce(int overlayHeightPx, string? appFolder)
        {
            var onlyFolder = IsTrackableFolder(appFolder) ? appFolder : null;
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
                    // חלון-דו-שיח רגיל של Windows (הדפסה, "מדפיס...", הודעה) - לא מותחים למסך מלא:
                    // הוא קטן מטבעו, ומתוח הוא נראה כמו מסך לבן ענק
                    if (IsStandardDialog(hWnd))
                    {
                        if (IsIconic(hWnd)) ShowWindow(hWnd, SwRestore);
                        continue;
                    }
                    if (onlyFolder != null)
                    {
                        GetWindowThreadProcessId(hWnd, out var wpid);
                        if (!IsFromFolder((int)wpid, onlyFolder)) continue; // חלון של דרייבר / תוכנה אחרת
                    }

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

                    // ב-Windows 10/11 יש לכל חלון מסגרת שקופה (כמה פיקסלים) שנכללת בגודל
                    // החלון אך לא נראית - בלי לפצות עליה נשאר פס ריק בצדדים. מחשבים את
                    // עובי המסגרת לפי ההפרש בין גבולות החלון לגבולות הנראים שלו (DWM).
                    int mL = 0, mT = 0, mR = 0, mB = 0;
                    if (DwmGetWindowAttribute(hWnd, DwmwaExtendedFrameBounds, out Rect visible, Marshal.SizeOf<Rect>()) == 0)
                    {
                        mL = Math.Max(0, visible.Left - r.Left);
                        mT = Math.Max(0, visible.Top - r.Top);
                        mR = Math.Max(0, r.Right - visible.Right);
                        mB = Math.Max(0, r.Bottom - visible.Bottom);
                    }
                    var x = targetX - mL;
                    var y = targetY - mT;
                    var w = targetW + mL + mR;
                    var h = targetH + mT + mB;

                    var positionWrong = r.Left != x || r.Top != y || r.Right - r.Left != w || r.Bottom - r.Top != h;

                    if (positionWrong || styleChanged)
                    {
                        SetWindowPos(hWnd, IntPtr.Zero, x, y, w, h,
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

        /// <summary>
        /// מוחק "זיכרון" שתוכנות שומרות אוטומטית בין הפעלות, כדי שהמשתמש הבא לא יראה מה
        /// שהקודם עשה. נקרא בניתוק, אחרי שהתוכנות נסגרו.
        /// פנקס הרשימות של Windows 11 שומר טקסט שלא נשמר (גם בלי לשאול) ומשחזר אותו
        /// בפתיחה הבאה - נמצא בבדיקה במחשב נעול.
        /// </summary>
        public static void ClearAppSessionState()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var foldersToEmpty = new[]
            {
                System.IO.Path.Combine(localAppData, @"Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState\TabState"),
                System.IO.Path.Combine(localAppData, @"Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState\WindowState"),
            };

            foreach (var folder in foldersToEmpty)
            {
                try
                {
                    if (!System.IO.Directory.Exists(folder)) continue;
                    foreach (var file in System.IO.Directory.GetFiles(folder))
                    {
                        System.IO.File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    App.LogError($"ניקוי מצב תוכנות: {folder}", ex);
                }
            }

            ClearKioskBrowserData();
        }

        /// <summary>
        /// מוחק את נתוני הדפדפן הפנימי של הקיוסק (עוגיות, התחברויות, מטמון) - בעיקר ההתחברות
        /// לאזור האישי בנדרים פלוס: בלי זה, המשתמש הבא היה מוצא את עצמו מחובר לאזור האישי של
        /// הקודם ויכול לשלם בכרטיס השמור שלו. רשת ביטחון - חלון נדרים פלוס מוחק גם בעצמו בסגירה.
        /// זו תיקייה של הקיוסק בלבד, ולכן בטוח למחוק גם במחשב שלא ננעל.
        /// </summary>
        public static void ClearKioskBrowserData()
        {
            var folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KioskApp", "WebView2");
            if (!System.IO.Directory.Exists(folder)) return;
            // תהליכי הדפדפן נסגרים שניות ספורות אחרי החלון - כמה ניסיונות
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    System.IO.Directory.Delete(folder, recursive: true);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 4) App.LogError("ניקוי נתוני הדפדפן הפנימי", ex);
                    else System.Threading.Thread.Sleep(600);
                }
            }
        }

        /// <summary>
        /// מוחק קבצים שמשתמש שמר בתיקיות האישיות של משתמש הקיוסק (מסמכים, הורדות, שולחן עבודה,
        /// תמונות), כדי שהמשתמש הבא לא יראה אותם. רשת ביטחון - כונן C חסום ממילא בחלונות
        /// השמירה. ⚠️ פועל רק כשהקיוסק הוא ה-Shell (מחשב נעול, משתמש הקיוסק) - במחשב שלא
        /// ננעל הקיוסק רץ בחשבון רגיל, ושם אסור בשום אופן למחוק קבצים.
        /// </summary>
        public static void ClearUserFiles()
        {
            if (!App.IsRunningAsKioskShell()) return;
            if (IsCurrentUserAdministrator())
            {
                App.LogInfo("ניקוי קבצי משתמש: דילוג - המשתמש הנוכחי הוא מנהל (לא משתמש קיוסק)");
                return;
            }

            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            };

            foreach (var folder in folders)
            {
                try
                {
                    if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder)) continue;
                    var dir = new System.IO.DirectoryInfo(folder);
                    foreach (var file in dir.GetFiles())
                    {
                        if (file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        file.Attributes = System.IO.FileAttributes.Normal;
                        file.Delete();
                    }
                    foreach (var sub in dir.GetDirectories())
                    {
                        // תיקיות מערכת מיוחדות (למשל "My Music" הישנה) - קישורים, לא נוגעים
                        if ((sub.Attributes & System.IO.FileAttributes.ReparsePoint) != 0) continue;
                        sub.Delete(true);
                    }
                }
                catch (Exception ex)
                {
                    App.LogError($"ניקוי קבצי משתמש: {folder}", ex);
                }
            }
        }

        /// <summary>
        /// חבר בקבוצת המנהלים - גם כש-UAC מסנן את ההרשאות (הקבוצה מופיעה אז כ-"deny only",
        /// ולכן בודקים את רשימת הקבוצות עצמה ולא IsInRole).
        /// </summary>
        private static bool IsCurrentUserAdministrator()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var adminsSid = new System.Security.Principal.SecurityIdentifier(
                    System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
                return identity.Groups?.Contains(adminsSid) == true;
            }
            catch
            {
                return true; // בספק - מתייחסים כמנהל, כלומר לא מוחקים
            }
        }

        /// <summary>תהליך של תוכנת משתמש: לא הקיוסק, לא תהליך שרץ לפני הקיוסק, ולא רכיב מערכת</summary>
        /// <summary>
        /// התיקייה "של התוכנה" לפי קובץ ההרצה: התיקייה הראשונה מתחת לכונן (C:\OtzarApp),
        /// או מתחת ל-Program Files / ProgramData (C:\Program Files\Otzaria). תוכנות עם
        /// "Launcher" מריצות את התוכנה עצמה מקובץ אחר באותה תיקייה.
        /// </summary>
        public static string? AppFolderRoot(string? exePath)
        {
            exePath = (exePath ?? "").Trim().Trim('"');
            if (exePath.Length == 0) return null;
            var full = System.IO.Path.GetFullPath(exePath);
            var parts = full.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return System.IO.Path.GetDirectoryName(full);
            var depth = parts[1].StartsWith("Program", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            if (parts.Length <= depth) return System.IO.Path.GetDirectoryName(full);
            return string.Join(System.IO.Path.DirectorySeparatorChar.ToString(), parts.Take(depth))
                + System.IO.Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// תיקיית תוכנה שמותר "לעקוב" אחרי התהליכים שלה. לא תיקיית Windows (פנקס רשימות וכו'):
        /// משם רצים גם תהליכי מערכת של המשתמש, שאסור לסגור ושלא מעידים על התוכנה.
        /// </summary>
        private static bool IsTrackableFolder(string? folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return !string.IsNullOrEmpty(windows)
                && !folder.StartsWith(windows.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private static List<Process> ProcessesFrom(string? folder)
        {
            var result = new List<Process>();
            if (!IsTrackableFolder(folder)) return result;
            foreach (var p in Process.GetProcesses())
            {
                var keep = false;
                try
                {
                    if (p.Id != OwnPid && !IgnoredProcessNames.Contains(p.ProcessName))
                    {
                        var path = p.MainModule?.FileName;
                        keep = path != null && path.StartsWith(folder!, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    // תהליך של המערכת / של משתמש אחר - אין גישה לנתיב שלו, ולא רלוונטי
                }
                if (keep) result.Add(p); else p.Dispose();
            }
            return result;
        }

        private static bool IsStandardDialog(IntPtr hWnd)
        {
            var cls = new StringBuilder(64);
            GetClassName(hWnd, cls, cls.Capacity);
            return cls.ToString() == "#32770";
        }

        /// <summary>
        /// ליומן: החלונות הגלויים של תהליכי התוכנה (מחלקה, כותרת, סגנון, בעלים, והאם הקיוסק
        /// מזהה אותם כחלון תוכנה) - לאבחון תוכנה שלא נשארת במסך מלא.
        /// </summary>
        public static string DescribeAppWindows(string? folder)
        {
            var pids = new HashSet<int>();
            foreach (var p in ProcessesFrom(folder)) { pids.Add(p.Id); p.Dispose(); }
            var sb = new StringBuilder();
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var pidRaw);
                if (!pids.Contains((int)pidRaw) || !IsWindowVisible(hWnd)) return true;
                var cls = new StringBuilder(256);
                GetClassName(hWnd, cls, cls.Capacity);
                var title = new StringBuilder(256);
                GetWindowText(hWnd, title, title.Capacity);
                var style = GetWindowLongPtr64(hWnd, GwlStyle).ToInt64();
                var exStyle = GetWindowLongPtr64(hWnd, GwlExStyle).ToInt64();
                var owner = GetWindow(hWnd, GwOwner);
                sb.Append($" [{cls}|\"{title}\"|style={style:X}|ex={exStyle:X}|owner={(owner == IntPtr.Zero ? "-" : (IsWindowVisible(owner) ? "visible" : "hidden"))}|app={IsUserAppWindow(hWnd)}]");
                return true;
            }, IntPtr.Zero);
            return sb.Length == 0 ? " (אין חלונות גלויים)" : sb.ToString();
        }

        /// <summary>האם רץ כרגע תהליך כלשהו מתוך התיקייה הזו (התוכנה עוד נטענת / פתוחה בלי חלון)</summary>
        public static bool AnyProcessRunningFrom(string? folder)
        {
            var list = ProcessesFrom(folder);
            foreach (var p in list) p.Dispose();
            return list.Count > 0;
        }

        /// <summary>
        /// סוגר בכוח תהליכים שנשארו מתיקיית תוכנה (למשל אוצר החכמה שנתקע ברקע). תוכנות כאלה
        /// מרשות רק עותק אחד: עותק תקוע גורם לכל הפעלה חדשה "להיתקע" במסך הפתיחה.
        /// מחזיר כמה תהליכים נסגרו.
        /// </summary>
        public static int KillProcessesFrom(string? folder)
        {
            var killed = 0;
            foreach (var p in ProcessesFrom(folder))
            {
                using (p)
                {
                    try
                    {
                        p.Kill(entireProcessTree: true);
                        killed++;
                    }
                    catch
                    {
                        // כבר נסגר / אין הרשאה - ממשיכים
                    }
                }
            }
            return killed;
        }

        public static bool IsUserProcess(int pid)
        {
            if (pid == 0 || pid == OwnPid || _baselinePids.Contains(pid)) return false;
            try
            {
                using var p = Process.GetProcessById(pid);
                return !IgnoredProcessNames.Contains(p.ProcessName);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUserAppWindow(IntPtr hWnd)
        {
            if (!IsWindowVisible(hWnd)) return false;
            // חלון "בבעלות" חלון גלוי = דיאלוג (שמירה/הדפסה) - לא נוגעים. אבל יש תוכנות (אוצר
            // החכמה) שהחלון הראשי שלהן "בבעלות" חלון נסתר - זה החלון הראשי, ואותו כן אוכפים.
            var owner = GetWindow(hWnd, GwOwner);
            if (owner != IntPtr.Zero && IsWindowVisible(owner)) return false;

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
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
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
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out Rect pvAttribute, int cbAttribute);

        private const uint GwOwner = 4;
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const int SwRestore = 9;
        private const int SmCxScreen = 0;
        private const int SmCyScreen = 1;
        private const int DwmwaCloaked = 14;
        private const int DwmwaExtendedFrameBounds = 9;
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
