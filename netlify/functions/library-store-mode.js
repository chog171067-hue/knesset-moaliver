// בדיקת אבחון: מציג באיזה מצב עקביות עובד אחסון הקיוסק ("strong" = קריאה ישירה
// מהמקור, "eventual" = דרך מטמון). לא חושף שום נתון של משתמשים.
const { getStoreMode } = require('./lib/kiosk-store');

exports.handler = async function (event) {
  const headers = { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' };
  try {
    return { statusCode: 200, headers, body: JSON.stringify({ mode: getStoreMode(event) }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ error: error.message }) };
  }
};
