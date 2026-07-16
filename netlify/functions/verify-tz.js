const { getCommunityRecord } = require('./lib/community');
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

    // בדיקת זכאות אמיתית מול הגיליון - זה החלק שהיה חסר לגמרי בגרסה הקודמת
    const record = await getCommunityRecord(tz);
    if (!record) {
      return {
        statusCode: 200,
        headers,
        body: JSON.stringify({ allowed: false, exists: false })
      };
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

    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({ allowed: true, exists: hasAccount })
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
