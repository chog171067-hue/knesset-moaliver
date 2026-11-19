// עמדת הקיוסק - כפתור "שכחתי סיסמה": לפי תעודת הזהות שהמשתמש הקליד, מחזיר
// לאיזה מספר להתקשר (ימות המשיח) ואת הטלפון הרשום בחשבון בצורה מוסתרת
// (למשל 050-•••••67), כדי שהמשתמש יידע מאיזה טלפון להתקשר.
//
// גוף הבקשה: { username }
// תשובה: { success, resetPhone, found, maskedPhone }
const { requireStation } = require('./lib/station-auth');
const { getConfig, getPasswordResetInfo } = require('./lib/kiosk-store');

exports.handler = async function (event) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireStation(event);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    const { username } = JSON.parse(event.body || '{}');
    if (!username || !String(username).trim()) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'יש להקליד קודם את תעודת הזהות בשדה שם המשתמש' }) };
    }

    const config = await getConfig(event);
    const info = await getPasswordResetInfo(event, username);
    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({ success: true, resetPhone: config.passwordResetPhone || '', ...info })
    };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
