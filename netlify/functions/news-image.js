// הגשת תמונה של "מהנעשה ונשמע" שהועלתה לאתר. נקרא דרך הכתובת
// /news-images/<id> (ראו redirects ב-netlify.toml). תמונה לעולם לא משתנה אחרי
// ההעלאה (עריכה = תמונה חדשה עם מזהה חדש), ולכן נשמרת במטמון לזמן ארוך.
const { getImage } = require('./lib/news-store');

exports.handler = async function (event) {
  const id = (event.queryStringParameters || {}).id;
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
