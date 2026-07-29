// דף ניהול - טאב "כניסות לאתר": מחזיר סיכום צפיות בדפים (שנרשמות ב-track-visit.js)
// לפי יום ולפי חודש, בהתבסס על שעון ישראל - כולל הערכת "מבקרים ייחודיים" לפי
// מזהה מכשיר אנונימי (לא מזהה אישית, ולא חסין מפני מכשיר/דפדפן חדש לאותו אדם).
const { getAdminStore, getIsraelDateString } = require('./lib/blobs-store');
const { requireAdmin } = require('./lib/admin-auth');

// מחזיר את סך הצפיות והמבקרים הייחודיים (המוערכים) עבור יום בודד. תומך גם
// בפורמט הישן (מספר בלבד, מלפני שנוסף מעקב מזהה מבקר) - אז unique = null.
function dayStats(entry) {
  if (typeof entry === 'number') return { views: entry, unique: null };
  if (!Array.isArray(entry)) return { views: 0, unique: 0 };
  return { views: entry.length, unique: new Set(entry).size };
}

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
    const daily = days.map(d => {
      const s = dayStats(visits[d]);
      return { date: d, views: s.views, unique: s.unique };
    });

    // לצבירה חודשית - הצפיות פשוט מסתכמות, אבל "ייחודיים" חייב להיות איחוד
    // (union) של הסטים לאורך החודש, אחרת מבקר שחוזר בכמה ימים באותו חודש
    // ייספר כמה פעמים בטעות.
    const monthlyViews = {};
    const monthlyIdSets = {};
    days.forEach(d => {
      const entry = visits[d];
      const month = d.slice(0, 7);
      monthlyViews[month] = (monthlyViews[month] || 0) + dayStats(entry).views;
      if (Array.isArray(entry)) {
        if (!monthlyIdSets[month]) monthlyIdSets[month] = new Set();
        entry.forEach(id => monthlyIdSets[month].add(id));
      }
    });
    const monthly = Object.keys(monthlyViews).sort().reverse().map(m => ({
      month: m,
      views: monthlyViews[m],
      unique: monthlyIdSets[m] ? monthlyIdSets[m].size : null
    }));

    const todayStr = getIsraelDateString(new Date());
    const todayStats = dayStats(visits[todayStr]);
    const thisMonthStr = todayStr.slice(0, 7);

    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({
        success: true,
        todayViews: todayStats.views,
        todayUnique: todayStats.unique,
        thisMonthViews: monthlyViews[thisMonthStr] || 0,
        thisMonthUnique: monthlyIdSets[thisMonthStr] ? monthlyIdSets[thisMonthStr].size : null,
        daily,
        monthly
      })
    };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
