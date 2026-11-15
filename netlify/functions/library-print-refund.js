// עמדת הקיוסק קוראת לכאן כשעבודת הדפסה שכבר חויבה נכשלה ובוטלה (מדפסת מנותקת,
// נגמר נייר, תקלה) - כדי להחזיר למשתמש את מה שחויב. ראו refundPrintCharge.
const { requireStation } = require('./lib/station-auth');
const { refundPrintCharge } = require('./lib/kiosk-store');

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
    const { userId, transactionId } = JSON.parse(event.body || '{}');
    if (!userId || !transactionId) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסרים פרטי החזר (userId/transactionId)' }) };
    }

    const result = await refundPrintCharge(event, { userId, transactionId });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, ...result }) };
  } catch (error) {
    const statusCode = error.alreadyRefunded ? 409 : 400;
    return { statusCode, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
