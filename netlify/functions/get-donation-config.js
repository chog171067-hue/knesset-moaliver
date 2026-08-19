// קריאה ציבורית (בלי צורך בהתחברות) של פרופיל "ערב התרמה" - לוגואים, סטריפ
// וסף הסכום - לפי מזהה אירוע. זו בדיוק הקריאה היחידה ש-control.html/display.html
// (donation-app/) מבצעים כדי לטעון את ההגדרות שנעולות עבורם; הם שומרים את
// התוצאה מקומית (localStorage) כדי להמשיך לעבוד גם בלי רשת בפעמים הבאות.
const { getConfigById } = require('./lib/donation-app-store');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const id = (event.queryStringParameters || {}).id;
  if (!id) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר מזהה אירוע' }) };
  }

  try {
    const record = await getConfigById(event, id);
    if (!record) {
      return { statusCode: 404, headers, body: JSON.stringify({ success: false, error: 'לא נמצאה הגדרה עבור קישור זה' }) };
    }

    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({
        success: true,
        config: {
          id: record.id,
          name: record.name,
          logoLeft: record.logoLeft,
          logoRight: record.logoRight,
          strip: record.strip,
          threshold: record.threshold
        }
      })
    };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
