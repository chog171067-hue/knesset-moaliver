// האזור האישי: היסטוריית עסקאות (הטענות/חיובים) של חשבון הקיוסק המקושר (לפי ת"ז).
const { requirePersonalLibraryLink } = require('./lib/personal-library-auth');
const { listUserTransactions } = require('./lib/kiosk-store');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = await requirePersonalLibraryLink(event, context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error, linked: auth.linked }) };
  }

  try {
    const transactions = await listUserTransactions(event, auth.kioskUserId);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, transactions }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
