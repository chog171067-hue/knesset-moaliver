// שכבת גישה משותפת ל-API של נדרים פלוס (Manage3.aspx / Tamal3.aspx).
// כל פונקציה שצריכה לדעת "אילו הוראות קבע שייכות למשתמש המחובר" או להשתמש
// ב-ApiPassword של אחד משני המוסדות - חייבת לעבור דרך הקובץ הזה,
// כדי שיהיה מקור אמת אחד בלבד לזיהוי בעלות ולסודות ה-API.

const MOSAD_1 = '7012851';
const MOSAD_2 = '7005583'; // מוסד קרן הבנין 'גדול יהיה'
const MOSAD_1_NAME = 'בית הכנסת המרכזי מוהליבר';
const MOSAD_2_NAME = "קרן הבנין 'גדול יהיה'";

function getApiPasswords() {
  const API_PASSWORD_1 = process.env.NEDARIM_API_PASSWORD;
  const API_PASSWORD_2 = process.env.NEDARIM_API_PASSWORD_2;
  if (!API_PASSWORD_1 || !API_PASSWORD_2) {
    throw new Error('שגיאת שרת: מפתחות ה-API של המוסדות אינם מוגדרים במלואם');
  }
  return { API_PASSWORD_1, API_PASSWORD_2 };
}

function getApiPasswordForMosad(mosadId) {
  const { API_PASSWORD_1, API_PASSWORD_2 } = getApiPasswords();
  return String(mosadId) === MOSAD_1 ? API_PASSWORD_1 : API_PASSWORD_2;
}

function getMosadName(mosadId) {
  return String(mosadId) === MOSAD_1 ? MOSAD_1_NAME : MOSAD_2_NAME;
}

// שליפת הוראות הקבע של שני המוסדות יחד, ללא כל סינון לפי בעלות - שימוש פנימי בלבד
// (על הקוד הקורא לפונקציה הזו לאכוף בעצמו את בדיקת ההרשאה המתאימה, אם יש כזו)
async function fetchAllKevot() {
  const { API_PASSWORD_1, API_PASSWORD_2 } = getApiPasswords();

  const [res1, res2] = await Promise.all([
    fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_1}&ApiPassword=${API_PASSWORD_1}`),
    fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_2}&ApiPassword=${API_PASSWORD_2}`)
  ]);

  const allKevot1 = res1.ok ? await res1.json() : [];
  const allKevot2 = res2.ok ? await res2.json() : [];

  const markedKevot1 = allKevot1.map(k => ({ ...k, belongsToMosad: MOSAD_1_NAME, mosadId: MOSAD_1 }));
  const markedKevot2 = allKevot2.map(k => ({ ...k, belongsToMosad: MOSAD_2_NAME, mosadId: MOSAD_2 }));
  return [...markedKevot1, ...markedKevot2];
}

// שליפת כל הוראות הקבע ששייכות למשתמש המחובר (לפי הת"ז/טלפון שלו בלבד, לא לפי בקשה חיצונית)
async function fetchOwnKevot(ownTz, ownPhone) {
  const combinedKevot = await fetchAllKevot();

  return combinedKevot.filter(keva => {
    const kevaPhone = (keva.Phone || '').trim().replace(/[-\s]/g, '');
    const kevaTz = (keva.Zeout || '').trim();
    return kevaTz === ownTz && (!ownPhone || kevaPhone === ownPhone);
  });
}

// מוודא שה-kevaId המבוקש שייך בפועל למשתמש המחובר (ולא לגורם אחר), ומחזיר את הרשומה שלו
async function findOwnKeva(ownTz, ownPhone, kevaId) {
  const ownKevot = await fetchOwnKevot(ownTz, ownPhone);
  return ownKevot.find(k => String(k.KevaId) === String(kevaId)) || null;
}

// שליפת רשימת הקטגוריות (Groupe) שכבר בשימוש בפועל אצל כל מוסד - אין ל-API של נדרים
// פלוס נקודת קצה ייעודית לרשימת קטגוריות של תרומות (זה קיים רק במודול ההוצאות),
// ולכן פשוט אוספים את כל הערכים השונים מתוך רשימת כל הוראות הקבע של המוסד (GetKevaJson).
async function fetchCategoriesByMosad() {
  const { API_PASSWORD_1, API_PASSWORD_2 } = getApiPasswords();

  const [res1, res2] = await Promise.all([
    fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_1}&ApiPassword=${API_PASSWORD_1}`),
    fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_2}&ApiPassword=${API_PASSWORD_2}`)
  ]);

  const allKevot1 = res1.ok ? await res1.json() : [];
  const allKevot2 = res2.ok ? await res2.json() : [];

  function distinctCategories(list) {
    const set = new Set();
    list.forEach(k => {
      const g = (k.Groupe || '').trim();
      if (g) set.add(g);
    });
    return Array.from(set).sort((a, b) => a.localeCompare(b, 'he'));
  }

  return {
    [MOSAD_1]: distinctCategories(allKevot1),
    [MOSAD_2]: distinctCategories(allKevot2)
  };
}

