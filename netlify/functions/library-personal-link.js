// האזור האישי באתר: קישור חד-פעמי בין חשבון האתר (Netlify Identity) לחשבון
// עמדת הקיוסק בספרייה, ע"י הזנת שם המשתמש+הסיסמה של הקיוסק (אותה הוכחת
// בעלות בדיוק כמו בעמדה עצמה) - לא ע"י בחירה מרשימה, כדי שאי אפשר יהיה
// "לגנוב" חשבון קיוסק של מישהו אחר רק כי יודעים את שם המשתמש שלו.
// אותו דפוס הגנה כמו bind-tz.js: לא סומכים על שום דבר מלבד ה-JWT עצמו,
// וברגע שיש קישור קיים - לא מאפשרים לדרוס אותו בשקט.
const { findUserByUsername, verifyPassword, toPublicUser } = require('./lib/kiosk-store');
const { findUserByLibraryKioskId, setUserLibraryKioskId } = require('./lib/identity-admin');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const user = context.clientContext && context.clientContext.user;
  if (!user) {
    return { statusCode: 401, headers, body: JSON.stringify({ success: false, error: 'עליך להתחבר כדי לקשר חשבון' }) };
  }
  const siteUserId = user.sub || user.id;

  if (user.app_metadata && user.app_metadata.library_kiosk_user_id) {
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, alreadyLinked: true }) };
  }

  try {
    const { username, password } = JSON.parse(event.body || '{}');
    if (!username || !password) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חובה להזין שם משתמש וסיסמה של חשבון הקיוסק' }) };
    }

    const kioskUser = await findUserByUsername(event, username);
    if (!kioskUser || !verifyPassword(String(password), kioskUser.passwordSalt, kioskUser.passwordHash)) {
      return { statusCode: 401, headers, body: JSON.stringify({ success: false, error: 'שם משתמש או סיסמה שגויים' }) };
    }

    const identity = context.clientContext && context.clientContext.identity;
    const existingOwner = await findUserByLibraryKioskId(identity, kioskUser.id);
    if (existingOwner && existingOwner.id !== siteUserId) {
      return { statusCode: 409, headers, body: JSON.stringify({ success: false, error: 'חשבון הקיוסק הזה כבר מקושר לחשבון אתר אחר' }) };
    }

    await setUserLibraryKioskId(identity, siteUserId, kioskUser.id);

    return { statusCode: 200, headers, body: JSON.stringify({ success: true, user: toPublicUser(kioskUser) }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
