// תעריף עמוד הדפסה ורשימת התוכנות המאושרות בעמדת הקיוסק. שתי צריכות שונות
// לגמרי לגיטימיות ל-GET: עמדת הקיוסק עצמה (מפתח עמדה, כדי לדעת מה להציג
// ומה לגבות) ומנהל בפאנל הניהול (כדי לערוך) - לכן GET מקבל את אחד משני
// סוגי ההרשאה. עדכון (POST) מוגבל למנהל בלבד.
const { requireStation } = require('./lib/station-auth');
const { requireAdmin } = require('./lib/admin-auth');
const { getConfig, updateConfig } = require('./lib/kiosk-store');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod === 'GET') {
    const stationAuth = requireStation(event);
    const adminAuth = requireAdmin(context);
    if (!stationAuth.authorized && !adminAuth.authorized) {
      return { statusCode: 401, headers, body: JSON.stringify({ success: false, error: 'נדרשת הרשאת עמדה או הרשאת מנהל' }) };
    }

    try {
      const config = await getConfig(event);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, config }) };
    } catch (error) {
      return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
    }
  }

  if (event.httpMethod === 'POST') {
    const auth = requireAdmin(context);
    if (!auth.authorized) {
      return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
    }

    try {
      const patch = JSON.parse(event.body || '{}');
      const config = await updateConfig(event, patch);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, config }) };
    } catch (error) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
    }
  }

  return { statusCode: 405, headers, body: 'Method Not Allowed' };
};
