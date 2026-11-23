// עמדת הקיוסק - פתיחת חשבון בלי תשלום, כשיש לת"ז תשלום ממתין מנדרים פלוס של לפחות סכום
// פתיחת החשבון (signupAmount). החשבון נוצר, והתשלומים הממתינים נטענים ליתרה (כל אחד פעם אחת).
//
// גוף הבקשה: { tz, displayName, phone, password }
// תשובה: { success, loaded }
const { requireStation } = require('./lib/station-auth');
const { validateSignupDetails, assertTzAvailable, createPrepaidSignup } = require('./lib/kiosk-store');
const { isTzBlocked } = require('./lib/community');

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
    await assertTzAvailable(event, details.username);
    if (await isTzBlocked(details.username, event)) {
      throw new Error('לא ניתן לפתוח חשבון עם תעודת הזהות הזאת. יש לפנות לגבאים.');
    }
    const result = await createPrepaidSignup(event, details);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, loaded: result.loaded }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
