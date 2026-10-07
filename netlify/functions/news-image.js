// הגשת תמונה של "מהנעשה ונשמע" שהועלתה לאתר. נקרא דרך הכתובת
// /news-images/<id> (ראו redirects ב-netlify.toml). תמונה לעולם לא משתנה אחרי
// ההעלאה (עריכה = תמונה חדשה עם מזהה חדש), ולכן נשמרת במטמון לזמן ארוך.
const { getImage } = require('./lib/news-store');

// המזהה נלקח מהנתיב עצמו (/news-images/<id>): בהפניה (rewrite) לפונקציה
// נטליפיי מעבירה לפונקציה את הבקשה המקורית, כך שפרמטרים שמוגדרים ב-to של
// ההפניה לא מגיעים אליה. ?id= נתמך לקריאה ישירה ל-/.netlify/functions/news-image.
function getImageId(event) {
  const fromQuery = (event.queryStringParameters || {}).id;
  if (fromQuery) return fromQuery;
  const m = String(event.path || '').match(/\/news-images\/([^/?#]+)/) ||
    String(event.rawUrl || '').match(/\/news-images\/([^/?#]+)/);
  return m ? m[1] : '';
}

exports.handler = async function (event) {
  const id = getImageId(event);
  try {
    const image = await getImage(event, id);
    if (!image) return { statusCode: 404, body: 'Not found' };
    return {
      statusCode: 200,
      headers: {
        'Content-Type': image.contentType,
        'Cache-Control': 'public, max-age=31536000, immutable',
        'Netlify-CDN-Cache-Control': 'public, max-age=31536000, immutable'
      },
      body: image.buffer.toString('base64'),
      isBase64Encoded: true
    };
  } catch (error) {
    return { statusCode: 500, body: 'Error' };
  }
};
