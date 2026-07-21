// שים לב לשם הקובץ - הוא חייב להיות בדיוק get-donor-data-by-tz.js כדי לתאום
// לכתובת שה-personal.html קורא לה (/.netlify/functions/get-donor-data-by-tz).

const { fetchOwnKevot, findOwnKeva, getApiPasswordForMosad } = require('./lib/nedarim');

exports.handler = async function (event, context) {
  const corsHeaders = { 'Access-Control-Allow-Origin': '*' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers: corsHeaders, body: 'Method Not Allowed' };
  }

  // === שכבת ההרשאה - זה מה שהיה חסר לגמרי בגרסה הקודמת ===
  // אנחנו לא מקבלים tz/phone מהלקוח יותר. אנחנו שולפים אותם מהטוקן המאומת של
  // המשתמש המחובר בלבד. כך אין דרך לבקש נתונים של מישהו אחר.
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
    const body = JSON.parse(event.body || '{}');

    // מצב ב': היסטוריית חיובים עבור הוראת קבע ספציפית
    if (body.kevaId) {
      // בדיקת בעלות: מוודאים שה-kevaId המבוקש שייך בפועל למשתמש המחובר, ולא לגורם אחר
      const myKeva = await findOwnKeva(ownTz, ownPhone, body.kevaId);
      if (!myKeva) {
        return { statusCode: 403, headers: corsHeaders, body: JSON.stringify({ success: false, error: 'אין לך הרשאה לצפות בהוראת קבע זו' }) };
      }

      const apiPassword = getApiPasswordForMosad(myKeva.mosadId);
      const historyResponse = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaId&MosadId=${myKeva.mosadId}&ApiPassword=${apiPassword}&KevaId=${body.kevaId}`);
      const kevaDetails = await historyResponse.json();

      return {
        statusCode: 200,
        headers: { ...corsHeaders, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          success: true,
          successCount: kevaDetails.KevaSuccess,
          totalAmount: kevaDetails.TotalHistoryAmount,
          history: (kevaDetails.HistoryData || []).map(h => ({
            status: h.ID,
            amount: h.Amount,
            date: h.Date,
            transactionId: h.TransactionId
          }))
        })
      };
    }

    // מצב א': שליפת רשימת הוראות הקבע של המשתמש המחובר
    const donorKevot = await fetchOwnKevot(ownTz, ownPhone);

    return {
      statusCode: 200,
      headers: { ...corsHeaders, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        success: true,
        count: donorKevot.length,
        data: donorKevot.map(k => ({
          kevaId: k.KevaId,
          mosadName: k.belongsToMosad,
          name: k.ClientName,
          amount: k.Amount,
          nextDate: k.NextDate,
          lastNum: k.LastNum,
          category: k.Groupe,
          comments: k.Comments,
          enabled: k.Enabled === '1',
          errorText: k.ErrorText
        }))
      })
    };
  } catch (error) {
    return {
      statusCode: 500,
      headers: corsHeaders,
      body: JSON.stringify({ error: 'שגיאה בעיבוד הנתונים: ' + error.message })
    };
  }
};
