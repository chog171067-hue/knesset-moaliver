// דף ניהול - טאב "מהנעשה ונשמע": העלאת תמונה בודדת לאתר (אחרי הקטנה וכיווץ
// בדפדפן). מחזיר imageId שנשמר ברשימת ה-media של האירוע (ראו news-store.js).
// POST עם { data: <base64>, contentType }.
const { saveImage } = require('./lib/news-store');
const { requireAdmin } = require('./lib/admin-auth');

exports.handler = async function (event, context) {
  const headers = { 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    const { data, contentType } = JSON.parse(event.body || '{}');
    const imageId = await saveImage(event, data, contentType);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, imageId }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
