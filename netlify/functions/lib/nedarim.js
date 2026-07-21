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

// שליפת כל הוראות הקבע ששייכות למשתמש המחובר (לפי הת"ז/טלפון שלו בלבד, לא לפי בקשה חיצונית)
async function fetchOwnKevot(ownTz, ownPhone) {
  const { API_PASSWORD_1, API_PASSWORD_2 } = getApiPasswords();

  const [res1, res2] = await Promise.all([
    fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_1}&ApiPassword=${API_PASSWORD_1}`),
    fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_2}&ApiPassword=${API_PASSWORD_2}`)
  ]);

  const allKevot1 = res1.ok ? await res1.json() : [];
  const allKevot2 = res2.ok ? await res2.json() : [];

  const markedKevot1 = allKevot1.map(k => ({ ...k, belongsToMosad: MOSAD_1_NAME, mosadId: MOSAD_1 }));
  const markedKevot2 = allKevot2.map(k => ({ ...k, belongsToMosad: MOSAD_2_NAME, mosadId: MOSAD_2 }));
  const combinedKevot = [...markedKevot1, ...markedKevot2];

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

module.exports = {
  MOSAD_1,
  MOSAD_2,
  MOSAD_1_NAME,
  MOSAD_2_NAME,
  getApiPasswords,
  getApiPasswordForMosad,
  getMosadName,
  fetchOwnKevot,
  findOwnKeva,
  fetchCategoriesByMosad
};
