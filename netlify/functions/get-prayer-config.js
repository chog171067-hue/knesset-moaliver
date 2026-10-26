// דפי התפילה (yemothachol.html, shabbat.html) - הגדרות זמני התפילות האוטומטיים
// לתצוגה ציבורית, ללא התחברות. החישוב עצמו נעשה בדפדפן (assets/prayer-schedule.js).
const { readPrayerConfig } = require('./lib/prayer-config-store');

exports.handler = async function (event) {
  const headers = { 'Content-Type': 'application/json', 'Cache-Control': 'public, max-age=60' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  try {
    const config = await readPrayerConfig(event);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, config }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאה בטעינת הגדרות התפילות' }) };
  }
};
