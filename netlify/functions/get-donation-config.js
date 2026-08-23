// קריאה ציבורית (בלי צורך בהתחברות) של פרופיל "ערב התרמה" - שם לזיהוי בלבד -
// לפי מזהה אירוע. זו הקריאה ש-control.html/display.html (donation-app/) מבצעים
// כדי לטעון את ההגדרות שנעולות עבורם; הם שומרים את התוצאה מקומית (localStorage)
// כדי להמשיך לעבוד גם בלי רשת בפעמים הבאות.
//
// בכוונה לא מחזירים כאן לוגואים/סטריפ/רקע: תמונות שמגיעות דרך קריאת רשת כזו
// מטושטשות אצל חלק מהמשתמשים ע"י תוכנות סינון תוכן (כמו נטפרי), גם כשהן
// מוטבעות כ-base64 בתוך JSON. לכן הן נבחרות אצל display.html מקומית מהמחשב של
// המשתמש (ראו שם) ולא עוברות ברשת בכלל. גם סף סכום לא מוחזר כאן - חלוקת
// התרומות לאיזורים במסך התצוגה נעשית לפי סכומים קבועים, זהים לכל האירועים.
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
          name: record.name
        }
      })
    };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
