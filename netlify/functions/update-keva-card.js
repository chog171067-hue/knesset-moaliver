// עדכון אמצעי תשלום ו/או תאריך חיוב להוראת קבע קיימת (Action=UpdateKevaNew ב-Manage3.aspx).
//
// חשוב מאוד: זו פונקציית "מעבר" (proxy) בלבד. מספר הכרטיס, התוקף וה-CVV שהלקוח
// מקליד עוברים דרך הפונקציה הזו ישירות לנדרים פלוס ולא נשמרים כאן, לא בלוג
// ולא בשום מקום אחר - כי זו הדרך היחידה שנדרים פלוס מציעים לעדכון הו"ק קיימת
// (הם לא מציעים אייפרם ייעודי לפעולה הזו).
//
// לפי התיעוד, Amount ו-NextDate הם שדות חובה גם בבקשת העדכון - לכן קודם שולפים
// את הערכים הקיימים (או את אלו שהלקוח בחר לשנות) ושולחים אותם בחזרה, כדי לא
// לשנות אותם בטעות לערך ריק.

const { findOwnKeva, getApiPasswordForMosad } = require('./lib/nedarim');

function isValidExpiry(tokef) {
  // פורמט MM/YY או MMYY בלבד
  return /^\d{2}\/?\d{2}$/.test(String(tokef || '').trim());
}

exports.handler = async function (event, context) {
  const corsHeaders = { 'Access-Control-Allow-Origin': '*' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers: corsHeaders, body: 'Method Not Allowed' };
  }

  const authedUser = context.clientContext && context.clientContext.user;
  if (!authedUser || !authedUser.app_metadata || !authedUser.app_metadata.verified_tz) {
    return {
      statusCode: 401,
      headers: corsHeaders,
      body: JSON.stringify({ success: false, error: 'עליך להתחבר עם חשבון מאומת כדי לבצע פעולה זו' })
    };
  }

  const ownTz = String(authedUser.app_metadata.verified_tz).trim();
  const ownPhone = String(authedUser.app_metadata.verified_phone || '').trim();

  let body;
  try {
    body = JSON.parse(event.body || '{}');
  } catch (e) {
    return { statusCode: 400, headers: corsHeaders, body: JSON.stringify({ success: false, error: 'בקשה לא תקינה' }) };
  }

  const { kevaId, creditCard, tokef, cvv, nextDate } = body;

  if (!kevaId || !creditCard || !tokef) {
    return {
      statusCode: 400,
      headers: corsHeaders,
      body: JSON.stringify({ success: false, error: 'חסרים פרטים: יש למלא מספר כרטיס ותוקף' })
    };
  }

  if (!isValidExpiry(tokef)) {
    return {
      statusCode: 400,
      headers: corsHeaders,
      body: JSON.stringify({ success: false, error: 'תוקף הכרטיס אינו תקין - יש להזין בפורמט חודש/שנה, לדוגמה 12/27' })
    };
  }

  try {
    // בדיקת בעלות: מוודאים שהוראת הקבע שייכת בפועל למשתמש המחובר, ולא לגורם אחר
    const myKeva = await findOwnKeva(ownTz, ownPhone, kevaId);
    if (!myKeva) {
      return { statusCode: 403, headers: corsHeaders, body: JSON.stringify({ success: false, error: 'אין לך הרשאה לעדכן הוראת קבע זו' }) };
    }

    const apiPassword = getApiPasswordForMosad(myKeva.mosadId);

    // Amount תמיד נשאר כפי שהיה - שינוי סכום החיוב אינו חלק מהפעולה הזו.
    // NextDate: אם הלקוח בחר תאריך חיוב חדש נשלח אותו, אחרת נשמר הקיים כדי לא לאפס אותו.
    const params = new URLSearchParams({
      Action: 'UpdateKevaNew',
      MosadNumber: myKeva.mosadId,
      ApiPassword: apiPassword,
      KevaId: String(kevaId),
      Amount: String(myKeva.Amount),
      NextDate: nextDate ? String(nextDate) : String(myKeva.NextDate),
      CreditCard: String(creditCard).trim(),
      Tokef: String(tokef).trim()
    });
    if (cvv) params.set('CVV', String(cvv).trim());

    const response = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?${params.toString()}`);
    const result = await response.json();

    if (result.Result !== 'OK') {
      return {
        statusCode: 400,
        headers: corsHeaders,
        body: JSON.stringify({ success: false, error: result.Message || 'העדכון נכשל - ייתכן שפרטי הכרטיס שגויים' })
      };
    }

    return {
      statusCode: 200,
      headers: { ...corsHeaders, 'Content-Type': 'application/json' },
      body: JSON.stringify({ success: true })
    };
  } catch (error) {
    return {
      statusCode: 500,
      headers: corsHeaders,
      body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message })
    };
  }
};
