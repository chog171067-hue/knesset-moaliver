// שכבת גישה יחידה לרשימת חברי הקהילה הזכאים.
// כל פונקציה אחרת (verify-tz, identity-signup, bind-tz) חייבת להשתמש בקובץ הזה
// ולא לממש בדיקה משלה - כך יש מקור אמת אחד בלבד.

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

async function getCommunityRecord(tz) {
  const list = await fetchCommunityList();
  const cleanTz = String(tz).trim();
  return list.find(r => r.tz === cleanTz) || null;
}

// מאפשר רענון כפוי מיידי (למשל אם עדכנת את הגיליון ורוצה לבדוק את זה מיד, בלי לחכות לדקה)
function clearCache() {
  cache = { data: null, fetchedAt: 0 };
}

module.exports = { fetchCommunityList, getCommunityRecord, clearCache };
