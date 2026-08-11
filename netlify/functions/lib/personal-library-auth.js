// בדיקת הרשאה משותפת לכל נקודות הקצה של "חשבון הקיוסק שלי" באזור האישי.
//
// ארכיטקטורה: זיהוי אוטומטי לפי ת"ז - אם למשתמש האתר יש ת"ז מאומתת
// (app_metadata.verified_tz, שנקבעה כבר בהרשמה - ראו identity-signup.js),
// ויש חשבון קיוסק שה-username שלו שווה בדיוק לאותה ת"ז - זה נחשב "אותו אדם",
// בלי צורך בשום קישור ידני נפרד (בניגוד לגרסה הקודמת של קובץ זה).
//
// למה זה בטוח: verified_tz נשמר ב-app_metadata, שרק Functions בצד השרת יכולות
// לערוך (לא הלקוח) - כך שאי אפשר "להתחזות" לת"ז של מישהו אחר.
const { findUserByUsername } = require('./kiosk-store');

async function requirePersonalLibraryLink(event, context) {
  const user = context.clientContext && context.clientContext.user;
  if (!user) {
    return { authorized: false, statusCode: 401, error: 'עליך להתחבר כדי לגשת לחשבון הקיוסק' };
  }

  const verifiedTz = user.app_metadata && user.app_metadata.verified_tz;
  if (!verifiedTz) {
    // מצב תיאורטי בלבד: לפי identity-signup.js אי אפשר להירשם בלי ת"ז מאומתת -
    // נשמר ליתר ביטחון, למקרה קצה (למשל חשבון ישן מלפני שינוי זה).
    return { authorized: false, statusCode: 403, error: 'לא נמצאה תעודת זהות מאומתת בחשבון זה' };
  }

  const kioskUser = await findUserByUsername(event, verifiedTz);
  if (!kioskUser) {
    return {
      authorized: false,
      statusCode: 404,
      error: 'לא נמצא חשבון קיוסק ספרייה עבור תעודת הזהות שלך. פנה לצוות הספרייה לפתיחת חשבון.',
      linked: false
    };
  }

  return {
    authorized: true,
    siteUserId: user.sub || user.id,
    kioskUserId: kioskUser.id,
    verifiedTz,
    // השם מהאתר (לא מהקיוסק - שם לא נשמר שם) - שימושי למילוי אוטומטי בטופסי תשלום
    fullName: (user.user_metadata && (user.user_metadata.full_name || user.user_metadata.name)) || ''
  };
}

module.exports = { requirePersonalLibraryLink };
