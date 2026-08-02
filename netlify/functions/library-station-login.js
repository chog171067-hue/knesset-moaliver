// עמדת הקיוסק (אפליקציית Windows) - מסך הנעילה שולח לכאן שם משתמש+סיסמה כדי
// להתחבר. אין כאן מושג "session token" נפרד: מרגע ההתחברות העמדה עצמה מחזיקה
// את ה-userId בזיכרון (עד לניתוק/כיבוי), ושולחת אותו יחד עם מפתח העמדה
// (x-station-key) בכל בקשה הבאה - מפתח העמדה הוא גבול ההרשאה מול האינטרנט,
// לא טוקן session נפרד, כי ממילא רק העמדה הפיזית מכירה אותו.
const { requireStation } = require('./lib/station-auth');
const { findUserByUsername, verifyPassword, toPublicUser } = require('./lib/kiosk-store');

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
    const { username, password } = JSON.parse(event.body || '{}');
    if (!username || !password) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חובה להזין שם משתמש וסיסמה' }) };
    }

    // הודעת שגיאה זהה לשם משתמש לא קיים ולסיסמה שגויה בכוונה - כדי לא לחשוף
    // לתוקף פוטנציאלי אילו שמות משתמש קיימים בכלל במערכת.
    const genericError = { statusCode: 401, headers, body: JSON.stringify({ success: false, error: 'שם משתמש או סיסמה שגויים' }) };

    const user = await findUserByUsername(event, username);
    if (!user || !verifyPassword(String(password), user.passwordSalt, user.passwordHash)) {
      return genericError;
    }

    if (!user.isActive) {
      return { statusCode: 403, headers, body: JSON.stringify({ success: false, error: 'החשבון מושבת - יש לפנות לצוות הספרייה' }) };
    }

    return { statusCode: 200, headers, body: JSON.stringify({ success: true, user: toPublicUser(user) }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
