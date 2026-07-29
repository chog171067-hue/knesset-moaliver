// שכבת גישה יחידה לרשימת חברי הקהילה הזכאים.
// כל פונקציה אחרת (verify-tz, identity-signup, bind-tz) חייבת להשתמש בקובץ הזה
// ולא לממש בדיקה משלה - כך יש מקור אמת אחד בלבד.
//
// מקור הזכאות המרכזי הוא גיליון הגוגל (SHEET_CSV_URL, לקריאה בלבד). בנוסף,
// המנהל יכול להוסיף ת.ז ידנית דרך דף הניהול (admin.html) - אלו נשמרות ברשימה
// משלימה ב-Netlify Blobs (ראו admin-eligible-list.js) ונבדקות כאן גם כן.
const { getAdminStore } = require('./blobs-store');

let cache = { data: null, fetchedAt: 0 };
const CACHE_TTL_MS = 60 * 1000; // דקה - מונע הצפה של גוגל בבקשות בזמן עומס (הרשמות רבות ברצף)

function parseCsvLine(line) {
  return line.split(',').map(c => c.trim().replace(/^"|"$/g, ''));
}

function cleanPhone(phone) {
  return phone ? String(phone).trim().replace(/[-\s]/g, '') : '';
}

async function fetchCommunityList() {
  const now = Date.now();
  if (cache.data && (now - cache.fetchedAt) < CACHE_TTL_MS) {
    return cache.data;
  }

  const csvUrl = process.env.SHEET_CSV_URL;
  if (!csvUrl) {
    throw new Error('חסר משתנה סביבה SHEET_CSV_URL - יש להגדיר אותו בהגדרות האתר בנטליפיי');
  }

  const response = await fetch(csvUrl);
  if (!response.ok) {
    throw new Error(`כשל בשליפת גיליון הקהילה: ${response.status}`);
  }
  const csvText = await response.text();
  const lines = String(csvText).split('\n').map(l => l.trim()).filter(Boolean);

  // מניחים שהשורה הראשונה היא כותרות: תז,טלפון
  const rows = lines.slice(1).map(parseCsvLine);

  const list = rows
    .filter(r => r[0])
    .map(r => ({
      tz: String(r[0]).trim(),
      phone: cleanPhone(r[1])
    }));

  cache = { data: list, fetchedAt: now };
  return list;
}

// בודק ברשימה המשלימה שהמנהל מנהל ידנית (בנפרד מהגיליון הראשי). כשל בקריאה
// מה-Blobs לא אמור לחסום את הבדיקה מול הגיליון הראשי - לכן נבלע שקט ל-null.
// event הוא אירוע ה-Lambda הגולמי, נדרש כדי לחבר את הקשר ה-Blobs (ראו blobs-store.js).
async function getExtraEligibleRecord(tz, event) {
  try {
    const store = getAdminStore(event);
    const list = (await store.get('extra-eligible-tz.json', { type: 'json' })) || [];
    const cleanTz = String(tz).trim();
    const found = list.find(r => r.tz === cleanTz);
    return found ? { tz: found.tz, phone: found.phone || '' } : null;
  } catch (e) {
    return null;
  }
}

async function getCommunityRecord(tz, event) {
  const list = await fetchCommunityList();
  const cleanTz = String(tz).trim();
  const fromSheet = list.find(r => r.tz === cleanTz);
  if (fromSheet) return fromSheet;
  return getExtraEligibleRecord(cleanTz, event);
}

// מאפשר רענון כפוי מיידי (למשל אם עדכנת את הגיליון ורוצה לבדוק את זה מיד, בלי לחכות לדקה)
function clearCache() {
  cache = { data: null, fetchedAt: 0 };
}

module.exports = { fetchCommunityList, getCommunityRecord, clearCache };
