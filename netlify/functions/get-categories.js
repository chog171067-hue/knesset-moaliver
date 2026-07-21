// שולף את רשימת הקטגוריות (Groupe) שכבר בשימוש בפועל בכל אחד משני המוסדות,
// כדי להציג אותן כהשלמה אוטומטית בטופס "פתיחת הוראת קבע חדשה" - כך שלא יווצרו
// כפילויות של אותה קטגוריה בניסוחים שונים (למשל "בניין" מול "לבניין").

const { fetchCategoriesByMosad } = require('./lib/nedarim');

exports.handler = async function (event, context) {
  const corsHeaders = { 'Access-Control-Allow-Origin': '*' };

  if (event.httpMethod !== 'GET' && event.httpMethod !== 'POST') {
    return { statusCode: 405, headers: corsHeaders, body: 'Method Not Allowed' };
  }

  // דורש התחברות בלבד (לא מידע אישי - רק חלק מהאזור האישי המוגן)
  const authedUser = context.clientContext && context.clientContext.user;
  if (!authedUser) {
    return { statusCode: 401, headers: corsHeaders, body: JSON.stringify({ success: false, error: 'עליך להתחבר כדי לבצע פעולה זו' }) };
  }

  try {
    const categoriesByMosad = await fetchCategoriesByMosad();
    return {
      statusCode: 200,
      headers: { ...corsHeaders, 'Content-Type': 'application/json' },
      body: JSON.stringify({ success: true, categoriesByMosad })
    };
  } catch (error) {
    return {
      statusCode: 500,
      headers: corsHeaders,
      body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message })
    };
  }
};
