// שים לב לשם הקובץ - הוא חייב להיות בדיוק get-donor-data-by-tz.js כדי לתאום
// לכתובת שה-personal.html קורא לה (/.netlify/functions/get-donor-data-by-tz).

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
    const API_PASSWORD_1 = process.env.NEDARIM_API_PASSWORD;
    const API_PASSWORD_2 = process.env.NEDARIM_API_PASSWORD_2;

    if (!API_PASSWORD_1 || !API_PASSWORD_2) {
      return { statusCode: 500, headers: corsHeaders, body: JSON.stringify({ error: 'שגיאת שרת: מפתחות ה-API של המוסדות אינם מוגדרים במלואם' }) };
    }

    const MOSAD_1 = '7012851';
    const MOSAD_2 = '7005583'; // מוסד קרן הבנין 'גדול יהיה'
    const MOSAD_1_NAME = 'בית הכנסת המרכזי מוהליבר';
    const MOSAD_2_NAME = "קרן הבנין 'גדול יהיה'";

    // שליפת כל הוראות הקבע ששייכות למשתמש המחובר (לפי הת"ז/טלפון שלו בלבד, לא לפי בקשה חיצונית)
    async function fetchOwnKevot() {
      const [res1, res2] = await Promise.all([
        fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_1}&ApiPassword=${API_PASSWORD_1}`),
        fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_2}&ApiPassword=${API_PASSWORD_2}`)
      ]);

      const allKevot1 = res1.ok ? await res1.json() : [];
      const allKevot2 = res2.ok ? await res2.json() : [];

      const markedKevot1 = allKevot1.map(k => ({ ...k, belongsToMosad: MOSAD_1_NAME }));
      const markedKevot2 = allKevot2.map(k => ({ ...k, belongsToMosad: MOSAD_2_NAME }));
      const combinedKevot = [...markedKevot1, ...markedKevot2];

      return combinedKevot.filter(keva => {
        const kevaPhone = (keva.Phone || '').trim().replace(/[-\s]/g, '');
        const kevaTz = (keva.Zeout || '').trim();
        return kevaTz === ownTz && (!ownPhone || kevaPhone === ownPhone);
      });
    }

    // מצב ב': היסטוריית חיובים עבור הוראת קבע ספציפית
    if (body.kevaId) {
      // בדיקת בעלות: מוודאים שה-kevaId המבוקש שייך בפועל למשתמש המחובר, ולא לגורם אחר
      const ownKevot = await fetchOwnKevot();
      const owns = ownKevot.some(k => String(k.KevaId) === String(body.kevaId));
      if (!owns) {
        return { statusCode: 403, headers: corsHeaders, body: JSON.stringify({ success: false, error: 'אין לך הרשאה לצפות בהוראת קבע זו' }) };
      }

      let historyResponse = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaId&MosadId=${MOSAD_1}&ApiPassword=${API_PASSWORD_1}&KevaId=${body.kevaId}`);
      let kevaDetails = await historyResponse.json();

      if (kevaDetails.Result === 'Error' || !kevaDetails.KevaName) {
        historyResponse = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaId&MosadId=${MOSAD_2}&ApiPassword=${API_PASSWORD_2}&KevaId=${body.kevaId}`);
        kevaDetails = await historyResponse.json();
      }

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
    const donorKevot = await fetchOwnKevot();

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
