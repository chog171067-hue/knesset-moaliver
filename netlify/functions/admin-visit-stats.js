// דף ניהול - טאב "כניסות לאתר": מחזיר סיכום צפיות בדפים (שנרשמות ב-track-visit.js)
// לפי יום ולפי חודש, בהתבסס על שעון ישראל.
const { getAdminStore, getIsraelDateString } = require('./lib/blobs-store');
const { requireAdmin } = require('./lib/admin-auth');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    const store = getAdminStore(event);
    const visits = (await store.get('site-visits.json', { type: 'json' })) || {};

    const days = Object.keys(visits).sort().reverse();
    const daily = days.map(d => ({ date: d, count: visits[d] }));

    const monthlyMap = {};
    days.forEach(d => {
      const month = d.slice(0, 7);
      monthlyMap[month] = (monthlyMap[month] || 0) + visits[d];
    });
    const monthly = Object.keys(monthlyMap).sort().reverse().map(m => ({ month: m, count: monthlyMap[m] }));

    const todayStr = getIsraelDateString(new Date());
    const thisMonthStr = todayStr.slice(0, 7);

    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({
        success: true,
        todayCount: visits[todayStr] || 0,
        thisMonthCount: monthlyMap[thisMonthStr] || 0,
        daily,
        monthly
      })
    };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
