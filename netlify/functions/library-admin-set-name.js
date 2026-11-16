// דף ניהול - טאב הקיוסק לספרייה: עדכון השם המלא של משתמש קיוסק.
// השם נשלח לנדרים פלוס כ"שם משלם" בכל הטענה, ומוסדות שמחייבים שם דוחים עסקה בלעדיו.
const { requireAdmin } = require('./lib/admin-auth');
const { setDisplayName } = require('./lib/kiosk-store');

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
    const { userId, displayName } = JSON.parse(event.body || '{}');
    if (!userId) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר מזהה משתמש' }) };
    }

    const user = await setDisplayName(event, { userId, displayName });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, user }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
