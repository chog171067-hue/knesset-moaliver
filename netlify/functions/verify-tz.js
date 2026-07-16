const axios = require('axios');

exports.handler = async (event, context) => {
  // הגדרת כותרות חובה למניעת בעיות דפדפן (CORS)
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

    // --- שלב א': בדיקת זכאות ברשימת הקהילה (ה-Whitelist) ---
    // כרגע מוגדר כ-true קבוע כדי שתוכל לבדוק את עצמך. 
    // בהמשך, כאן נחבר את רשימת חברי הקהילה האמיתית שלך.
    const isAllowedInCommunity = true; 

    if (!isAllowedInCommunity) {
      return {
        statusCode: 200,
        headers,
        body: JSON.stringify({ allowed: false, exists: false })
      };
    }

    // --- שלב ב': בדיקה מול ה-API הרשמי האם לת"ז זו כבר יש חשבון קיים ---
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
      statusCode: 200,
      headers,
      body: JSON.stringify({ allowed: true, exists: false, error: error.message })
    };
  }
};

async function checkIfUserExistsInIdentity(tz) {
  const identityToken = process.env.NETLIFY_IDENTITY_ADMIN_TOKEN;
  const siteId = process.env.SITE_ID; // נטליפיי מזריקה את זה אוטומטית

  if (!identityToken || !siteId) {
    console.error("Missing system tokens. Token state:", !!identityToken, "SiteID state:", !!siteId);
    return false; 
  }

  try {
    // פנייה מאובטחת לשרת הניהול המרכזי של נטליפיי
    const response = await axios.get(`https://api.netlify.com/api/v1/sites/${siteId}/identity/users`, {
      headers: {
        Authorization: `Bearer ${identityToken}`,
        'Content-Type': 'application/json'
      }
    });

    const users = response.data.users || [];
    
    // סריקה מדויקת: האם לאחד המשתמשים יש את הת"ז הזו בשדה הנתונים המאומת שלו
    const existingUser = users.find(u => 
      u.user_metadata && 
      u.user_metadata.data && 
      String(u.user_metadata.data.verified_tz) === String(tz)
    );

    return !!existingUser; // מחזיר true אם נמצא, false אם לא
  } catch (err) {
    console.error("Netlify API Call failed:", err.message);
    return false;
  }
}
