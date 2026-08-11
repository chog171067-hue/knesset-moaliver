// עמדת הקיוסק קוראת לכאן לפני פתיחת מסך התשלום, כדי לקבל token (לזיהוי
// מדויק של המשתמש כשה-CallBack יגיע - ראו lib/kiosk-store.js) ומזהה עסקה
// שהוקם מראש בצד השרת של נדרים פלוס (כך שהסכום נעול ולא ניתן לשינוי בצד
// הלקוח). התשובה מכילה רק את שניהם - העמדה מזינה את מזהה העסקה לאייפרם
// בהודעת FinishTransaction (ראו "אייפרם: הקמת עסקה בצד שרת" בתיעוד).
const { requireStation } = require('./lib/station-auth');
const { createPendingTopup, getUserById } = require('./lib/kiosk-store');
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
    const { userId, amount } = JSON.parse(event.body || '{}');
    if (!userId || !amount) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסרים פרטי הטענה (userId/amount)' }) };
    }

    // שולפים את המשתמש כדי לשלוח שם מלא + ת"ז (username) לנדרים פלוס - חלק
    // מהמוסדות דורשים "שם משלם" לפני שמאשרים עסקה, ראו lib/library-nedarim.js
    const user = await getUserById(event, userId);
    if (!user) {
      return { statusCode: 404, headers, body: JSON.stringify({ success: false, error: 'משתמש לא נמצא' }) };
    }

    const pending = await createPendingTopup(event, { userId, amount });
    const { transactionId } = await createServerTransaction({
      amount: pending.amount,
      param1: pending.token,
      callbackUrl: buildCallbackUrl(event),
      fullName: user.displayName,
      tz: user.username
    });

    return { statusCode: 200, headers, body: JSON.stringify({ success: true, token: pending.token, transactionId }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
