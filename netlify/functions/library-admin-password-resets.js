// דף ניהול - טאב הקיוסק לספרייה: יומן שחזורי הסיסמה בטלפון (ימות המשיח).
// כל ניסיון נרשם - הצלחה, אי-התאמה, חסימה, ביטול, והחלפת הסיסמה בעמדה.
const { requireAdmin } = require('./lib/admin-auth');
const { listPasswordResetLog } = require('./lib/kiosk-store');

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
    const entries = (await listPasswordResetLog(event)).slice(0, 500);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, entries }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
