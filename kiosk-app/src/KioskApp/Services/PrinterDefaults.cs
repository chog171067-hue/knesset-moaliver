using System;
using System.Drawing.Printing;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace KioskApp.Services
{
    /// <summary>
    /// מדפסת ברירת המחדל של חשבון הקיוסק תמיד מדפסת אמיתית (Samsung):
    /// ב-Windows 11, "Windows ינהל את מדפסת ברירת המחדל" הופך את המדפסת האחרונה שהשתמשו בה
    /// לברירת המחדל - אחרי שמירת PDF מעורך הטקסט זו הייתה "Microsoft Print to PDF", ואוצר החכמה
    /// סירב להדפיס ("Printer not allowed"). כאן מכבים את ההגדרה הזו ומחזירים את המדפסת האמיתית.
    /// נקרא בכל כניסה לחשבון ואחרי כל שמירת PDF.
    /// </summary>
    public static class PrinterDefaults
    {
        public static void Ensure()
        {
            try
            {
                // "Windows ינהל את מדפסת ברירת המחדל" - כבוי (למשתמש הנוכחי = חשבון הקיוסק)
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Windows"))
                {
                    if (!(key.GetValue("LegacyDefaultPrinterMode") is int mode && mode == 1))
                    {
                        key.SetValue("LegacyDefaultPrinterMode", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogError("מדפסת ברירת מחדל: כשל בכיבוי 'Windows ינהל את מדפסת ברירת המחדל'", ex);
            }

            try
            {
                var current = new PrinterSettings().PrinterName ?? "";
                if (current.Length > 0 && !PrintMonitorService.IsVirtualPrinter(current)) return; // כבר מדפסת אמיתית

                var physical = PrinterSettings.InstalledPrinters.Cast<string>()
                    .FirstOrDefault(p => !PrintMonitorService.IsVirtualPrinter(p));
                if (physical == null)
                {
                    App.LogInfo("מדפסת ברירת מחדל: לא נמצאה מדפסת אמיתית במחשב");
                    return;
                }

                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Printer WHERE Name = '{physical.Replace("\\", "\\\\").Replace("'", "\\'")}'");
                foreach (ManagementObject printer in searcher.Get())
                {
                    using (printer) printer.InvokeMethod("SetDefaultPrinter", null);
                }
                App.LogInfo($"מדפסת ברירת מחדל: הוחלפה מ-\"{current}\" ל-\"{physical}\"");
            }
            catch (Exception ex)
            {
                App.LogError("מדפסת ברירת מחדל: כשל בהגדרה", ex);
            }
        }
    }
}
