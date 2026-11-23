// דף ניהול - טאב הקיוסק לספרייה: "יתרות מהמערכת הישנה (סייפר)".
// GET - הרשימה וסטטוס המימוש. POST { rows } - העלאת הרשימה (מהאקסל שהופק ממסד הנתונים של
// סייפר). שורות שכבר מומשו נשארות ממומשות. המימוש עצמו - בעמדה (library-station-legacy-balance).
const { requireAdmin } = require('./lib/admin-auth');
const { listLegacyBalances, saveLegacyBalances } = require('./lib/kiosk-store');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    if (event.httpMethod === 'GET') {
      const entries = await listLegacyBalances(event);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, entries }) };
    }
    if (event.httpMethod === 'POST') {
      const { rows } = JSON.parse(event.body || '{}');
      const result = await saveLegacyBalances(event, rows);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, ...result }) };
    }
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
