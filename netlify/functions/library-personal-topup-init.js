// האזור האישי: כפתור "הטענת כסף" - זהה בדיוק לזרימה שבעמדת הקיוסק
// (library-topup-init.js), רק שה-userId לא מגיע מגוף הבקשה אלא נגזר אך ורק
// מהחשבון המקושר של המשתמש המחובר - כדי שאי אפשר יהיה לטעון כסף לחשבון
// קיוסק של מישהו אחר דרך קריאת API ישירה.
const { requirePersonalLibraryLink } = require('./lib/personal-library-auth');
const { createPendingTopup } = require('./lib/kiosk-store');
const { createServerTransaction } = require('./lib/library-nedarim');

function buildCallbackUrl(event) {
  if (process.env.LIBRARY_NEDARIM_CALLBACK_URL) return process.env.LIBRARY_NEDARIM_CALLBACK_URL;
  const host = (event.headers && event.headers.host) || '';
  return `https://${host}/.netlify/functions/library-nedarim-callback`;
}

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requirePersonalLibraryLink(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error, linked: auth.linked }) };
  }

  try {
    const { amount } = JSON.parse(event.body || '{}');
    if (!amount) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר סכום הטענה' }) };
    }

    const pending = await createPendingTopup(event, { userId: auth.kioskUserId, amount });
    const { transactionId } = await createServerTransaction({
      amount: pending.amount,
      param1: pending.token,
      callbackUrl: buildCallbackUrl(event)
    });

    return { statusCode: 200, headers, body: JSON.stringify({ success: true, token: pending.token, transactionId }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
