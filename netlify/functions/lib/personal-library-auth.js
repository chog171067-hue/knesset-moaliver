// בדיקת הרשאה משותפת לכל נקודות הקצה של "חשבון הקיוסק שלי" באזור האישי:
// מחייב משתמש מחובר (JWT), ושכבר קישר בעבר חשבון קיוסק לחשבון האתר שלו
// (ראו library-personal-link.js) - ה-library_kiosk_user_id נשמר ב-app_metadata,
// שדה שרק Functions בצד השרת יכולות לערוך, לא הלקוח.
function requirePersonalLibraryLink(context) {
  const user = context.clientContext && context.clientContext.user;
  if (!user) {
    return { authorized: false, statusCode: 401, error: 'עליך להתחבר כדי לגשת לחשבון הקיוסק' };
  }

  const kioskUserId = user.app_metadata && user.app_metadata.library_kiosk_user_id;
  if (!kioskUserId) {
    return { authorized: false, statusCode: 404, error: 'לא קושר חשבון קיוסק ספרייה לחשבון זה', linked: false };
  }

  return { authorized: true, siteUserId: user.sub || user.id, kioskUserId };
}

module.exports = { requirePersonalLibraryLink };
