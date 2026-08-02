// דף ניהול - טאב הקיוסק לספרייה: הטענת יתרה ידנית ע"י הצוות (למשל תשלום
// במזומן בדלפק). מעדכן את היתרה ורושם עסקת "topup" בהיסטוריה, עם ת"ז המנהל
// שביצע את הפעולה - כדי ששקיפות ההיסטוריה (סעיף 3.7 באפיון) תהיה אמיתית.
const { requireAdmin } = require('./lib/admin-auth');
const { addManualBalance } = require('./lib/kiosk-store');

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
    const { userId, amount, description } = JSON.parse(event.body || '{}');
    if (!userId) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר מזהה משתמש' }) };
    }

    const authedUser = context.clientContext && context.clientContext.user;
    const performedBy = authedUser && authedUser.app_metadata && authedUser.app_metadata.verified_tz;

    const result = await addManualBalance(event, { userId, amount, description, performedBy });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, ...result }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
