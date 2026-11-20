// עמדת הקיוסק - "פתיחת חשבון", כפתור "להטענה": בודק שוב את הפרטים, שומר אותם
// כ"פתיחת חשבון ממתינה" ומקים עסקה בנדרים פלוס על סכום פתיחת החשבון (נקבע בשרת,
// מהגדרות הקיוסק - לא מהעמדה). החשבון עצמו נוצר רק כשנדרים פלוס מאשרים את התשלום
// (library-nedarim-callback ← confirmPendingTopup). העמדה בודקת את מצב התשלום
// ב-library-topup-status עם האסימון שמוחזר כאן, ואז מתחברת עם ת"ז + הסיסמה.
//
// גוף הבקשה: { tz, displayName, phone, password }
// תשובה: { success, token, transactionId, amount }
const { requireStation } = require('./lib/station-auth');
const { validateSignupDetails, createPendingSignup, getConfig } = require('./lib/kiosk-store');
const { isTzBlocked } = require('./lib/community');
const { createServerTransaction } = require('./lib/library-nedarim');

function buildCallbackUrl(event) {
  if (process.env.LIBRARY_NEDARIM_CALLBACK_URL) return process.env.LIBRARY_NEDARIM_CALLBACK_URL;
  const host = (event.headers && event.headers.host) || '';
  return `https://${host}/.netlify/functions/library-nedarim-callback`;
}

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
    const details = validateSignupDetails(JSON.parse(event.body || '{}'));
    if (await isTzBlocked(details.username, event)) {
      throw new Error('לא ניתן לפתוח חשבון עם תעודת הזהות הזאת. יש לפנות לצוות הספרייה.');
    }

    const config = await getConfig(event);
    const pending = await createPendingSignup(event, details, config.signupAmount);
    const { transactionId } = await createServerTransaction({
      amount: pending.amount,
      param1: pending.token,
      callbackUrl: buildCallbackUrl(event),
      fullName: details.displayName,
      tz: details.username,
      phone: details.phone
    });

    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({ success: true, token: pending.token, transactionId, amount: pending.amount })
    };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
