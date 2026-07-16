exports.handler = async (event, context) => {
  const { action, user } = JSON.parse(event.body);

  // אנחנו רוצים להתערב רק בזמן יצירת משתמש חדש
  if (action === 'signup') {
    const metadata = user.user_metadata || {};
    
    // אם תעודת הזהות הגיעה מההרשמה, אנחנו נועלים אותה בבסיס הנתונים של נטליפיי באופן רשמי
    const tz = metadata.verified_tz || (metadata.data ? metadata.data.verified_tz : null);

    if (tz) {
      console.log(`Successfully binding TZ ${tz} to user ${user.email} on server-side signup.`);
      return {
        statusCode: 200,
        body: JSON.stringify({
          user_metadata: {
            ...metadata,
            verified_tz: String(tz) // נעילת הת"ז בפרופיל לצמיתות
          }
        })
      };
    }
  }

  return {
    statusCode: 200,
    body: JSON.stringify({})
  };
};
