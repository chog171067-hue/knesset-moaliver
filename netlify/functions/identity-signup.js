exports.handler = async (event, context) => {
  const { action, user } = JSON.parse(event.body);

  // אנחנו מטפלים ברישום (signup)
  if (action === 'signup') {
    const metadata = user.user_metadata || {};
    
    // ניסיון לחלץ את תעודת הזהות מכל מקום אפשרי שבו היא עלולה להסתתר במבנה הנתונים של Netlify
    const tz = metadata.verified_tz || 
               (metadata.data ? metadata.data.verified_tz : null) ||
               (user.data ? user.data.verified_tz : null) ||
               (user.user_metadata ? user.user_metadata.verified_tz : null);

    if (tz) {
      console.log(`[Netlify Signup] Found TZ ${tz} for user ${user.email}. Binding to metadata...`);
      
      return {
        statusCode: 200,
        body: JSON.stringify({
          // אנחנו מעדכנים גם את ה-app_metadata (שמור רק בשרת ומאובטח) 
          // וגם את ה-user_metadata כדי שהדפדפן יוכל לקרוא את זה מיד
          app_metadata: {
            roles: ["user"], // מגדיר תפקיד בסיסי כדי למנוע בעיות הרשאה
            verified_tz: String(tz)
          },
          user_metadata: {
            ...metadata,
            verified_tz: String(tz)
          }
        })
      };
    } else {
      console.warn(`[Netlify Signup] Signup event triggered for ${user.email} but NO verified_tz was found in metadata.`);
    }
  }

  return {
    statusCode: 200,
    body: JSON.stringify({})
  };
};
