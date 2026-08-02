namespace KioskApp.Services
{
    /// <summary>
    /// דף HTML מקומי זעיר, נטען ישירות לתוך ה-WebView2 (לא דרך אתר), שמטמיע בתוכו את
    /// האייפרם האמיתי של נדרים פלוס - בדיוק כמו שה-personal.html באתר עושה. הסיבה
    /// שאי אפשר לנווט את ה-WebView2 ישירות לכתובת האייפרם: הסקריפט של נדרים פלוס
    /// מאזין להודעות עם window.addEventListener('message', ...) - זה עובד בדפדפן רגיל
    /// (שם דף האתר החיצוני הוא "ההורה" ששולח postMessage לאייפרם), אבל לא כשה-WebView2
    /// עצמו הוא ה"הורה": התקשורת בין קוד C# ל-WebView2 עוברת דרך ערוץ נפרד לגמרי
    /// (window.chrome.webview.postMessage), שהסקריפט של נדרים פלוס לא מכיר. הדף הזה
    /// מגשר בין שני הערוצים: מצד אחד מטמיע אייפרם אמיתי ומדבר איתו ב-postMessage רגיל
    /// (בדיוק כמו שנדרים פלוס מצפים), ומצד שני משדר הכל הלאה ל-C# דרך chrome.webview.
    /// </summary>
    public static class NedarimWrapperHtml
    {
        public const string Content = """
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="margin:0; padding:0;">
        <iframe id="NedarimFrame" style="width:100%; border:none; height:100vh;" scrolling="no"></iframe>
        <script>
          var frame = document.getElementById('NedarimFrame');

          // הודעות שמגיעות מהאייפרם האמיתי של נדרים פלוס -> מעבירים אותן הלאה ל-C#
          window.addEventListener('message', function (event) {
            if (!event.data || !event.data.Name) return;
            if (event.data.Name === 'Height' || event.data.Name === 'TransactionResponse') {
              window.chrome.webview.postMessage(event.data);
            }
          });

          // פקודות שמגיעות מ-C# -> מעבירים אותן הלאה לאייפרם האמיתי
          window.chrome.webview.addEventListener('message', function (event) {
            var msg = event.data;
            if (!msg || !msg.Name) return;
            if (msg.Name === 'StartTransaction') {
              frame.contentWindow.postMessage({ Name: 'FinishTransaction', Value: msg.TransactionId }, '*');
            }
          });

          frame.onload = function () {
            frame.contentWindow.postMessage({ Name: 'GetHeight' }, '*');
          };
          frame.src = 'https://www.matara.pro/nedarimplus/iframe/';
        </script>
        </body>
        </html>
        """;
    }
}
