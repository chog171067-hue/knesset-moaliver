// עמדת הקיוסק - "תשלום בכרטיס שמור (אזור אישי בנדרים פלוס)": מכין תשלום בדף הרגיל של
// נדרים פלוס (שם עובד האזור האישי עם הכרטיסים השמורים), ומחזיר את הקישור אליו.
// האסימון (token) נרשם בהערות העסקה, ולפיו ה-Webhook (nedarim-webhook.js) טוען את
// הסכום לחשבון הנכון. העמדה בודקת את המצב ב-library-topup-status עם אותו אסימון.
//
// גוף הבקשה - הטענה לחשבון קיים: { userId, amount }
//            - פתיחת חשבון:       { signup: { tz, displayName, phone, password } }
// תשובה: { success, token, url, amount }
const { requireStation } = require('./lib/station-auth');
const {
  createPendingTopup, getUserById, validateSignupDetails, createPendingSignup, getConfig
} = require('./lib/kiosk-store');
const { isTzBlocked } = require('./lib/community');
const { buildOnlinePaymentUrl } = require('./lib/library-nedarim');

function siteUrl(event) {
  const host = (event.headers && event.headers.host) || '';
  return `https://${host}`;
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
    const body = JSON.parse(event.body || '{}');
    let pending;
    let payer;

    if (body.signup) {
      const details = validateSignupDetails(body.signup);
      if (await isTzBlocked(details.username, event)) {
        throw new Error('לא ניתן לפתוח חשבון עם תעודת הזהות הזאת. יש לפנות לגבאים.');
      }
      const config = await getConfig(event);
      pending = await createPendingSignup(event, details, config.signupAmount, 'online');
      payer = { fullName: details.displayName, tz: details.username, phone: details.phone };
    } else {
      const { userId, amount } = body;
      if (!userId || !amount) throw new Error('חסרים פרטי הטענה (userId/amount)');
      const user = await getUserById(event, userId);
      if (!user) throw new Error('משתמש לא נמצא');
      pending = await createPendingTopup(event, { userId, amount, channel: 'online' });
      payer = { fullName: user.displayName, tz: user.username, phone: user.phone };
    }

    const url = buildOnlinePaymentUrl({
      amount: pending.amount,
      token: pending.token,
      ...payer,
      redirectUrl: `${siteUrl(event)}/kiosk-payment-done.html`
    });

    return { statusCode: 200, headers, body: JSON.stringify({ success: true, token: pending.token, url, amount: pending.amount }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
