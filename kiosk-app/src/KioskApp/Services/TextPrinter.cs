using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KioskApp.Services
{
    /// <summary>
    /// הדפסה ושמירה כ-PDF של המסמך מעורך הטקסט (FlowDocument עם עיצוב), בעמודי A4 עם שוליים
    /// של 2.5 ס"מ - בדיוק כמו הדף שמוצג בעורך. מספר העמודים מחושב מראש (CountPages) והוא
    /// מספר העמודים שיודפסו בפועל.
    /// - הדפסה: XPS ישירות לתור ההדפסה (חדות מלאה). עוברת דרך חלון האישור והחיוב של הקיוסק.
    /// - PDF: כל עמוד מצויר כתמונה (200dpi) ונשלח ל-"Microsoft Print to PDF" עם שם קובץ קבוע -
    ///   בלי שום חלון שמירה.
    /// </summary>
    public static class TextPrinter
    {
        public const string PdfPrinterName = "Microsoft Print to PDF";

        // A4 ביחידות של WPF (1/96 אינץ'), ושוליים של 2.5 ס"מ - אותם מידות כמו הדף בעורך
        public const double PageWidth = 793.7;
        public const double PageHeight = 1122.5;
        public const double PageMargin = 94.5;

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

        /// <summary>עותק של המסמך מעובד לעמודי A4 (המקור בעורך לא משתנה)</summary>
        public static FlowDocument CloneForPrint(FlowDocument source)
        {
            var copy = new FlowDocument();
            var from = new TextRange(source.ContentStart, source.ContentEnd);
            using (var ms = new MemoryStream())
            {
                from.Save(ms, DataFormats.XamlPackage);
                ms.Position = 0;
                new TextRange(copy.ContentStart, copy.ContentEnd).Load(ms, DataFormats.XamlPackage);
            }
            copy.FontFamily = source.FontFamily;
            copy.FontSize = source.FontSize;
            copy.FlowDirection = source.FlowDirection;
            copy.TextAlignment = source.TextAlignment;
            copy.Foreground = Brushes.Black;
            copy.Background = Brushes.White;
            copy.PageWidth = PageWidth;
            copy.PageHeight = PageHeight;
            copy.PagePadding = new Thickness(PageMargin);
            copy.ColumnWidth = double.PositiveInfinity; // עמודה אחת לכל רוחב הדף
            copy.ColumnGap = 0;
            return copy;
        }

        private static DocumentPaginator Paginate(FlowDocument printDoc)
        {
            var paginator = ((IDocumentPaginatorSource)printDoc).DocumentPaginator;
            paginator.PageSize = new Size(PageWidth, PageHeight);
            paginator.ComputePageCount();
            return paginator;
        }

        /// <summary>כמה עמודים ייצאו בהדפסה (חישוב בלבד)</summary>
        public static int CountPages(FlowDocument source)
        {
            return Math.Max(1, Paginate(CloneForPrint(source)).PageCount);
        }

        /// <summary>שולח להדפסה (XPS לתור). documentName - השם של העבודה בתור. מחזיר את מספר העמודים.</summary>
        public static int Print(FlowDocument source, string printerName, string documentName)
        {
            var paginator = Paginate(CloneForPrint(source));
            using var server = new LocalPrintServer();
            using var queue = server.GetPrintQueue(printerName);
            queue.CurrentJobSettings.Description = documentName;
            var ticket = queue.DefaultPrintTicket ?? new PrintTicket();
            ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            ticket.PageOrientation = PageOrientation.Portrait;
            var writer = PrintQueue.CreateXpsDocumentWriter(queue);
            writer.Write(paginator, ticket);
            return paginator.PageCount;
        }

        /// <summary>שומר כקובץ PDF בנתיב שנבחר (דרך "Microsoft Print to PDF", בלי חלון שמירה)</summary>
        public static void SavePdf(FlowDocument source, string outputFile)
        {
            var paginator = Paginate(CloneForPrint(source));
            var images = new List<System.Drawing.Bitmap>();
            try
            {
                for (var i = 0; i < paginator.PageCount; i++)
                {
                    images.Add(RenderPage(paginator.GetPage(i)));
                }

                using var doc = new PrintDocument { DocumentName = Path.GetFileNameWithoutExtension(outputFile) };
                doc.PrinterSettings.PrinterName = PdfPrinterName;
                doc.PrinterSettings.PrintToFile = true;
                doc.PrinterSettings.PrintFileName = outputFile;
                var a4 = doc.PrinterSettings.PaperSizes.Cast<PaperSize>()
                    .FirstOrDefault(p => p.Kind == PaperKind.A4);
                if (a4 != null) doc.DefaultPageSettings.PaperSize = a4;
                doc.DefaultPageSettings.Landscape = false;
                doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
                doc.OriginAtMargins = false;
                doc.PrintController = new StandardPrintController(); // בלי חלון "מדפיס..."

                var index = 0;
                doc.BeginPrint += (_, _) => index = 0;
                doc.PrintPage += (_, e) =>
                {
                    e.Graphics!.DrawImage(images[index], e.PageBounds);
                    index++;
                    e.HasMorePages = index < images.Count;
                };
                doc.Print();
            }
            finally
            {
                foreach (var img in images) img.Dispose();
            }
        }

        /// <summary>עמוד אחד כתמונה ברזולוציה של 200dpi, על רקע לבן</summary>
        private static System.Drawing.Bitmap RenderPage(DocumentPage page)
        {
            const double dpi = 200;
            var w = (int)Math.Ceiling(PageWidth / 96 * dpi);
            var h = (int)Math.Ceiling(PageHeight / 96 * dpi);
            var rtb = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);

            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, PageWidth, PageHeight));
            }
            rtb.Render(background);
            rtb.Render(page.Visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;
            return new System.Drawing.Bitmap(ms); // ה-Bitmap מחזיק את הזרם עד שהוא נמחק
        }
    }
}
