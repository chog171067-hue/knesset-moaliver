// הרשאת מנהל - אותה תבנית שכבר קיימת בקוד עבור טאבים חריגים אחרים (ראו
// get-coffee-room-tracking.js, get-yeshiva-tracking.js וכו'): ת.ז מאומתת
// ומקובעת בקוד, נבדקת מול app_metadata.verified_tz של המשתמש המחובר בטוקן.
const ADMIN_TZ = '207139759';

function requireAdmin(context) {
  const authedUser = context.clientContext && context.clientContext.user;
  const authedTz = authedUser && authedUser.app_metadata && String(authedUser.app_metadata.verified_tz || '').trim();

  if (!authedTz) {
    return { authorized: false, statusCode: 401, error: 'עליך להתחבר עם חשבון מאומת כדי לגשת לדף הניהול' };
  }
  if (authedTz !== ADMIN_TZ) {
    return { authorized: false, statusCode: 403, error: 'אין לך הרשאת ניהול' };
  }
  return { authorized: true };
}

module.exports = { ADMIN_TZ, requireAdmin };
