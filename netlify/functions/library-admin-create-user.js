// דף ניהול - טאב הקיוסק לספרייה: יצירת משתמש חדש לעמדה (תעודת זהות כשם
// המשתמש + סיסמה ראשונית שהצוות קובע, ושם מלא לתצוגה ולשליחה לנדרים פלוס
// בהטענת כסף - ראו library-topup-init.js). היתרה ההתחלתית תמיד 0.
const { requireAdmin } = require('./lib/admin-auth');
const { createUser } = require('./lib/kiosk-store');

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
    const { username, password, displayName } = JSON.parse(event.body || '{}');
    const user = await createUser(event, { username, password, displayName });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, user }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
