// עמדת הקיוסק - מסך הפתיחה: המשתמש מקליד רק תעודת זהות, והעמדה שואלת כאן האם
// כבר יש לה חשבון. יש חשבון ← העמדה מבקשת סיסמה. אין ← העמדה פותחת "פתיחת חשבון".
// לא מחזיר שום פרט על החשבון מלבד קיומו.
//
// גוף הבקשה: { username }
// תשובה: { success, exists }
const { requireStation } = require('./lib/station-auth');
const { accountExists } = require('./lib/kiosk-store');

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
    const clean = String(username || '').trim();
    if (!clean) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'יש להקליד תעודת זהות' }) };
    }
    const exists = await accountExists(event, clean);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, exists }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
