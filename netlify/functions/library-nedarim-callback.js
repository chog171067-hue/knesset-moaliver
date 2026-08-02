// CallBack שרתי-לשרתי מנדרים פלוס בסיום עסקת הטענת כסף בקיוסק - זהו מקור
// האמת היחיד לכך שהתשלום אכן בוצע (ראו lib/kiosk-store.js לזרימה המלאה).
// אין כאן הרשאת station/admin רגילה - האימות היחיד האפשרי מול נדרים פלוס
// הוא כתובת ה-IP השולחת (ראו lib/nedarim-security.js).
const { isFromNedarim } = require('./lib/nedarim-security');
const { confirmPendingTopup } = require('./lib/kiosk-store');

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
      lastNum: data.LastNum
    });

    return { statusCode: 200, body: 'OK' };
  } catch (error) {
    // מחזירים 200 גם בכשל (ולא 500) כי נדרים פלוס לא ינסה לשדר שוב בכל מקרה
    // (ראו תיעוד: "המערכת לא תנסה לשדר יותר מפעם אחת") - כשל אמיתי כאן דורש
    // בדיקה ידנית של הלוגים, לא ניסיון חוזר אוטומטי מהצד שלהם.
    console.error('[library-nedarim-callback] שגיאה בעיבוד הקאלבק:', error.message);
    return { statusCode: 200, body: 'error logged' };
  }
};
