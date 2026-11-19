using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;

namespace KioskApp.Services
{
    /// <summary>
    /// מדפיס טקסט פשוט (מעורך הטקסט) - למדפסת של הספרייה, או לקובץ PDF דרך "Microsoft Print to PDF"
    /// (מובנית ב-Windows 10/11) בלי שום חלון שמירה. הטקסט מימין לשמאל, מחולק לעמודים אוטומטית.
    /// הדפסה רגילה עוברת דרך תור ההדפסה של Windows, ולכן מחויבת ומוחזרת כמו כל הדפסה אחרת.
    /// </summary>
    public static class TextPrinter
    {
        public const string PdfPrinterName = "Microsoft Print to PDF";

        /// <summary>המדפסת שתשמש להדפסה: ברירת המחדל של Windows, אם היא מדפסת אמיתית</summary>
        public static string? DefaultPhysicalPrinter()
        {
            var settings = new PrinterSettings();
            var name = settings.PrinterName;
            if (string.IsNullOrEmpty(name) || !settings.IsValid || PrintMonitorService.IsVirtualPrinter(name)) return null;
            return name;
        }

        public static bool PdfPrinterAvailable() =>
            PrinterSettings.InstalledPrinters.Cast<string>().Any(p => p.Equals(PdfPrinterName, StringComparison.OrdinalIgnoreCase));

        /// <summary>כמה עמודים ייצאו (חישוב "יבש", בלי לשלוח למדפסת)</summary>
        public static int CountPages(string text, string printerName)
        {
            var pages = 0;
            using var doc = CreateDocument(text, printerName, outputFile: null);
            doc.PrintController = new PreviewPrintController();
            doc.PrintPage += (_, _) => pages++;
            doc.Print();
            return pages;
        }

        /// <summary>שולח להדפסה. outputFile - רק עבור מדפסת PDF: הנתיב שבו הקובץ יישמר.</summary>
        public static void Print(string text, string printerName, string? outputFile = null)
        {
            using var doc = CreateDocument(text, printerName, outputFile);
            doc.PrintController = new StandardPrintController(); // בלי חלון "מדפיס..." של Windows
            doc.Print();
        }

        private static PrintDocument CreateDocument(string text, string printerName, string? outputFile)
        {
            var content = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
            var doc = new PrintDocument { DocumentName = "קיוסק הספרייה - עורך טקסט" };
            doc.PrinterSettings.PrinterName = printerName;
            if (outputFile != null)
            {
                doc.PrinterSettings.PrintToFile = true;
                doc.PrinterSettings.PrintFileName = outputFile;
            }

            var font = new Font("David", 14f);
            var format = new StringFormat(StringFormatFlags.DirectionRightToLeft)
            {
                Alignment = StringAlignment.Near,     // בכיוון מימין לשמאל: צמוד לימין
                Trimming = StringTrimming.Word
            };
            var offset = 0;

            doc.BeginPrint += (_, _) => offset = 0;
            doc.PrintPage += (_, e) =>
            {
                var g = e.Graphics!;
                var area = e.MarginBounds;
                var remaining = content.Substring(offset);

                int charsFitted;
                int linesFilled;
                g.MeasureString(remaining, font, new SizeF(area.Width, area.Height), format,
                    out charsFitted, out linesFilled);
                if (charsFitted <= 0) charsFitted = Math.Min(remaining.Length, 1); // הגנה מלולאה אינסופית

                g.DrawString(remaining.Substring(0, charsFitted), font, Brushes.Black, area, format);
                offset += charsFitted;
                e.HasMorePages = offset < content.Length;
            };
            doc.Disposed += (_, _) =>
            {
                font.Dispose();
                format.Dispose();
            };
            return doc;
        }
    }
}
