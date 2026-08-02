// דף ניהול - טאב הקיוסק לספרייה: רשימת כל המשתמשים הרשומים בעמדה (שם
// משתמש, יתרה, סטטוס), ללא כל פרט סיסמה.
const { requireAdmin } = require('./lib/admin-auth');
const { listUsers } = require('./lib/kiosk-store');

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
    const users = await listUsers(event);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, users }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
