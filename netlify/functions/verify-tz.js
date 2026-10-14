const { getCommunityRecord, isTzBlocked, maskEmail } = require('./lib/community');
const { findUserByBoundTz } = require('./lib/identity-admin');

exports.handler = async (event, context) => {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type',
    'Access-Control-Allow-Methods': 'POST, OPTIONS'
  };

  if (event.httpMethod === 'OPTIONS') {
    return { statusCode: 200, headers, body: '' };
  }
  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  try {
    const { tz } = JSON.parse(event.body || '{}');
    if (!tz) {
      return {
        statusCode: 400,
        headers,
        body: JSON.stringify({ allowed: false, error: 'תעודת זהות חסרה' })
      };
    }

    // חסימה גוברת על הכול - גם על חשבון קיים וגם על פתיחת חשבון חדש
    if (await isTzBlocked(tz, event)) {
      return { statusCode: 200, headers, body: JSON.stringify({ allowed: false, blocked: true }) };
    }

    // בדיקה האם כבר יש חשבון המקושר לת"ז הזו (לפי app_metadata המוגן, לא user_metadata)
    let hasAccount = false;
    try {
      const identity = context.clientContext && context.clientContext.identity;
      const existingUser = await findUserByBoundTz(identity, tz);
      hasAccount = !!existingUser;
    } catch (adminErr) {
      console.error('שגיאה בבדיקת חשבון קיים מול Identity Admin API:', adminErr.message);
      // אם בדיקת ה-Admin API נכשלת מסיבה טכנית, עדיף להציג טופס הרשמה/כניסה מאוחד
      // בצד הלקוח (ראה personal.html) ולא לחסום את המשתמש לגמרי.
      hasAccount = null;
    }

    // חשבון קיים שאינו חסום - תמיד מותר להתחבר אליו, גם אם הת"ז לא מופיעה ברשימה
    // (למשל חשבונות שנפתחו לפי גיליון הגוגל הקודם)
    if (hasAccount === true) {
      return { statusCode: 200, headers, body: JSON.stringify({ allowed: true, exists: true }) };
    }

    const record = await getCommunityRecord(tz, event);
    if (!record) {
      return { statusCode: 200, headers, body: JSON.stringify({ allowed: false, exists: false }) };
    }

    // ההרשמה מחייבת את המייל שרשום ברשימה (ראו identity-signup.js) - בלי מייל אין איך להירשם
    if (!record.email) {
      return { statusCode: 200, headers, body: JSON.stringify({ allowed: false, noEmail: true }) };
    }

    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({ allowed: true, exists: hasAccount, emailHint: maskEmail(record.email) })
    };
  } catch (error) {
    console.error('Error in verify-tz:', error.message);
    return {
      statusCode: 500,
      headers,
      body: JSON.stringify({ allowed: false, error: 'שגיאת שרת, נסה שוב מאוחר יותר' })
    };
  }
};
