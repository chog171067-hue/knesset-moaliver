// דף ניהול - טאב "בקשות שליחת זמנים במייל": מחזיר את יומן כל הבקשות
// לשליחת לוחות זמני תפילה במייל, כולל בקשות ממי שאין לו אזור אישי כלל
// (send-schedule.js רושם כל בקשה ליומן ללא תלות בהתחברות).
const { getAdminStore } = require('./lib/blobs-store');
const { requireAdmin } = require('./lib/admin-auth');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    const store = getAdminStore();
    const list = (await store.get('email-requests.json', { type: 'json' })) || [];
    const sorted = [...list].sort((a, b) => String(b.timestamp || '').localeCompare(String(a.timestamp || '')));

    return { statusCode: 200, headers, body: JSON.stringify({ success: true, count: sorted.length, requests: sorted }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
