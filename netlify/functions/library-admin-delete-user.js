// דף ניהול - טאב הקיוסק לספרייה: מחיקת משתמש לצמיתות. היסטוריית העסקאות
// שלו (transactions.json) לא נמחקת - נשארת כרשומה עצמאית, מסומנת עם
// מזהה המשתמש שכבר לא קיים, לצורך שקיפות בדוחות.
const { requireAdmin } = require('./lib/admin-auth');
const { deleteUser } = require('./lib/kiosk-store');

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
    const { userId } = JSON.parse(event.body || '{}');
    if (!userId) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר מזהה משתמש' }) };
    }

    await deleteUser(event, { userId });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
