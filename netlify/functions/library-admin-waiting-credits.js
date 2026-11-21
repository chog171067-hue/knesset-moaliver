// דף ניהול - קיוסק ספרייה: תשלומים מנדרים פלוס בקטגוריית הקיוסק שלא נמצא להם חשבון
// (ת"ז שאין לה חשבון, ת"ז חסרה, מטבע זר וכו' - ראו nedarim-webhook.js).
// GET - הרשימה. POST { transactionId, username } - שיוך ידני לחשבון וטעינה ליתרה.
const { requireAdmin } = require('./lib/admin-auth');
const { listWaitingCredits, assignWaitingCredit } = require('./lib/kiosk-store');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    if (event.httpMethod === 'GET') {
      const credits = (await listWaitingCredits(event)).slice(0, 500);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, credits }) };
    }
    if (event.httpMethod === 'POST') {
      const { transactionId, username } = JSON.parse(event.body || '{}');
      if (!transactionId || !username) throw new Error('חסרים פרטים לשיוך');
      const performedBy = (context.clientContext && context.clientContext.user && context.clientContext.user.email) || '';
      const user = await assignWaitingCredit(event, { transactionId, username, performedBy });
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, user }) };
    }
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
