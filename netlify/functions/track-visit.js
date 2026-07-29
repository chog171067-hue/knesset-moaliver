// נקרא (best-effort, בלי לחכות לתשובה) מכל טעינת עמוד באתר דרך assets/header.js,
// וסופר צפיות בדפים לפי יום (שעון ישראל). לא דורש התחברות - זה מונה כניסות
// כללי לאתר, לא רק לאזור האישי. כשל כאן אף פעם לא אמור להשפיע על חוויית המשתמש.
const { getAdminStore, getIsraelDateString } = require('./lib/blobs-store');

const STORE_KEY = 'site-visits.json';
const MAX_DAYS_KEPT = 400; // קצת יותר משנה, כדי שתמיד יהיה גם חודש מקביל אשתקד

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
    const store = getAdminStore();
    const today = getIsraelDateString(new Date());

    const visits = (await store.get(STORE_KEY, { type: 'json' })) || {};
    visits[today] = (visits[today] || 0) + 1;

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
