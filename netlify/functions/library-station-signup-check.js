// עמדת הקיוסק - "פתיחת חשבון", כפתור "המשך": בודק את הפרטים שהוקלדו (ת"ז כולל
// ספרת ביקורת, שם מלא, טלפון, סיסמה של 4-8 ספרות), שאין כבר חשבון עם אותה ת"ז,
// ושהת"ז לא חסומה. מחזיר האם זה חבר קהילה (לתגית "חבר קהילה" בחלון התשלום) ואת
// סכום ההטענה הראשונה. לא יוצר כלום - החשבון נוצר רק אחרי התשלום.
// חבר קהילה = בקובץ התורמים / ברשימה הידנית, או מי שהקים הוראת קבע "דמי חבר"
// דרך הקיוסק אחרי העלאת הקובץ האחרון (ראו getKioskMembership ב-lib/community.js).
//
// גוף הבקשה: { tz, displayName, phone, password }
// תשובה: { success, isCommunityMember, signupAmount, communityJoinUrl }
const { requireStation } = require('./lib/station-auth');
const { validateSignupDetails, assertTzAvailable, getConfig } = require('./lib/kiosk-store');
const { getKioskMembership, isTzBlocked } = require('./lib/community');

exports.handler = async function (event) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireStation(event);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  let details;
  try {
    details = validateSignupDetails(JSON.parse(event.body || '{}'));
    await assertTzAvailable(event, details.username);
    if (await isTzBlocked(details.username, event)) {
      throw new Error('לא ניתן לפתוח חשבון עם תעודת הזהות הזאת. יש לפנות לצוות הספרייה.');
    }
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }

  try {
    const config = await getConfig(event);
    let isCommunityMember = false;
    try {
      const membership = await getKioskMembership(event, details.username);
      isCommunityMember = membership.member;
    } catch (e) {
      // תקלה בבדיקת החברות לא חוסמת פתיחת חשבון - רק לא מציגה תגית
      console.error('[signup-check] כשל בבדיקת חברות קהילה:', e.message);
    }
    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({
        success: true,
        isCommunityMember,
        signupAmount: config.signupAmount,
        communityJoinUrl: config.communityJoinUrl || ''
      })
    };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
