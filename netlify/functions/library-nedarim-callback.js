// CallBack שרתי-לשרתי מנדרים פלוס בסיום עסקת הטענת כסף בקיוסק - זהו מקור
// האמת היחיד לכך שהתשלום אכן בוצע (ראו lib/kiosk-store.js לזרימה המלאה).
// אין כאן הרשאת station/admin רגילה - האימות היחיד האפשרי מול נדרים פלוס
// הוא כתובת ה-IP השולחת (ראו lib/nedarim-security.js).
const { isFromNedarim } = require('./lib/nedarim-security');
const { confirmPendingTopup, recordOrphanTopup } = require('./lib/kiosk-store');

exports.handler = async function (event) {
  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, body: 'Method Not Allowed' };
  }

  const { sourceIp, verified } = isFromNedarim(event);
  if (!verified) {
    console.warn(`[library-nedarim-callback] התקבלה קריאה מכתובת IP לא מוכרת: ${sourceIp}`);
    return { statusCode: 200, body: 'ignored' };
  }

  try {
    const data = JSON.parse(event.body || '{}');
    // רישום מלא של ההודעה (מופיע בלוגים של הפונקציה בנטליפיי) - לבירור מקרים חריגים
    console.log('[library-nedarim-callback] התקבל:', JSON.stringify(data));
    const token = data.Param1 || data.Param2;
    if (!token) {
      console.error('[library-nedarim-callback] קאלבק ללא Param1/Param2 - לא ניתן לשייך לחשבון');
      return { statusCode: 200, body: 'ignored: no token' };
    }

    await confirmPendingTopup(event, {
      token,
      nedarimAmount: data.Amount,
      nedarimTransactionId: data.TransactionId,
      confirmation: data.Confirmation,
      lastNum: data.LastNum,
      raw: data
    });

    return { statusCode: 200, body: 'OK' };
  } catch (error) {
    // מחזירים 200 גם בכשל (ולא 500) כי נדרים פלוס לא ינסה לשדר שוב בכל מקרה
    // (ראו תיעוד: "המערכת לא תנסה לשדר יותר מפעם אחת") - כשל אמיתי כאן דורש
    // בדיקה ידנית של הלוגים, לא ניסיון חוזר אוטומטי מהצד שלהם.
    console.error('[library-nedarim-callback] שגיאה בעיבוד הקאלבק:', error.message);
    // תשלום אמיתי שלא זוכה - שומרים את כל הפרטים לטיפול ידני, כדי שלא "ייעלם"
    try {
      const data = JSON.parse(event.body || '{}');
      await recordOrphanTopup(event, {
        reason: error.message,
        token: data.Param1 || data.Param2 || null,
        amount: data.Amount || null,
        nedarimTransactionId: data.TransactionId || null,
        confirmation: data.Confirmation || null,
        clientName: data.ClientName || null,
        zeout: data.Zeout || null,
        phone: data.Phone || null,
        raw: data
      });
    } catch (recordError) {
      console.error('[library-nedarim-callback] גם שמירת התשלום לטיפול ידני נכשלה:', recordError.message);
    }
    return { statusCode: 200, body: 'error logged' };
  }
};
