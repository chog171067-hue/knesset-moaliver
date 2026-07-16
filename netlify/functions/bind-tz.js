const { getCommunityRecord } = require('./lib/community');
const { findUserByBoundTz, setUserVerifiedTz } = require('./lib/identity-admin');

// פונקציה זו נקראת מהלקוח מיד אחרי כל התחברות/הרשמה מוצלחת (בעיקר עבור Google,
// כי שם לא ניתן להעביר ת"ז דרך identity-signup.js).
//
// חשוב: היא לא סומכת על שום דבר שהלקוח שולח מלבד ה-JWT עצמו (Authorization header).
// Netlify מפענח את הטוקן אוטומטית ומספק את זהות המשתמש דרך context.clientContext.user -
// כך שאין דרך להתחזות למשתמש אחר, בניגוד למצב הקודם שסמך על מה שנשלח בגוף הבקשה.

exports.handler = async (event, context) => {
  const headers = { 'Access-Control-Allow-Origin': '*' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const user = context.clientContext && context.clientContext.user;
  if (!user) {
    return { statusCode: 401, headers, body: JSON.stringify({ success: false, error: 'לא מחובר' }) };
  }

  const userId = user.sub || user.id;

  // אם כבר יש ת"ז מקושרת לחשבון - אין מה לעשות, ובוודאי שלא לאפשר לשנות אותה מכאן
  if (user.app_metadata && user.app_metadata.verified_tz) {
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, alreadyBound: true }) };
  }

  try {
    const { tz } = JSON.parse(event.body || '{}');
    if (!tz) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'תעודת זהות חסרה' }) };
    }

    const record = await getCommunityRecord(tz);
    if (!record) {
      return { statusCode: 403, headers, body: JSON.stringify({ success: false, error: 'תעודת הזהות אינה מופיעה ברשימת חברי הקהילה' }) };
    }

    const identity = context.clientContext && context.clientContext.identity;
    const existingOwner = await findUserByBoundTz(identity, tz);
    if (existingOwner && existingOwner.id !== userId) {
      return { statusCode: 409, headers, body: JSON.stringify({ success: false, error: 'תעודת הזהות הזו כבר משויכת לחשבון קיים אחר' }) };
    }

    await setUserVerifiedTz(identity, userId, record.tz, record.phone);

    return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
  } catch (err) {
    console.error('bind-tz error:', err.message);
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת' }) };
  }
};
