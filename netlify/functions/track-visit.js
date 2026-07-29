// נקרא (best-effort, בלי לחכות לתשובה) מכל טעינת עמוד באתר דרך assets/header.js,
// וסופר צפיות בדפים לפי יום (שעון ישראל). לא דורש התחברות - זה מונה כניסות
// כללי לאתר, לא רק לאזור האישי. כשל כאן אף פעם לא אמור להשפיע על חוויית המשתמש.
//
// כל יום נשמר כמערך של מזהי-מכשיר אנונימיים (visitorId - ראו assets/header.js),
// אחד לכל צפייה - כדי שנוכל להסיק גם "כמה צפיות בסה"כ" (אורך המערך) וגם "כמה
// מכשירים שונים בפועל" (גודל הסט הייחודי), ראו admin-visit-stats.js.
const { getAdminStore, getIsraelDateString } = require('./lib/blobs-store');

const STORE_KEY = 'site-visits.json';
const MAX_DAYS_KEPT = 400; // קצת יותר משנה, כדי שתמיד יהיה גם חודש מקביל אשתקד
const NO_ID_MARKER = 'no-id'; // מכשיר שחסם/לא תמך ב-localStorage - נספר כצפייה, אך לא כמבקר ייחודי נוסף

exports.handler = async function (event) {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type',
    'Access-Control-Allow-Methods': 'POST, OPTIONS',
    'Content-Type': 'application/json'
  };

  if (event.httpMethod === 'OPTIONS') {
    return { statusCode: 200, headers, body: '' };
  }
  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  try {
    const store = getAdminStore(event);
    const today = getIsraelDateString(new Date());

    let visitorId = NO_ID_MARKER;
    try {
      const body = JSON.parse(event.body || '{}');
      if (body.visitorId) visitorId = String(body.visitorId).slice(0, 100);
    } catch (e) { /* גוף לא תקין - עדיין סופרים את הצפייה, בלי מזהה */ }

    const visits = (await store.get(STORE_KEY, { type: 'json' })) || {};

    // תאימות לאחור: ימים שנשמרו לפני התמיכה במזהה מבקר (כמספר בלבד) - הופכים
    // למערך כדי שאפשר יהיה להוסיף אליהם; סך הצפיות ההיסטורי נשמר, אך לא ניתן
    // לשחזר מהם מבקרים ייחודיים בדיעבד.
    let dayEntry = visits[today];
    if (typeof dayEntry === 'number') {
      dayEntry = new Array(dayEntry).fill(NO_ID_MARKER);
    }
    if (!Array.isArray(dayEntry)) dayEntry = [];

    dayEntry.push(visitorId);
    visits[today] = dayEntry;

    const days = Object.keys(visits).sort();
    if (days.length > MAX_DAYS_KEPT) {
      days.slice(0, days.length - MAX_DAYS_KEPT).forEach(d => delete visits[d]);
    }

    await store.setJSON(STORE_KEY, visits);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
  } catch (error) {
    // לא קריטי - מעקב כניסות לא אמור לחסום או להאט את טעינת הדף במקרה של כשל
    return { statusCode: 200, headers, body: JSON.stringify({ success: false }) };
  }
};
