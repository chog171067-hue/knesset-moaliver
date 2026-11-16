// דף ניהול - טאב הקיוסק לספרייה: הענקה/ביטול של הרשאת "יציאה למחשב" - כפתור בעמדה
// שפותח את שולחן העבודה של Windows. מיועד למנהלים/אנשי תחזוקה מורשים בלבד.
const { requireAdmin } = require('./lib/admin-auth');
const { setUserCanExit } = require('./lib/kiosk-store');

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
    const { userId, canExitKiosk } = JSON.parse(event.body || '{}');
    if (!userId) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר מזהה משתמש' }) };
    }

    const user = await setUserCanExit(event, { userId, canExitKiosk });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, user }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
