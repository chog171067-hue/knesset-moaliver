// דף "מהנעשה ונשמע" (news.html) - רשימת האירועים לתצוגה ציבורית, ללא התחברות.
const { listPublicEvents } = require('./lib/news-store');

exports.handler = async function (event) {
  const headers = { 'Content-Type': 'application/json', 'Cache-Control': 'public, max-age=60' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  try {
    const events = await listPublicEvents(event);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, events }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאה בטעינת האירועים' }) };
  }
};
