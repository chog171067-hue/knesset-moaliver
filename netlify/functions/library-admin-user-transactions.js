// דף ניהול - טאב הקיוסק לספרייה: היסטוריית העסקאות (הטענות/חיובים) של
// משתמש ספציפי - הלוג השקוף שדורש סעיף 3.7 באפיון.
const { requireAdmin } = require('./lib/admin-auth');
const { listUserTransactions } = require('./lib/kiosk-store');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  const userId = event.queryStringParameters && event.queryStringParameters.userId;
  if (!userId) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר מזהה משתמש' }) };
  }

  try {
    const transactions = await listUserTransactions(event, userId);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, transactions }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
