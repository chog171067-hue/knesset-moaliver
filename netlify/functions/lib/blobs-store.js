// גישה מרוכזת למאגר ה-Blobs של האתר - כל נתוני הניהול (בקשות מייל, רשימת
// זכאים משלימה, מונה כניסות) נשמרים תחת אותו store לוגי אחד, כדי שלא נצטרך
// לנהל כמה stores נפרדים בלי סיבה אמיתית.
const { getStore } = require('@netlify/blobs');

function getAdminStore() {
  return getStore('admin-data');
}

// מחזיר תאריך בפורמט YYYY-MM-DD לפי שעון ישראל (ולא UTC) - כדי שהספירה
// היומית תואמת ליום הישראלי בפועל, גם סביב חצות.
function getIsraelDateString(date) {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Jerusalem' }).format(date || new Date());
}

module.exports = { getAdminStore, getIsraelDateString };
