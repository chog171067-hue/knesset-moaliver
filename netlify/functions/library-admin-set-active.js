// דף ניהול - טאב הקיוסק לספרייה: השבתה/הפעלה מחדש של משתמש (חסימה זמנית
// בלי למחוק את החשבון וההיסטוריה שלו).
const { requireAdmin } = require('./lib/admin-auth');
const { setUserActive } = require('./lib/kiosk-store');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    const { userId, isActive } = JSON.parse(event.body || '{}');
    if (!userId) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר מזהה משתמש' }) };
    }

    const user = await setUserActive(event, { userId, isActive });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, user }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
