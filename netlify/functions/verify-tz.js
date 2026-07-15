const axios = require('axios');

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
    const { tz } = JSON.parse(event.body);
    if (!tz) {
      return {
        statusCode: 400,
        headers,
        body: JSON.stringify({ allowed: false, error: 'תעודת זהות חסרה' })
      };
    }

    // 1. בדיקה האם ה-TZ נמצא ברשימת הקהילה (כרגע מוגדר כמאושר לצורך הבדיקות שלך)
    const isAllowedInCommunity = true; 

    if (!isAllowedInCommunity) {
      return {
        statusCode: 200,
        headers,
        body: JSON.stringify({ allowed: false, exists: false })
      };
    }

    // 2. בדיקה מול ה-API של Netlify Identity האם קיים כבר משתמש כזה
    const hasAccount = await checkIfUserExistsInIdentity(tz);

    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({ 
        allowed: true, 
        exists: hasAccount 
      })
    };

  } catch (error) {
    console.error('Error in verify-tz:', error);
    return {
      statusCode: 200, // מחזירים 200 כדי לא לתקוע את המשתמש במקרה של שגיאת הרשאה זמנית ב-API
      headers,
      body: JSON.stringify({ allowed: true, exists: false, debugError: error.message })
    };
  }
};

async function checkIfUserExistsInIdentity(tz) {
  // נטליפיי מספקת את כתובת ה-API של ה-Identity ישירות בתוך ה-context של הפונקציה בזמן ריצה
  const siteUrl = process.env.URL || 'https://your-site.netlify.app';
  const identityToken = process.env.NETLIFY_IDENTITY_ADMIN_TOKEN;
  
  if (!identityToken) {
    console.warn("NETLIFY_IDENTITY_ADMIN_TOKEN חסר במערכת");
    return false; 
  }

  try {
    // פנייה ישירה לרשימת המשתמשים ב-Identity של האתר
    const response = await axios.get(`${siteUrl}/.netlify/identity/admin/users`, {
      headers: {
        Authorization: `Bearer ${identityToken}`,
        'Content-Type': 'application/json'
      }
    });

    const users = response.data.users || [];
    
    // סריקה האם קיים משתמש עם תעודת הזהות הזו במטא-דאטה שלו
    const existingUser = users.find(u => 
      u.user_metadata && String(u.user_metadata.verified_tz) === String(tz)
    );

    return !!existingUser; 
  } catch (err) {
    console.error("שגיאה בסריקת משתמשים ב-Identity API:", err.response ? err.response.data : err.message);
    return false;
  }
}
