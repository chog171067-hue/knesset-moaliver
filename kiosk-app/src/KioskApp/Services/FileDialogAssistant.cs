using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace KioskApp.Services
{
    /// <summary>
    /// ממלא ומאשר בעצמו חלונות "שמירה" / "בחירת תיקייה" שתוכנות פותחות (למשל שמירת עמוד
    /// כ-PDF באוצריא): בעמדה ציבורית אסור שמשתמש ידפדף בקבצי המחשב, ומבחינתו הקובץ פשוט
    /// נשמר לדיסק-און-קי המחובר. אם אין דיסק-און-קי - החלון נסגר והמשתמש מקבל הסבר.
    /// גם חלון "Restrictions" של Windows (נפתח כשחלון קבצים מנסה להציג כונן חסום) נסגר.
    ///
    /// חלונות "פתיחת קובץ" לא מטופלים - שם המשתמש צריך לבחור קובץ (מדיסק-און-קי; שאר
    /// הכוננים חסומים בסקריפט הנעילה).
    ///
    /// נקרא כל חצי שנייה משומר החלונות. עובד עם UI Automation של Windows - אותו מנגנון
    /// שמשמש קוראי מסך - ולכן לא תלוי בתוכנה הספציפית שפתחה את החלון.
    /// </summary>
    public static class FileDialogAssistant
    {
        /// <summary>הודעה למשתמש (מוצגת ע"י ה-Overlay)</summary>
        public static event Action<string>? UserMessage;

        private static readonly HashSet<IntPtr> Handled = new();
        private static readonly Dictionary<IntPtr, int> Attempts = new();
        private static readonly int OwnPid = Environment.ProcessId;

        private enum DialogKind { None, Save, Folder, Open }

        public static void Tick(Func<int, bool> isUserProcess)
        {
            foreach (var hWnd in FindDialogs(isUserProcess))
            {
                if (Handled.Contains(hWnd)) continue;
                try
                {
                    if (HandleDialog(hWnd))
                    {
                        Handled.Add(hWnd);
                        Attempts.Remove(hWnd);
                    }
                    else
                    {
                        // החלון עוד נטען - מנסים שוב בסבב הבא, אבל לא לנצח
                        Attempts[hWnd] = Attempts.TryGetValue(hWnd, out var n) ? n + 1 : 1;
                        if (Attempts[hWnd] > 10)
                        {
                            Handled.Add(hWnd);
                            App.LogInfo("חלון קבצים: לא זוהה מבנה החלון - משאירים אותו כמו שהוא");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Handled.Add(hWnd);
                    App.LogError("חלון קבצים: כשל בטיפול", ex);
                }
            }

            // ניקוי חלונות שכבר נסגרו
            Handled.RemoveWhere(h => !IsWindow(h));
        }

        private static bool HandleDialog(IntPtr hWnd)
        {
            var title = GetTitle(hWnd);

            // הודעת "Restrictions" של Windows - סוגרים
            if (title.Equals("Restrictions", StringComparison.OrdinalIgnoreCase) || title == "הגבלות")
            {
                PostMessage(hWnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                return true;
            }

            var root = AutomationElement.FromHandle(hWnd);
            var okButton = FindByAutomationId(root, "1", ControlType.Button);
            var cancelButton = FindByAutomationId(root, "2", ControlType.Button);
            var nameBox = FindNameEdit(root);
            if (okButton == null || nameBox == null) return false; // עוד לא נטען / לא חלון קבצים

            var kind = Classify(okButton.Current.Name, title);
            if (kind == DialogKind.Open || kind == DialogKind.None) return true; // לא נוגעים

            var usb = FindUsbDrive();
            if (usb == null)
            {
                Invoke(cancelButton);
                if (cancelButton == null) PostMessage(hWnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                App.LogInfo("חלון קבצים: אין דיסק-און-קי - החלון נסגר");
                UserMessage?.Invoke("כדי לשמור קבצים יש לחבר דיסק-און-קי לעמדה, ולנסות שוב.");
                return true;
            }

            string target;
            if (kind == DialogKind.Folder)
            {
                target = usb;
            }
            else
            {
                var current = GetValue(nameBox);
                var fileName = Path.GetFileName(current ?? "");
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "קובץ";
                target = UniquePath(usb, fileName);
            }

            if (!SetValue(nameBox, target)) return false;
            Invoke(okButton);
            App.LogInfo($"חלון קבצים: נשמר אוטומטית אל {target}");
            UserMessage?.Invoke(kind == DialogKind.Folder
                ? $"הקובץ נשמר בדיסק-און-קי ({usb})."
                : $"הקובץ נשמר בדיסק-און-קי:\n{target}");
            return true;
        }

        private static DialogKind Classify(string okName, string title)
        {
            var text = (okName + " " + title).ToLowerInvariant();
            if (text.Contains("folder") || text.Contains("תיקי")) return DialogKind.Folder;
            if (text.Contains("save") || text.Contains("שמור") || text.Contains("שמיר")) return DialogKind.Save;
            if (text.Contains("open") || text.Contains("פתח") || text.Contains("פתיח")) return DialogKind.Open;
            return DialogKind.None;
        }

        /// <summary>הדיסק-און-קי הראשון שמחובר ומוכן (האות משתנה ממחשב למחשב - מזהים בזמן אמת)</summary>
        private static string? FindUsbDrive()
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .FirstOrDefault();
        }

        /// <summary>לא דורסים קובץ קיים בדיסק-און-קי: "שם (2).pdf", "שם (3).pdf"...</summary>
        private static string UniquePath(string folder, string fileName)
        {
            var path = Path.Combine(folder, fileName);
            if (!File.Exists(path)) return path;
            var name = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            for (var i = 2; i < 1000; i++)
            {
                path = Path.Combine(folder, $"{name} ({i}){ext}");
                if (!File.Exists(path)) return path;
            }
            return Path.Combine(folder, $"{name} ({Guid.NewGuid():N}){ext}");
        }

        // --- UI Automation ---

        private static AutomationElement? FindByAutomationId(AutomationElement root, string id, ControlType type)
        {
            return root.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, id),
                new PropertyCondition(AutomationElement.ControlTypeProperty, type)));
        }

        /// <summary>
        /// שדה "שם קובץ" / "תיקייה" (בחלונות הקבצים של Windows מזהה 1001; ליתר ביטחון - כל שדה
        /// טקסט שאינו תיבת החיפוש או שורת הכתובת).
        /// </summary>
        private static AutomationElement? FindNameEdit(AutomationElement root)
        {
            var byId = FindByAutomationId(root, "1001", ControlType.Edit);
            if (byId != null) return byId;

            var edits = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            foreach (AutomationElement e in edits)
            {
                var name = e.Current.Name ?? "";
                if (name.Contains("Search", StringComparison.OrdinalIgnoreCase) || name.Contains("חיפוש")) continue;
                if (name.Contains("Address", StringComparison.OrdinalIgnoreCase) || name.Contains("כתובת")) continue;
                if (name.TrimEnd().EndsWith(":")) return e;
            }
            return null;
        }

        private static string? GetValue(AutomationElement e)
        {
            return e.TryGetCurrentPattern(ValuePattern.Pattern, out var p) ? ((ValuePattern)p).Current.Value : null;
        }

        private static bool SetValue(AutomationElement e, string value)
        {
            if (!e.TryGetCurrentPattern(ValuePattern.Pattern, out var p)) return false;
            ((ValuePattern)p).SetValue(value);
            return true;
        }

        private static void Invoke(AutomationElement? button)
        {
            if (button != null && button.TryGetCurrentPattern(InvokePattern.Pattern, out var p))
            {
                ((InvokePattern)p).Invoke();
            }
        }

        // --- Win32 ---

        /// <summary>חלונות דיאלוג (#32770) גלויים של תוכנות המשתמש (גם כאלה שה"בעלים" שלהם הוא חלון התוכנה)</summary>
        private static List<IntPtr> FindDialogs(Func<int, bool> isUserProcess)
        {
            var result = new List<IntPtr>();
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                var cls = new StringBuilder(64);
                GetClassName(hWnd, cls, cls.Capacity);
                if (cls.ToString() != "#32770") return true;
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pid == 0 || pid == OwnPid || !isUserProcess((int)pid)) return true;
                result.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static string GetTitle(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString().Trim();
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private const uint WmClose = 0x0010;
    }
}
