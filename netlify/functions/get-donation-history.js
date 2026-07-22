// שולף את כל היסטוריית התרומות של המשתמש המחובר משני המוסדות - כולל תרומות חד-פעמיות
// שאינן קשורות לשום הוראת קבע (בניגוד ל"היסטוריית חיובים" הקיימת שמוצגת רק בתוך
// כרטיס של הוראת קבע ספציפית).

const { fetchOwnDonationHistory } = require('./lib/nedarim');

exports.handler = async function (event, context) {
  const corsHeaders = { 'Access-Control-Allow-Origin': '*' };

  if (event.httpMethod !== 'GET' && event.httpMethod !== 'POST') {
    return { statusCode: 405, headers: corsHeaders, body: 'Method Not Allowed' };
  }

  const authedUser = context.clientContext && context.clientContext.user;
  if (!authedUser || !authedUser.app_metadata || !authedUser.app_metadata.verified_tz) {
    return {
      statusCode: 401,
      headers: corsHeaders,
      body: JSON.stringify({ success: false, error: 'עליך להתחבר עם חשבון מאומת כדי לצפות בנתונים' })
    };
  }

  const ownTz = String(authedUser.app_metadata.verified_tz).trim();
  const ownPhone = String(authedUser.app_metadata.verified_phone || '').trim();

  try {
    const history = await fetchOwnDonationHistory(ownTz, ownPhone);
    const totalAmount = history.reduce((sum, t) => sum + (parseFloat(t.Amount) || 0), 0);

    return {
      statusCode: 200,
      headers: { ...corsHeaders, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        success: true,
        totalAmount,
        count: history.length,
        history: history.map(t => ({
          date: t.TransactionTime,
          amount: t.Amount,
          type: t.TransactionType,
          category: t.Groupe,
          comments: t.Comments,
          lastNum: t.LastNum,
          receiptId: t.KabalaId,
          mosadName: t.mosadName,
          isKeva: !!t.KevaId
        }))
      })
    };
  } catch (error) {
    return {
      statusCode: 500,
      headers: corsHeaders,
      body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message })
    };
  }
};
