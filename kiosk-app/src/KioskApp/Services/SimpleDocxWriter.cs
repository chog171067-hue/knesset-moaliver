using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace KioskApp.Services
{
    /// <summary>
    /// כותב קובץ Word (.docx) מינימלי מטקסט פשוט - פסקה לכל שורה, מימין לשמאל.
    /// קובץ docx הוא בסך הכול ZIP עם כמה קובצי XML קבועים; אין צורך בספרייה חיצונית
    /// בשביל טקסט בלי עיצוב.
    /// </summary>
    public static class SimpleDocxWriter
    {
        public static void Write(string path, string text)
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(file, ZipArchiveMode.Create);

            AddEntry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>");

            AddEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>");

            var body = new StringBuilder();
            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var line in lines)
            {
                // bidi = פסקה מימין לשמאל; rtl + cs = הטקסט עצמו בעברית, בגופן ובגודל קריאים
                body.Append("<w:p><w:pPr><w:bidi/><w:jc w:val=\"both\"/></w:pPr>");
                if (line.Length > 0)
                {
                    body.Append("<w:r><w:rPr><w:rFonts w:ascii=\"David\" w:hAnsi=\"David\" w:cs=\"David\"/>" +
                                "<w:rtl/><w:sz w:val=\"28\"/><w:szCs w:val=\"28\"/></w:rPr>");
                    body.Append("<w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(CleanXmlText(line))).Append("</w:t></w:r>");
                }
                body.Append("</w:p>");
            }

            AddEntry(zip, "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                body +
                "<w:sectPr><w:bidi/></w:sectPr></w:body></w:document>");
        }

        /// <summary>תווי בקרה שאינם חוקיים ב-XML (מגיעים לפעמים בטקסט מועתק) - מוסרים; טאב נשאר</summary>
        private static string CleanXmlText(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '\t' || c >= 0x20) sb.Append(c);
            }
            return sb.ToString();
        }

        private static void AddEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}
