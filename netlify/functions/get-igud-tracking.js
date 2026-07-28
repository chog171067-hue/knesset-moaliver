// טאב אישי חריג ("מעקב תרומות האיגוד") שנחשף בממשק רק למשתמש מורשה ספציפי אחד
// (מזוהה לפי ת"ז מקושרת) - מציג לו את כל התורמים (ולא רק את עצמו) שנתרמו תחת
// הקטגוריה הנתונה, מתוך שני המוסדות. ראו assets/../personal.html לצד הלקוח.

const { fetchAllKevot, fetchAllDonationHistory } = require('./lib/nedarim');

const AUTHORIZED_TZ = '066436932';
const TRACKED_CATEGORY = "איגוד מוהליבר";

function matchesCategory(rawCategory) {
  return (rawCategory || '').trim() === TRACKED_CATEGORY;
}

exports.handler = async function (event, context) {
  const corsHeaders = { 'Access-Control-Allow-Origin': '*' };

  if (event.httpMethod !== 'GET' && event.httpMethod !== 'POST') {
    return { statusCode: 405, headers: corsHeaders, body: 'Method Not Allowed' };
  }

  const authedUser = context.clientContext && context.clientContext.user;
  const authedTz = authedUser && authedUser.app_metadata && String(authedUser.app_metadata.verified_tz || '').trim();

  if (!authedTz) {
    return {
      statusCode: 401,
      headers: corsHeaders,
      body: JSON.stringify({ success: false, error: 'עליך להתחבר עם חשבון מאומת כדי לצפות בנתונים' })
    };
  }

  if (authedTz !== AUTHORIZED_TZ) {
    return {
      statusCode: 403,
      headers: corsHeaders,
      body: JSON.stringify({ success: false, error: 'אין לך הרשאה לצפות בטאב זה' })
    };
  }

  try {
    const [allKevot, allHistory] = await Promise.all([fetchAllKevot(), fetchAllDonationHistory()]);

    const kevot = allKevot
      .filter(k => matchesCategory(k.Groupe))
      .map(k => ({
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
      }));

    const history = allHistory
      .filter(t => matchesCategory(t.Groupe))
      .map(t => ({
        date: t.TransactionTime,
        amount: t.Amount,
        type: t.TransactionType,
        category: t.Groupe,
        name: t.ClientName,
        mosadName: t.mosadName,
        isKeva: !!t.KevaId
      }));

    const totalAmount = history.reduce((sum, t) => sum + (parseFloat(t.amount) || 0), 0);

    return {
      statusCode: 200,
      headers: { ...corsHeaders, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        success: true,
        category: TRACKED_CATEGORY,
        kevot,
        history,
        totalAmount
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
