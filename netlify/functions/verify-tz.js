const axios = require('axios');

exports.handler = async (event, context) => {
  // הגדרת כותרות CORS בסיסיות
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

    // 1. שלב ראשון: בדיקה האם המשתמש נמצא ברשימת הנישומים המורשים (ה-Whitelist) באקסל/גוגל שיטס שלך
    // (כאן מתבצעת הפנייה הרגילה שלך לשרת הנתונים או לקובץ ה-JSON שמכיל את רשימת חברי הקהילה הזכאים)
    const isAllowedInCommunity = await checkCommunityWhitelist(tz); 

    if (!isAllowedInCommunity) {
      return {
        statusCode: 200,
        headers,
        body: JSON.stringify({ allowed: false, exists: false })
      };
    }

    // 2. שלב שני: בדיקה מול ה-API של Netlify Identity האם כבר קיים משתמש רשום עם ה-verified_tz הזו
    const hasAccount = await checkIfUserExistsInIdentity(tz);

    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({ 
        allowed: true, 
        exists: hasAccount // יחזיר true אם קיים חשבון, או false אם זו פעם ראשונה שלו
      })
    };

  } catch (error) {
    console.error('Error in verify-tz:', error);
    return {
      statusCode: 500,
      headers,
      body: JSON.stringify({ allowed: false, error: 'שגיאה פנימית במערכת האימות' })
    };
  }
};

// פונקציית עזר לבדיקת קיום החשבון בשרת של Netlify Identity
async function checkIfUserExistsInIdentity(tz) {
  const siteUrl = process.env.URL || 'https://your-site.netlify.app'; // נטליפיי מעדכנת זאת אוטומטית
  const identityToken = process.env.NETLIFY_IDENTITY_ADMIN_TOKEN; // מפתח מנהל שמוגדר בהגדרות נטליפיי שלך
  
  if (!identityToken) {
    console.warn("מפתח מנהל Identity אינו מוגדר. לא ניתן לבדוק קיום חשבונות קודמים.");
    return false; 
  }

  try {
    // שליפת רשימת המשתמשים הרשומים מהאתר
    const response = await axios.get(`${siteUrl}/.netlify/identity/admin/users`, {
      headers: {
        Authorization: `Bearer ${identityToken}`
      }
    });

    const users = response.data.users || [];
    // חיפוש האם יש משתמש ששדה ה-verified_tz במטא-דאטה שלו תואם לת"ז שהוזנה
    const existingUser = users.find(u => 
      u.user_metadata && u.user_metadata.verified_tz === tz
    );

    return !!existingUser; // מחזיר true אם נמצא משתמש תואם
  } catch (err) {
    console.error("שגיאה בפנייה ל-Netlify Identity Users API:", err.message);
    return false;
  }
}

// פונקציית סימולציה לבדיקת הרשימה שלך (החלף אותה בלוגיקה הקיימת אצלך אם יש כזו)
async function checkCommunityWhitelist(tz) {
  // כאן צריכה להיות פנייה לקובץ ה-JSON או למקור המידע שלך שמאשר את רשימת חברי הקהילה.
  // כרגע הפונקציה מחזירה true לצורך בדיקה במידה והת"ז תקינה.
  return true; 
}
