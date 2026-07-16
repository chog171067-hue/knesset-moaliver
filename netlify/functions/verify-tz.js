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

    // בדיקת זכאות (פתוח לכולם כרגע לצורך בדיקות)
    const isAllowedInCommunity = true; 

    if (!isAllowedInCommunity) {
      return {
        statusCode: 200,
        headers,
        body: JSON.stringify({ allowed: false, exists: false })
      };
    }

    // בדיקה מול ה-API של Netlify
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
  const siteId = process.env.SITE_ID; 

  if (!identityToken || !siteId) {
    console.error("Missing tokens. IdentityToken:", !!identityToken, "SiteID:", !!siteId);
    return false; 
  }

  try {
    const response = await axios.get(`https://api.netlify.com/api/v1/sites/${siteId}/identity/users`, {
      headers: {
        Authorization: `Bearer ${identityToken}`,
        'Content-Type': 'application/json'
      }
    });

    const users = response.data.users || [];
    
    // סריקה חכמה הבודקת את שני המיקומים האפשריים של תעודת הזהות במטא-דאטה
    const existingUser = users.find(u => {
      if (!u.user_metadata) return false;
      
      // בדיקה 1: האם שמור בצורה שטוחה ישירה (הסטנדרט של נטליפיי ב-API)
      const flatTz = u.user_metadata.verified_tz;
      
      // בדיקה 2: האם שמור תחת אובייקט data פנימי
      const nestedTz = u.user_metadata.data ? u.user_metadata.data.verified_tz : null;
      
      return String(flatTz) === String(tz) || String(nestedTz) === String(tz);
    });

    return !!existingUser; 
  } catch (err) {
    console.error("Netlify API Call failed:", err.message);
    return false;
  }
}
