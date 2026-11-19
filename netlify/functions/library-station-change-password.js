// עמדת הקיוסק: המשתמש קובע לעצמו סיסמה חדשה. משמש בעיקר אחרי כניסה עם סיסמה
// זמנית שקיבל בטלפון (ראו library-yemot-password-reset.js) - שם העמדה חייבת
// להחליף סיסמה לפני שהקיוסק נפתח.
//
// גוף הבקשה: { username, currentPassword, newPassword }
// תשובה: { success: true, user } - ומכאן העמדה ממשיכה כמו בכניסה רגילה.
const { requireStation } = require('./lib/station-auth');
const { authenticateKioskUser, changeOwnPassword } = require('./lib/kiosk-store');

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
    const { username, currentPassword, newPassword } = JSON.parse(event.body || '{}');
    if (!username || !currentPassword || !newPassword) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסרים פרטים' }) };
    }

    // קודם אימות "רגיל" - הוא גם סופר ניסיונות שגויים מול סיסמה זמנית, כך שאי אפשר
    // לעקוף את מגבלת הניסיונות של מסך הכניסה דרך הכתובת הזו.
    const check = await authenticateKioskUser(event, { username, password: currentPassword });
    if (!check.ok) {
      return { statusCode: 401, headers, body: JSON.stringify({ success: false, error: 'הסיסמה הזמנית שגויה או שפג תוקפה - יש להתקשר שוב לקבלת סיסמה חדשה' }) };
    }

    const user = await changeOwnPassword(event, { username, currentPassword, newPassword });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, user }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