// שליפת "הסטוריית עסקאות" (Action=GetHistoryJson) - שים לב: לפי התיעוד, פעולה זו מוגבלת
// ל-20 בקשות בשעה בלבד (למוסד), בניגוד לשאר הפעולות. לכן מיישמים כאן מטמון פשוט
// בזיכרון התהליך (best-effort - נשמר כל עוד ה-function "חמה", לא מובטח בין הפעלות קרות),
// כדי שריבוי לחיצות של כמה משתמשים בו-זמנית לא "ישרוף" את המכסה המוגבלת.
//
// MaxId מוגדר גבוה מאוד (100000) כדי לוודא שכל ההיסטוריה מוחזרת: הרשומות מוחזרות
// מה-API בסדר כרונולוגי עולה (הישנות קודם), ולכן ערך MaxId נמוך מדי חותך בפועל
// את התרומות החדשות ביותר (שהן הרשומות שבסוף הרשימה), ולא את הישנות.
const HISTORY_CACHE_TTL_MS = 10 * 60 * 1000; // 10 דקות
const historyCache = {};

async function fetchInstitutionHistory(mosadId) {
  const now = Date.now();
  const cached = historyCache[mosadId];
  if (cached && (now - cached.fetchedAt) < HISTORY_CACHE_TTL_MS) {
    return cached.data;
  }

  const apiPassword = getApiPasswordForMosad(mosadId);
  const response = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetHistoryJson&MosadId=${mosadId}&ApiPassword=${apiPassword}&MaxId=100000`);
  const data = response.ok ? await response.json() : [];
  const list = Array.isArray(data) ? data : [];

  historyCache[mosadId] = { data: list, fetchedAt: now };
  return list;
}

// ממיר תאריך בפורמט שמחזיר נדרים פלוס (DD/MM/YYYY, לעיתים עם שעה) לאובייקט Date תקין.
// אי אפשר לסמוך על new Date(str) ישירות עם הפורמט הזה - JS מפרש מחרוזות עם / כ-MM/DD/YYYY,
// כך שכל יום מעל 12 (למשל 28/07/2026) הופך ל-Invalid Date, מה שגרם למיון לפי תאריך לא לעבוד בפועל.
function parseNedarimDate(value) {
  const str = String(value || '').trim();
  const match = str.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})(?:\s+(\d{1,2}):(\d{2})(?::(\d{2}))?)?$/);
  if (match) {
    const [, day, month, year, hour, minute, second] = match;
    return new Date(Number(year), Number(month) - 1, Number(day), Number(hour || 0), Number(minute || 0), Number(second || 0));
  }
  const fallback = new Date(str);
  return isNaN(fallback) ? new Date(0) : fallback;
}

// שליפת היסטוריית העסקאות של שני המוסדות יחד, ללא כל סינון לפי בעלות - שימוש פנימי בלבד
// (על הקוד הקורא לפונקציה הזו לאכוף בעצמו את בדיקת ההרשאה המתאימה, אם יש כזו)
async function fetchAllDonationHistory() {
  const [hist1, hist2] = await Promise.all([
    fetchInstitutionHistory(MOSAD_1),
    fetchInstitutionHistory(MOSAD_2)
  ]);

  const marked1 = hist1.map(t => ({ ...t, mosadId: MOSAD_1, mosadName: MOSAD_1_NAME }));
  const marked2 = hist2.map(t => ({ ...t, mosadId: MOSAD_2, mosadName: MOSAD_2_NAME }));
  const combined = [...marked1, ...marked2];

  combined.sort((a, b) => parseNedarimDate(b.TransactionTime) - parseNedarimDate(a.TransactionTime));
  return combined;
}

// שליפת כל ההיסטוריה (כולל עסקאות חד-פעמיות שאינן קשורות לשום הוראת קבע) של המשתמש המחובר בלבד
async function fetchOwnDonationHistory(ownTz, ownPhone) {
  const combined = await fetchAllDonationHistory();

  const ownPhoneClean = (ownPhone || '').replace(/[-\s]/g, '');
  return combined.filter(t => {
    const tTz = (t.Zeout || '').trim();
    const tPhone = (t.Phone || '').trim().replace(/[-\s]/g, '');
    return tTz === ownTz && (!ownPhoneClean || tPhone === ownPhoneClean);
  });
}

module.exports = {
  MOSAD_1,
  MOSAD_2,
  MOSAD_1_NAME,
  MOSAD_2_NAME,
  getApiPasswords,
  getApiPasswordForMosad,
  getMosadName,
  fetchAllKevot,
  fetchOwnKevot,
  findOwnKeva,
  fetchCategoriesByMosad,
  fetchAllDonationHistory,
  fetchOwnDonationHistory
};
