using System;
using System.Collections.Generic;
using System.Linq;

namespace KioskApp.Services
{
    public enum PrintAreaItemKind { SpoolJob, EditorDocument, PdfFile }

    /// <summary>הדפסה שממתינה באזור ההדפסות</summary>
    public class PrintAreaItem
    {
        public Guid Id { get; } = Guid.NewGuid();
        public PrintAreaItemKind Kind { get; init; }
        /// <summary>שם ההדפסה (שם המסמך שהתוכנה נתנה)</summary>
        public string Title { get; init; } = "";
        /// <summary>מאיזו תוכנה (אוצר החכמה, אוצריא, עורך טקסט...)</summary>
        public string Source { get; init; } = "";
        /// <summary>כמה עמודים יודפסו (ובהתאם - החיוב)</summary>
        public int Pages { get; set; }
        /// <summary>PdfFile: כמה עמודים בקובץ כולו</summary>
        public int TotalPages { get; init; }
        /// <summary>PdfFile: אילו עמודים להדפיס (מספרים מ-0, לפי הסדר). null = כל הקובץ</summary>
        public List<int>? SelectedPages { get; set; }
        public DateTime AddedAt { get; } = DateTime.Now;

        // SpoolJob: העבודה עצורה בתור של המדפסת עד שמדפיסים אותה
        public string PrinterName { get; init; } = "";
        public int JobId { get; init; }

        // EditorDocument: המסמך מעורך הטקסט (XamlPackage); PdfFile: תוכן קובץ ה-PDF (מדיסק-און-קי).
        // נשמר בזיכרון בלבד, ונשלח למדפסת רק כשמדפיסים
        public byte[]? DocumentData { get; init; }

        /// <summary>טקסט לחיוב / להיסטוריה: "מקור - שם"</summary>
        public string ChargeTitle => string.IsNullOrWhiteSpace(Title) ? Source : $"{Source} - {Title}";
    }

    /// <summary>
    /// אזור ההדפסות של ההתחברות הנוכחית: הדפסות שהמשתמש בחר "הוספה לאזור ההדפסות" - עוד לא חויבו
    /// ועוד לא הודפסו. בניתוק הכול נמחק (עבודות שעצורות בתור מבוטלות).
    /// </summary>
    public static class PrintArea
    {
        private static readonly List<PrintAreaItem> _items = new();
        private static readonly object Lock = new();

        public static event Action? Changed;

        public static IReadOnlyList<PrintAreaItem> Items
        {
            get { lock (Lock) return _items.ToList(); }
        }

        public static int Count
        {
            get { lock (Lock) return _items.Count; }
        }

        public static void Add(PrintAreaItem item)
        {
            lock (Lock) _items.Add(item);
            App.LogInfo($"אזור הדפסות: נוסף \"{item.ChargeTitle}\" ({item.Pages} עמודים)");
            RaiseChanged();
        }

        /// <summary>ההדפסה השתנתה (למשל נבחרו עמודים) - מרעננים את התצוגה</summary>
        public static void Updated(PrintAreaItem item)
        {
            App.LogInfo($"אזור הדפסות: \"{item.ChargeTitle}\" - {item.Pages} עמודים להדפסה");
            RaiseChanged();
        }

        /// <summary>הוצאה מהרשימה (אחרי הדפסה) - בלי לבטל את העבודה</summary>
        public static void Remove(PrintAreaItem item)
        {
            lock (Lock) _items.RemoveAll(i => i.Id == item.Id);
            RaiseChanged();
        }

        /// <summary>מחיקה (פח): עבודה שעצורה בתור - מבוטלת</summary>
        public static void Delete(PrintAreaItem item)
        {
            if (item.Kind == PrintAreaItemKind.SpoolJob) PrintMonitorService.CancelJob(item.PrinterName, item.JobId);
            Remove(item);
            App.LogInfo($"אזור הדפסות: נמחק \"{item.ChargeTitle}\"");
        }

        /// <summary>בניתוק: מוחקים הכול (העבודות שבתור מבוטלות)</summary>
        public static void Clear()
        {
            List<PrintAreaItem> all;
            lock (Lock)
            {
                all = _items.ToList();
                _items.Clear();
            }
            foreach (var item in all.Where(i => i.Kind == PrintAreaItemKind.SpoolJob))
            {
                PrintMonitorService.CancelJob(item.PrinterName, item.JobId);
            }
            if (all.Count > 0) App.LogInfo($"אזור הדפסות: נמחקו {all.Count} הדפסות בניתוק");
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            try { System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Changed?.Invoke())); }
            catch { /* בסגירה */ }
        }
    }
}
