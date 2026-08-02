// עמדת הקיוסק קוראת לכאן *לפני* שהיא משחררת עבודת הדפסה בפועל לתור ההדפסה
// (סעיף 3.6 באפיון: "המערכת תחסום את ההדפסה מראש"). אם היתרה לא מספיקה
// מוחזר 402 עם היתרה/העלות הנוכחיים, כדי שהעמדה תציג מיד כפתור "הטענת כסף"
// במקום לשלוח את העבודה לתור.
const { requireStation } = require('./lib/station-auth');
const { chargeForPrint } = require('./lib/kiosk-store');

exports.handler = async function (event) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireStation(event);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    const { userId, pages } = JSON.parse(event.body || '{}');
    if (!userId || !pages) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסרים פרטי חיוב (userId/pages)' }) };
    }

    const result = await chargeForPrint(event, { userId, pages });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, ...result }) };
  } catch (error) {
    if (error.insufficientFunds) {
      return {
        statusCode: 402,
        headers,
        body: JSON.stringify({ success: false, error: error.message, insufficientFunds: true, balance: error.balance, cost: error.cost })
      };
    }
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
