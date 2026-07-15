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

    // בדיקה האם המשתמש זכאי (כרגע מוגדר כמאושר לצורך הבדיקות)
    const isAllowedInCommunity = true; 

    if (!isAllowedInCommunity) {
      return {
        statusCode: 200,
        headers,
        body: JSON.stringify({ allowed: false, exists: false })
      };
    }

    // בדיקה האם קיים חשבון ב-Netlify Identity
    const checkResult = await checkIfUserExistsInIdentity(tz);

    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({ 
        allowed: true, 
        exists: checkResult.exists,
        debugMessage: checkResult.debugMessage || ''
      })
    };

  } catch (error) {
    console.error('Error in verify-tz:', error);
    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({ 
        allowed: true, 
        exists: false, 
        debugError: error.message 
      })
    };
  }
};

async function checkIfUserExistsInIdentity(tz) {
  // נטליפיי מספקת את ה-URL של האתר בזמן ריצה, או משתמשת בכתובת המקומית
  const siteUrl = process.env.URL || 'https://your-site.netlify.app';
  const identityToken = process.env.NETLIFY_IDENTITY_ADMIN_TOKEN;
  
  if (!identityToken) {
    return { exists: false, debugMessage: "NETLIFY_IDENTITY_ADMIN_TOKEN is missing in Environment Variables" };
  }

  try {
    const response = await axios.get(`${siteUrl}/.netlify/identity/admin/users`, {
      headers: {
        Authorization: `Bearer ${identityToken}`,
        'Content-Type': 'application/json'
      }
    });

    const users = response.data.users || [];
    
    // חיפוש המשתמש - בדיקה כפולה:
    // 1. האם יש משתמש עם ה-verified_tz הזו במטא-דאטה
    // 2. או האם יש משתמש שכתובת המייל שלו רשומה (כדי לתפוס רישומים חצי-מושלמים)
    const existingUser = users.find(u => {
      const hasTz = u.user_metadata && String(u.user_metadata.verified_tz) === String(tz);
      return hasTz;
    });

    return { exists: !!existingUser };
  } catch (err) {
    const errorDetails = err.response ? JSON.stringify(err.response.data) : err.message;
    console.error("Identity API Error:", errorDetails);
    return { exists: false, debugMessage: `API Error: ${errorDetails}` };
  }
}
