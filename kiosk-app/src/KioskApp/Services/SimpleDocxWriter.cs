using System;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace KioskApp.Services
{
    /// <summary>
    /// כותב קובץ Word (.docx) מהמסמך שבעורך הטקסט, עם העיצוב הפשוט שלו: מודגש, קו תחתון, גודל
    /// גופן ויישור פסקה (ימין / מרכז / שמאל / מיושר), מימין לשמאל, בעמוד A4 עם שוליים של 2.5 ס"מ.
    /// קובץ docx הוא בסך הכול ZIP עם כמה קובצי XML - אין צורך בספרייה חיצונית.
    /// </summary>
    public static class SimpleDocxWriter
    {
        private const string Font = "David";

        public static void Write(string path, FlowDocument document)
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
            foreach (var block in document.Blocks) AppendBlock(body, block);
            if (body.Length == 0) body.Append("<w:p/>");

            // A4 (11906x16838 twips), שוליים 2.5 ס"מ (1418 twips), עמוד מימין לשמאל
            AddEntry(zip, "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                body +
                "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
                "<w:pgMar w:top=\"1418\" w:right=\"1418\" w:bottom=\"1418\" w:left=\"1418\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/>" +
                "<w:bidi/></w:sectPr></w:body></w:document>");
        }

        private static void AppendBlock(StringBuilder body, Block block)
        {
            switch (block)
            {
                case Paragraph p:
                    AppendParagraph(body, p);
                    break;
                case Section s:
                    foreach (var b in s.Blocks) AppendBlock(body, b);
                    break;
                case List list:
                    foreach (var item in list.ListItems)
                        foreach (var b in item.Blocks) AppendBlock(body, b);
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                            foreach (var cell in row.Cells)
                                foreach (var b in cell.Blocks) AppendBlock(body, b);
                    break;
                default:
                    // רכיב אחר (תמונה וכו') - רק הטקסט שבו, אם יש
                    var text = new TextRange(block.ContentStart, block.ContentEnd).Text;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        body.Append("<w:p><w:pPr><w:bidi/></w:pPr>");
                        AppendRun(body, text, block.FontWeight, null, block.FontSize);
                        body.Append("</w:p>");
                    }
                    break;
            }
        }

        private static void AppendParagraph(StringBuilder body, Paragraph p)
        {
            // בפסקה מימין לשמאל: "left" = צמוד להתחלה (ימין), "right" = לסוף (שמאל) - כמו ב-WPF
            var jc = p.TextAlignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "both",
                _ => "left"
            };
            body.Append("<w:p><w:pPr><w:bidi/><w:jc w:val=\"").Append(jc).Append("\"/></w:pPr>");
            foreach (var inline in p.Inlines) AppendInline(body, inline);
            body.Append("</w:p>");
        }

        private static void AppendInline(StringBuilder body, Inline inline)
        {
            switch (inline)
            {
                case Run run:
                    AppendRun(body, run.Text, run.FontWeight, run.TextDecorations, run.FontSize);
                    break;
                case LineBreak:
                    body.Append("<w:r><w:br/></w:r>");
                    break;
                case Span span: // Bold / Underline / Italic / Hyperlink וכו'
                    foreach (var child in span.Inlines) AppendInline(body, child);
                    break;
                default:
                    var text = new TextRange(inline.ContentStart, inline.ContentEnd).Text;
                    if (!string.IsNullOrEmpty(text)) AppendRun(body, text, inline.FontWeight, inline.TextDecorations, inline.FontSize);
                    break;
            }
        }

        private static void AppendRun(StringBuilder body, string text, FontWeight weight,
            TextDecorationCollection? decorations, double fontSizeDip)
        {
            if (string.IsNullOrEmpty(text)) return;
            // גודל ב-Word בחצאי נקודות; ב-WPF הגודל ביחידות של 1/96 אינץ' (נקודה = 4/3 יחידות)
            var halfPoints = Math.Max(2, (int)Math.Round(fontSizeDip * 0.75 * 2));
            var bold = weight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight();
            var underline = decorations != null && decorations.Count > 0;

            body.Append("<w:r><w:rPr><w:rFonts w:ascii=\"").Append(Font).Append("\" w:hAnsi=\"").Append(Font)
                .Append("\" w:cs=\"").Append(Font).Append("\"/>");
            if (bold) body.Append("<w:b/><w:bCs/>");
            if (underline) body.Append("<w:u w:val=\"single\"/>");
            body.Append("<w:rtl/><w:sz w:val=\"").Append(halfPoints).Append("\"/><w:szCs w:val=\"").Append(halfPoints).Append("\"/></w:rPr>");

            // ירידות שורה בתוך הטקסט - <w:br/>
            var parts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) body.Append("<w:br/>");
                if (parts[i].Length > 0)
                    body.Append("<w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(CleanXmlText(parts[i]))).Append("</w:t>");
            }
            body.Append("</w:r>");
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
