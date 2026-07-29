// גישה מרוכזת למאגר ה-Blobs של האתר - כל נתוני הניהול (בקשות מייל, רשימת
// זכאים משלימה, מונה כניסות) נשמרים תחת אותו store לוגי אחד, כדי שלא נצטרך
// לנהל כמה stores נפרדים בלי סיבה אמיתית.
//
// כל הפונקציות בפרויקט הזה כתובות בסגנון "Lambda compatibility" הקלאסי
// (exports.handler = async (event, context) => {...}) - הסגנון הזה לא מקבל
// אוטומטית את הגדרות ה-Blobs מנטליפיי (זה קורה רק בפונקציות Edge/v2). לכן חובה
// לקרוא ל-connectLambda(event) עם אירוע הבקשה הגולמי, לפני כל שימוש ב-getStore.
const { getStore, connectLambda } = require('@netlify/blobs');

function getAdminStore(event) {
  connectLambda(event);
  return getStore('admin-data');
}

// מחזיר תאריך בפורמט YYYY-MM-DD לפי שעון ישראל (ולא UTC) - כדי שהספירה
// היומית תואמת ליום הישראלי בפועל, גם סביב חצות.
function getIsraelDateString(date) {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Jerusalem' }).format(date || new Date());
}

module.exports = { getAdminStore, getIsraelDateString };
