using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KioskApp.Core.Models;

namespace KioskApp.Services
{
    /// <summary>
    /// "תמונה נקייה" לתוכנות: בניתוק (וגם כשהקיוסק עולה) כל תיקייה שהוגדרה בדף הניהול ב"תיקיות
    /// לאיפוס בניתוק" מוחלפת בעותק הנקי שנשמר ב-C:\KioskSnapshots (Save-AppSnapshot.ps1) - כך
    /// המשתמש הבא מקבל את התוכנה כמו בפעם הראשונה (בלי היסטוריה, כרטיסיות פתוחות והערות).
    /// בטיחות: בלי עותק נקי שמור - לא נוגעים בתיקייה. תיקיות מערכת / שורש כונן - אף פעם.
    /// </summary>
    public static class AppStateReset
    {
        public const string SnapshotRoot = @"C:\KioskSnapshots";

        // איפוס שרץ ברקע (כשהקיוסק עולה) - הפעלת תוכנה מחכה לו, שלא נמחק קבצים בזמן שהיא נטענת
        private static System.Threading.Tasks.Task _pending = System.Threading.Tasks.Task.CompletedTask;

        public static void StartInBackground(IEnumerable<AllowedApp>? apps)
        {
            var list = apps?.ToList();
            _pending = System.Threading.Tasks.Task.Run(() => ResetAll(list));
        }

        /// <summary>לפני הפעלת תוכנה: מחכים לאיפוס שרץ ברקע (עד דקה)</summary>
        public static void WaitForPending()
        {
            try { _pending.Wait(TimeSpan.FromMinutes(1)); } catch { /* נרשם ביומן ב-ResetAll */ }
        }

        /// <summary>שם התיקייה של העותק הנקי - אותו חישוב כמו ב-Save-AppSnapshot.ps1</summary>
        public static string SnapshotKey(string fullPath) =>
            Regex.Replace(fullPath.TrimEnd('\\', '/'), @"[:\\/]+", "_").ToLowerInvariant();

        public static void ResetAll(IEnumerable<AllowedApp>? apps)
        {
            if (apps == null) return;
            foreach (var app in apps)
            {
                foreach (var folder in ParseFolders(app.ResetFolders))
                {
                    try
                    {
                        ResetFolder(folder, app.DisplayName);
                    }
                    catch (Exception ex)
                    {
                        App.LogError($"איפוס {app.DisplayName}: כשל באיפוס {folder}", ex);
                    }
                }
            }
        }

        /// <summary>"%APPDATA%\otzaria; C:\OtzarApp\otzar" → נתיבים מלאים (לפי חשבון הקיוסק)</summary>
        public static List<string> ParseFolders(string? value)
        {
            return (value ?? "")
                .Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => Environment.ExpandEnvironmentVariables(p.Trim().Trim('"').Trim()))
                .Where(p => p.Length > 0)
                .Select(p => { try { return Path.GetFullPath(p); } catch { return ""; } })
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSafeTarget(string path)
        {
            var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false; // לא שורש כונן ולא תיקייה ראשית (C:\Users וכו')
            string[] forbidden =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"C:\KioskApp", @"C:\KioskSource", SnapshotRoot
            };
            foreach (var f in forbidden.Where(f => !string.IsNullOrEmpty(f)))
            {
                var root = f.TrimEnd('\\') + "\\";
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || path.Equals(f.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static void ResetFolder(string target, string appName)
        {
            var snapshot = Path.Combine(SnapshotRoot, SnapshotKey(target));
            if (!Directory.Exists(snapshot))
            {
                App.LogInfo($"איפוס {appName}: אין עותק נקי שמור ל-{target} (צריך להריץ Save-AppSnapshot.ps1) - לא מאפסים");
                return;
            }
            if (!IsSafeTarget(target))
            {
                App.LogInfo($"איפוס {appName}: התיקייה {target} אסורה לאיפוס - מדלגים");
                return;
            }

            var failed = 0;
            if (Directory.Exists(target))
            {
                foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch { failed++; }
                }
                foreach (var dir in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories)
                             .OrderByDescending(d => d.Length))
                {
                    try { Directory.Delete(dir, false); } catch { /* לא ריקה - קובץ נעול */ }
                }
            }
            Directory.CreateDirectory(target);

            var copied = 0;
            foreach (var file in Directory.EnumerateFiles(snapshot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(snapshot, file);
                var dest = Path.Combine(target, relative);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, overwrite: true);
                    copied++;
                }
                catch { failed++; }
            }
            App.LogInfo($"איפוס {appName}: {target} הוחזר לעותק הנקי ({copied} קבצים{(failed > 0 ? $", {failed} נכשלו (נעולים)" : "")})");
        }
    }
}
