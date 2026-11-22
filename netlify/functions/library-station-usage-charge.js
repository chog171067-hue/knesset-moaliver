// עמדת הקיוסק - חיוב על זמן שימוש: נקרא בסוף כל 10 דקות של חיבור (או על הדקות שנותרו
// כשהיתרה נגמרת והחשבון מתנתק). חבר קהילה לא מחויב - נבדק כאן בשרת, לא רק בעמדה.
//
// גוף הבקשה: { userId, minutes }
// תשובה: { success, balance, cost, pricePerMinute }
const { requireStation } = require('./lib/station-auth');
const { chargeForUsage, getUserById } = require('./lib/kiosk-store');
const { getKioskMembership } = require('./lib/community');

exports.handler = async function (event) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireStation(event);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  let userId, minutes;
  try {
    ({ userId, minutes } = JSON.parse(event.body || '{}'));
  } catch (e) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'בקשה לא תקינה' }) };
  }
  if (!userId || !minutes) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסרים פרטי חיוב (userId/minutes)' }) };
  }

  try {
    const user = await getUserById(event, String(userId));
    if (!user) {
      return { statusCode: 404, headers, body: JSON.stringify({ success: false, error: 'משתמש לא נמצא' }) };
    }
    let isMember = false;
    try {
      isMember = (await getKioskMembership(event, user.username)).member;
    } catch (e) {
      // אם אי אפשר לבדוק חברות - לא מחייבים (עדיף לא לחייב חבר קהילה בטעות)
      console.error('[usage-charge] כשל בבדיקת חברות קהילה - לא מחייבים:', e.message);
      isMember = true;
    }
    const result = await chargeForUsage(event, { userId: user.id, minutes, isMember });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, ...result }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
