// עמדת הקיוסק - כפתור "להצטרפות ל'חבר קהילה'" (דף נדרים פלוס להקמת הוראת קבע):
//
// 1. { tz, action: "snapshot" } - לפני פתיחת הדף: מחזיר את מזהי הוראות הקבע שכבר
//    קיימות לת"ז. רק הוראה *חדשה* תיחשב - הוראות ישנות של מי שהושמט מהקובץ לא
//    מחזירות לו חברות (הקובץ קובע לגביהן).
// 2. { tz, knownKevaIds } - אחרי הדף: בודק אם נוספה הוראת קבע "דמי חבר" פעילה
//    (קטגוריה, סכום חודשי מינימלי ומספר תשלומים מינימלי לפי הגדרות הקיוסק). אם כן - נרשמת חברות זמנית עד העלאת
//    קובץ התורמים הבא (ראו getKioskMembership ב-lib/community.js).
//
// תשובה: { success, kevaIds } / { success, isCommunityMember }
const { requireStation } = require('./lib/station-auth');
const { getConfig } = require('./lib/kiosk-store');
const { getKioskMembership, kevaIdsForTz } = require('./lib/community');
const { fetchAllKevot } = require('./lib/nedarim');

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
    const { tz, action, knownKevaIds } = JSON.parse(event.body || '{}');
    const cleanTz = String(tz || '').replace(/\D/g, '');
    if (!/^\d{5,9}$/.test(cleanTz)) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'תעודת זהות לא תקינה' }) };
    }

    if (action === 'snapshot') {
      const kevaIds = kevaIdsForTz(await fetchAllKevot(), cleanTz);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, kevaIds }) };
    }

    const config = await getConfig(event);
    const membership = await getKioskMembership(event, cleanTz, {
      fetchKevot: fetchAllKevot,
      groupe: config.memberKevaGroupe,
      minAmount: config.memberKevaMinAmount,
      minPayments: config.memberKevaMinPayments,
      excludeKevaIds: Array.isArray(knownKevaIds) ? knownKevaIds : []
    });
    if (membership.joinedNow) console.log(`[member-check] חברות זמנית נרשמה אחרי הקמת הוראת קבע (ת"ז מסתיימת ב-${cleanTz.slice(-3)})`);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, isCommunityMember: membership.member }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
