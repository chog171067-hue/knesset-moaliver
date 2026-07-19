const { getCommunityRecord } = require('./lib/community');
const { findUserByBoundTz } = require('./lib/identity-admin');

// פונקציית הטריגר הזו רצה בשרת בכל הרשמה חדשה (Netlify Identity Event Function).
// היא חייבת להיות שכבת ההגנה האמיתית - לא לסמוך על מה שהלקוח טוען ששלח.
//
// הערה חשובה: הרשמה דרך Google (או ספק חיצוני אחר) לא נושאת איתה metadata מותאם אישית,
// ולכן לא נוכל לאמת ת"ז כאן בשבילה - היא תטופל בנפרד ע"י netlify/functions/bind-tz.js
// מיד אחרי שהמשתמש חוזר מגוגל (ראה את ה-README ואת personal.html).

exports.handler = async (event, context) => {
  const { action, user } = JSON.parse(event.body);

  if (action !== 'signup') {
    return { statusCode: 200, body: JSON.stringify({}) };
  }

  const provider = (user.app_metadata && user.app_metadata.provider) || 'email';

  // הרשמה חיצונית (גוגל וכו') - אין לנו כאן שום דרך לאמת ת"ז, ניתן לה לעבור בלי לקשר
  // ת"ז; החשבון יישאר "לא מורשה" עד לקריאה מוצלחת ל-bind-tz.
  if (provider !== 'email') {
    console.log(`[identity-signup] הרשמה חיצונית (${provider}) עבור ${user.email} - הקישור לת"ז ייעשה דרך bind-tz`);
    return { statusCode: 200, body: JSON.stringify({}) };
  }

  // הרשמה עם מייל+סיסמה - כאן חובה שתהיה ת"ז מצורפת, ואנחנו מאמתים אותה בעצמנו
  const rawMetadata = user.user_metadata || {};
  // נטליפיי לפעמים עוטפת את הנתונים המותאמים אישית (ת"ז, שם) בשכבת .data נוספת -
  // משטחים את זה כדי שהשדות יישמרו במקום הרגיל שגם הלקוח וגם ממשק הניהול מצפים לו.
  const metadata = rawMetadata.data ? { ...rawMetadata, ...rawMetadata.data } : rawMetadata;
  delete metadata.data;
  const claimedTz = metadata.verified_tz || null;

  if (!claimedTz) {
    console.warn(`[identity-signup] הרשמה נדחתה - אין ת"ז מצורפת עבור ${user.email}`);
    return {
      statusCode: 400,
      body: JSON.stringify({ error: 'לא נמצאה תעודת זהות מאומתת בבקשת ההרשמה' })
    };
  }

  try {
    const record = await getCommunityRecord(claimedTz);
    if (!record) {
      console.warn(`[identity-signup] הרשמה נדחתה - ת"ז ${claimedTz} אינה ברשימת הזכאים (${user.email})`);
      return {
        statusCode: 400,
        body: JSON.stringify({ error: 'תעודת הזהות אינה מופיעה ברשימת חברי הקהילה המאושרים' })
      };
    }

    const identity = context.clientContext && context.clientContext.identity;
    const existingOwner = await findUserByBoundTz(identity, claimedTz);
    if (existingOwner) {
      console.warn(`[identity-signup] הרשמה נדחתה - ת"ז ${claimedTz} כבר משויכת לחשבון קיים (${user.email})`);
      return {
        statusCode: 400,
        body: JSON.stringify({ error: 'כבר קיים חשבון המשויך לתעודת הזהות הזו. אנא היכנס דרך מסך ההתחברות.' })
      };
    }

    console.log(`[identity-signup] אושר ומקושר ת"ז ${record.tz} עבור ${user.email}`);
    return {
      statusCode: 200,
      body: JSON.stringify({
        app_metadata: {
          roles: ['user'],
          verified_tz: record.tz,
          verified_phone: record.phone || null
        },
        // חשוב: יש לכלול גם את ה-user_metadata המקורי (כולל השם שהוזן בהרשמה) -
        // אחרת נטליפיי מפרשת את ההיעדר שלו בתשובה כ"מחיקה" שלו.
        user_metadata: metadata
      })
    };
  } catch (err) {
    console.error('[identity-signup] שגיאת שרת באימות:', err.message);
    return {
      statusCode: 500,
      body: JSON.stringify({ error: 'שגיאת שרת באימות הזכאות, נסה שוב' })
    };
  }
};
