namespace KioskApp.Services
{
    /// <summary>
    /// תקלות מדפסת - הטקסט שמוצג למשתמש בחלון "בעיה במדפסת" (עם הסבר מה לעשות), וזיהוי
    /// התקלה מתוך הטקסט של חלון "מצב הדפסה" של הדרייבר (Samsung וכו'): המדפסת מדווחת על
    /// "אין נייר" רק לתוכנה של הדרייבר, ולא ל-Windows - ולכן קוראים את החלון שלה לפני שסוגרים.
    /// </summary>
    public static class PrinterProblems
    {
        public const string PaperOut = "אין נייר במדפסת.\nיש לטעון נייר במגש המדפסת, ואז ללחוץ \"טענתי - המשך\".";
        public const string PaperJam = "נייר תקוע במדפסת.\nיש לפנות לגבאים לשחרור הנייר.";
        public const string DoorOpen = "מכסה המדפסת פתוח.\nיש לסגור את המכסה, ואז ללחוץ \"טענתי - המשך\".";
        public const string ManualFeed = "המדפסת ממתינה להזנת נייר ידנית.\nיש לטעון נייר במגש, ואז ללחוץ \"טענתי - המשך\".";
        public const string OutputFull = "מגש היציאה של המדפסת מלא.\nיש להוציא את הדפים מהמגש, ואז ללחוץ \"טענתי - המשך\".";
        public const string OutOfMemory = "זיכרון המדפסת מלא.\nיש לפנות לגבאים.";
        public const string Offline = "המדפסת כבויה או מנותקת.\nיש לבדוק שהמדפסת דלוקה ומחוברת. אם זה לא עוזר - לפנות לגבאים.";
        public const string Toner = "נגמר הטונר (הדיו) במדפסת.\nיש לפנות לגבאים.";
        public const string General = "יש תקלה במדפסת (ייתכן שנגמר הטונר, או תקלה אחרת).\nיש לפנות לגבאים.";

        /// <summary>
        /// הטקסט מחלון הדרייבר ← התקלה (או null אם זה לא חלון של תקלה, למשל "מדפיס...").
        /// הסדר חשוב: "נייר תקוע" לפני "אין נייר".
        /// </summary>
        public static string? FromDriverText(string text)
        {
            var t = (text ?? "").ToLowerInvariant();
            bool Has(params string[] words)
            {
                foreach (var w in words) if (t.Contains(w)) return true;
                return false;
            }

            if (Has("jam", "תקוע", "חסימ", "תקיע")) return PaperJam;
            if (Has("output bin full", "output tray full", "מגש היציאה מלא", "סל היציאה מלא")) return OutputFull;
            if (Has("manual feed", "הזנה ידנית")) return ManualFeed;
            // "נייר" לבד לא מספיק (יכול להיות "גודל נייר: A4") - רק עם מילה של חוסר
            if (Has("paper", "נייר", "tray", "מגש")
                && Has("out of", "empty", "no paper", "load", "add paper", "אין", "נגמר", "טען", "הוסף", "חסר", "ריק"))
                return PaperOut;
            // טונר "נמוך" זו רק אזהרה - ממשיכים להדפיס. רק טונר שנגמר / צריך להחליף
            if (Has("toner", "טונר", "דיו", "cartridge", "מחסנית", "ink")
                && Has("empty", "replace", "out of", "end of", "נגמר", "החלף", "ריק", "חסר"))
                return Toner;
            if (Has("door", "cover", "מכסה", "דלת")) return DoorOpen;
            if (Has("offline", "disconnected", "not connected", "מנותק", "לא מחובר", "כבוי")) return Offline;
            if (Has("memory", "זיכרון")) return OutOfMemory;
            if (Has("error", "שגיאה", "תקלה", "fail", "נכשל")) return General;
            return null;
        }
    }
}
