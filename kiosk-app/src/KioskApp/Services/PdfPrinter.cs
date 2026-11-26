using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace KioskApp.Services
{
    /// <summary>
    /// הדפסת קובץ PDF (מדיסק-און-קי) - בלי תוכנה חיצונית: כל עמוד מצויר כתמונה ע"י מנוע ה-PDF
    /// של Windows (Windows.Data.Pdf) ונשלח למדפסת. העמודים מצוירים אחד-אחד בזמן ההדפסה (לא כולם
    /// בזיכרון). עמוד לרוחב מודפס לרוחב.
    /// </summary>
    public static class PdfPrinter
    {
        private const double PrintDpi = 300;

        public static async Task<PdfDocument> OpenAsync(byte[] data)
        {
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(data.AsBuffer());
            stream.Seek(0);
            return await PdfDocument.LoadFromStreamAsync(stream);
        }

        /// <summary>מספר העמודים בקובץ. זורק שגיאה בעברית אם הקובץ פגום / מוגן בסיסמה.</summary>
        public static async Task<int> CountPagesAsync(byte[] data)
        {
            try
            {
                var doc = await OpenAsync(data);
                return (int)doc.PageCount;
            }
            catch (Exception ex)
            {
                App.LogError("PDF: פתיחת הקובץ נכשלה", ex);
                throw new InvalidOperationException("לא ניתן לפתוח את הקובץ. ייתכן שהוא פגום או מוגן בסיסמה.");
            }
        }

        /// <summary>
        /// עמוד אחד כתמונה לתצוגה (ברוחב widthPx), על רקע לבן. נקרא על ה-UI thread.
        /// </summary>
        public static async Task<System.Windows.Media.Imaging.BitmapImage> RenderPreviewAsync(PdfDocument pdf, int pageIndex, uint widthPx)
        {
            using var page = pdf.GetPage((uint)pageIndex);
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = widthPx,
                DestinationHeight = (uint)Math.Max(1, Math.Round(widthPx * page.Size.Height / page.Size.Width)),
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
            };
            var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, options);
            stream.Seek(0);
            var ms = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(ms);
            ms.Position = 0;
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }

        /// <summary>
        /// שולח את הקובץ להדפסה. documentName - שם העבודה בתור (לזיהוי החיוב המוקדם).
        /// pages - אילו עמודים (מספרים מ-0, לפי הסדר); null = כל הקובץ.
        /// </summary>
        public static Task PrintAsync(byte[] data, string printerName, string documentName, IReadOnlyList<int>? pages = null) => Task.Run(async () =>
        {
            var pdf = await OpenAsync(data);
            var order = (pages != null && pages.Count > 0)
                ? pages.Where(p => p >= 0 && p < pdf.PageCount).ToList()
                : Enumerable.Range(0, (int)pdf.PageCount).ToList();
            var count = order.Count;
            if (count == 0) return;

            using var doc = new PrintDocument { DocumentName = documentName };
            doc.PrinterSettings.PrinterName = printerName;
            var a4 = doc.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind == PaperKind.A4);
            if (a4 != null) doc.DefaultPageSettings.PaperSize = a4;
            doc.OriginAtMargins = false;
            doc.PrintController = new StandardPrintController(); // בלי חלון "מדפיס..."

            var index = 0;
            doc.BeginPrint += (_, _) => index = 0;
            doc.QueryPageSettings += (_, e) =>
            {
                using var p = pdf.GetPage((uint)order[index]);
                var size = p.Size;
                e.PageSettings.Landscape = size.Width > size.Height;
            };
            doc.PrintPage += (_, e) =>
            {
                using (var page = pdf.GetPage((uint)order[index]))
                using (var image = RenderPage(page))
                {
                    // התאמה לשטח שהמדפסת מסוגלת להדפיס (בלי לעוות את היחס). ב-OriginAtMargins=false
                    // נקודת ה-0 היא תחילת השטח הזה
                    var printable = e.PageSettings.PrintableArea;
                    var pw = (int)printable.Width;
                    var ph = (int)printable.Height;
                    if (e.PageSettings.Landscape && pw < ph) (pw, ph) = (ph, pw); // בדרייברים שלא מסובבים את השטח
                    var area = new Rectangle(0, 0, pw, ph);
                    var scale = Math.Min(area.Width / (double)image.Width, area.Height / (double)image.Height);
                    var w = (int)(image.Width * scale);
                    var h = (int)(image.Height * scale);
                    var x = area.Left + (area.Width - w) / 2;
                    var y = area.Top + (area.Height - h) / 2;
                    e.Graphics!.DrawImage(image, new Rectangle(x, y, w, h));
                }
                index++;
                e.HasMorePages = index < count;
            };
            doc.Print();
            PrinterDefaults.Ensure();
        });

        /// <summary>עמוד אחד כתמונה ב-300dpi, על רקע לבן</summary>
        private static Bitmap RenderPage(PdfPage page)
        {
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Ceiling(page.Size.Width / 96.0 * PrintDpi),
                DestinationHeight = (uint)Math.Ceiling(page.Size.Height / 96.0 * PrintDpi),
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
            };
            var stream = new InMemoryRandomAccessStream();
            page.RenderToStreamAsync(stream, options).AsTask().GetAwaiter().GetResult();
            stream.Seek(0);
            var ms = new MemoryStream();
            stream.AsStreamForRead().CopyTo(ms);
            ms.Position = 0;
            return new Bitmap(ms); // ה-Bitmap מחזיק את הזרם עד שהוא נמחק
        }
    }
}
