// הפקת קבלה מרוכזת עבור הוראת קבע אשראי (Action=CreateInvoice, Type=Keva, ב-Tamal3.aspx).
// סוג הקבלה קבוע ל-405 (קבלת תרומה) כי בית הכנסת רשום באיזיקאונט כעמותה.

const { findOwnKeva, getApiPasswordForMosad } = require('./lib/nedarim');

const RECEIPT_TYPE_DONATION = '405'; // עמותה - קבלת תרומה

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

  const { kevaId, year } = body;
  const currentYear = new Date().getFullYear();
  const targetYear = parseInt(year, 10) || currentYear;

  // הגנה בסיסית מפני ערכים הזויים בשדה השנה
  if (targetYear < 2000 || targetYear > currentYear) {
    return { statusCode: 400, headers: corsHeaders, body: JSON.stringify({ success: false, error: 'שנה לא תקינה' }) };
  }

  if (!kevaId) {
    return { statusCode: 400, headers: corsHeaders, body: JSON.stringify({ success: false, error: 'חסר מזהה הוראת קבע' }) };
  }

  try {
    // בדיקת בעלות: מוודאים שהוראת הקבע שייכת בפועל למשתמש המחובר, ולא לגורם אחר
    const myKeva = await findOwnKeva(ownTz, ownPhone, kevaId);
    if (!myKeva) {
      return { statusCode: 403, headers: corsHeaders, body: JSON.stringify({ success: false, error: 'אין לך הרשאה עבור הוראת קבע זו' }) };
    }

    const apiPassword = getApiPasswordForMosad(myKeva.mosadId);

    const params = new URLSearchParams({
      Action: 'CreateInvoice',
      MosadNumber: myKeva.mosadId,
      ApiPassword: apiPassword,
      ID: String(kevaId),
      Type: 'Keva',
      Tkufa: String(targetYear),
      TamalType: RECEIPT_TYPE_DONATION
    });

    const response = await fetch(`https://matara.pro/nedarimplus/Reports/Tamal3.aspx?${params.toString()}`);
    const result = await response.json();

    if (result.Result !== 'OK') {
      return {
        statusCode: 400,
        headers: corsHeaders,
        body: JSON.stringify({ success: false, error: result.Message || 'הפקת הקבלה נכשלה' })
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
