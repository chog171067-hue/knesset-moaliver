// שכבת גישה יחידה לרשימת חברי הקהילה הזכאים.
// כל פונקציה אחרת (verify-tz, identity-signup, bind-tz) חייבת להשתמש בקובץ הזה
// ולא לממש בדיקה משלה - כך יש מקור אמת אחד בלבד.
//
// מקור הזכאות המרכזי הוא רשימת התורמים (כרטיס "תורמים") של מוסד בית הכנסת בנדרים
// פלוס - כל מי שמופיע בה זכאי (הגבאים מנהלים שם רק את חברי הקהילה, עם תגית "חבר
// קהילה"). הרשימה נמשכת בפעולת GetTormimCsv של ה-API (ייצוא CSV של רשימת התורמים).
// בנוסף, המנהל יכול להוסיף ת.ז ידנית דרך דף הניהול (admin.html) - אלו נשמרות ברשימה
// משלימה ב-Netlify Blobs (ראו admin-eligible-list.js) ונבדקות כאן גם כן.
//
// עמידות לתקלות: עותק אחרון תקין של הרשימה נשמר גם ב-Blobs, כך שאם נדרים פלוס לא
// זמין (או שהפונקציה עלתה "קרה" בלי מטמון בזיכרון) ממשיכים לעבוד עם העותק האחרון.
// רק אם אין שום עותק, ואם SHEET_CSV_URL עדיין מוגדר - נופלים לגיליון הגוגל הישן.
const { getAdminStore } = require('./blobs-store');
const { MOSAD_1 } = require('./nedarim');

const LIST_BLOB_KEY = 'nedarim-community-list.json';
const EXTRA_BLOB_KEY = 'extra-eligible-tz.json';

// רענון רגיל: לכל היותר פעם ב-10 דקות - ייצוא רשימה מלאה הוא פעולה כבדה, ואין
// סיבה להעמיס על נדרים פלוס בכל הרשמה.
const CACHE_TTL_MS = 10 * 60 * 1000;
// ת.ז שלא נמצאה ברשימה מאלצת רענון מוקדם (חבר שהגבאי הוסיף זה עתה יוכל להירשם
// מיד) - אבל לא יותר מפעם ב-2 דקות, כדי שניסיונות חוזרים של ת.ז לא זכאית לא יגרמו
// להצפה של נדרים פלוס.
const MISS_REFRESH_MIN_MS = 2 * 60 * 1000;

let cache = { data: null, fetchedAt: 0 };

function cleanPhone(phone) {
  return phone ? String(phone).trim().replace(/[-\s]/g, '') : '';
}

// מפתח השוואה לת.ז: ספרות בלבד וללא אפסים מובילים - כדי ש-012345678 (כפי שנדרים
// פלוס מרפד) ו-12345678 (כפי שהמשתמש אולי הקליד) ייחשבו אותה ת.ז.
function tzKey(tz) {
  return String(tz || '').replace(/\D/g, '').replace(/^0+/, '');
}

// פרסור CSV מלא (שדות בגרשיים, פסיקים ושורות חדשות בתוך שדה, "" כגרש בודד) -
// בשמות ובכתובות של תורמים יש לא מעט פסיקים, כך שפיצול נאיבי לפי פסיק לא מספיק.
function parseCsv(text, delimiter) {
  const rows = [];
  let row = [];
  let field = '';
  let inQuotes = false;

  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (inQuotes) {
      if (ch === '"') {
        if (text[i + 1] === '"') { field += '"'; i++; } else { inQuotes = false; }
      } else {
        field += ch;
      }
    } else if (ch === '"') {
      inQuotes = true;
    } else if (ch === delimiter) {
      row.push(field); field = '';
    } else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && text[i + 1] === '\n') i++;
      row.push(field); field = '';
      rows.push(row); row = [];
    } else {
      field += ch;
    }
  }
  if (field !== '' || row.length) { row.push(field); rows.push(row); }
  return rows.map(r => r.map(c => c.trim())).filter(r => r.some(Boolean));
}

// קבצי ייצוא ממערכות ישראליות מגיעים לעיתים ב-windows-1255 ולא ב-UTF-8 - אם פענוח
// UTF-8 מייצר תווים פגומים, מפענחים מחדש ב-windows-1255.
function decodeCsvBuffer(buffer) {
  const utf8 = new TextDecoder('utf-8').decode(buffer);
  if (!utf8.includes('�')) return utf8.replace(/^﻿/, '');
  return new TextDecoder('windows-1255').decode(buffer);
}

function findColumn(header, patterns) {
  return header.findIndex(h => patterns.some(p => p.test(h)));
}

// הפיכת קובץ הייצוא לרשימת {tz, phone}. העמודות מזוהות לפי שם הכותרת (התיעוד של
// נדרים פלוס לא מפרט את סדר העמודות, ועדיף לא להסתמך על מיקום קבוע).
function parseTormimCsv(text) {
  const firstLine = text.split(/\r?\n/, 1)[0] || '';
  const delimiter = ['\t', ';', ','].reduce((best, d) =>
    firstLine.split(d).length > firstLine.split(best).length ? d : best, ',');

  const rows = parseCsv(text, delimiter);
  if (rows.length === 0) return [];

  const header = rows[0];
  const tzCol = findColumn(header, [/זהות/, /ת\.?\s*ז/, /zeout/i]);
  const phoneCol = findColumn(header, [/טלפון/, /נייד/, /phone/i]);

  if (tzCol === -1) {
    throw new Error(`לא נמצאה עמודת מספר זהות בייצוא רשימת התורמים מנדרים פלוס (כותרות: ${header.join(' | ')})`);
  }

  return rows.slice(1)
    .map(r => ({ tz: String(r[tzCol] || '').replace(/\D/g, ''), phone: phoneCol === -1 ? '' : cleanPhone(r[phoneCol]) }))
    .filter(r => tzKey(r.tz));
}

async function fetchFromNedarim() {
  const apiPassword = process.env.NEDARIM_TORMIM_API_PASSWORD || process.env.NEDARIM_API_PASSWORD;
  if (!apiPassword) {
    throw new Error('חסר משתנה סביבה NEDARIM_TORMIM_API_PASSWORD - יש להגדיר אותו בהגדרות האתר בנטליפיי');
  }

  const params = new URLSearchParams({ Action: 'GetTormimCsv', MosadId: MOSAD_1, ApiPassword: apiPassword, ToMail: '0' });
  const response = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?${params.toString()}`);
  if (!response.ok) {
    throw new Error(`כשל בשליפת רשימת התורמים מנדרים פלוס: ${response.status}`);
  }

  const text = decodeCsvBuffer(await response.arrayBuffer());

  // שגיאה (מפתח שגוי / ללא הרשאה לפעולה) חוזרת כ-JSON או כטקסט חשוף ולא כ-CSV
  if (/^\s*\{/.test(text)) {
    let message = text;
    try { message = JSON.parse(text).Message || text; } catch (e) { /* טקסט חשוף */ }
    throw new Error(`נדרים פלוס החזיר שגיאה בייצוא רשימת התורמים: ${String(message).slice(0, 200)}`);
  }

  const list = parseTormimCsv(text);
  if (list.length === 0) {
    // רשימה ריקה כמעט בוודאות היא תקלה ולא מצב אמיתי - לא דורסים איתה עותק תקין
    throw new Error('ייצוא רשימת התורמים מנדרים פלוס חזר ריק');
  }
  return list;
}

// הגיליון הישן - משמש רק כגיבוי אחרון, אם עדיין מוגדר SHEET_CSV_URL
async function fetchFromLegacySheet() {
  const csvUrl = process.env.SHEET_CSV_URL;
  if (!csvUrl) return null;

  const response = await fetch(csvUrl);
  if (!response.ok) {
    throw new Error(`כשל בשליפת גיליון הקהילה: ${response.status}`);
  }
  // מניחים שהשורה הראשונה היא כותרות: תז,טלפון
  const rows = parseCsv(await response.text(), ',').slice(1);
  return rows.filter(r => r[0]).map(r => ({ tz: String(r[0]).trim(), phone: cleanPhone(r[1]) }));
}

async function readStoredList(event) {
  try {
    const stored = await getAdminStore(event).get(LIST_BLOB_KEY, { type: 'json' });
    return stored && Array.isArray(stored.list) ? stored : null;
  } catch (e) {
    return null;
  }
}

async function writeStoredList(event, list, fetchedAt) {
  try {
    await getAdminStore(event).setJSON(LIST_BLOB_KEY, { list, fetchedAt });
  } catch (e) {
    console.error('[community] כשל בשמירת עותק רשימת התורמים ב-Blobs:', e.message);
  }
}

// maxAgeMs - הגיל המקסימלי של רשימה שמוכנים לקבל בלי לפנות שוב לנדרים פלוס.
// event הוא אירוע ה-Lambda הגולמי, נדרש כדי לחבר את הקשר ה-Blobs (ראו blobs-store.js).
async function fetchCommunityList(event, maxAgeMs = CACHE_TTL_MS) {
  const now = Date.now();
  if (cache.data && (now - cache.fetchedAt) < maxAgeMs) {
    return cache.data;
  }

  if (!cache.data) {
    const stored = await readStoredList(event);
    if (stored) {
      cache = { data: stored.list, fetchedAt: stored.fetchedAt || 0 };
      if ((now - cache.fetchedAt) < maxAgeMs) return cache.data;
    }
  }

  try {
    const list = await fetchFromNedarim();
    cache = { data: list, fetchedAt: now };
    await writeStoredList(event, list, now);
    return list;
  } catch (err) {
    console.error('[community]', err.message);
    if (cache.data) {
      // עותק קודם (גם אם לא הכי טרי) עדיף על חסימת כולם. מסמנים אותו כ"נבדק עכשיו"
      // כדי שבזמן תקלה בנדרים פלוס לא ננסה שוב בכל בקשה ובקשה.
      cache.fetchedAt = now;
      return cache.data;
    }

    const legacy = await fetchFromLegacySheet();
    if (legacy) return legacy;
    throw err;
  }
}

// בודק ברשימה המשלימה שהמנהל מנהל ידנית (בנפרד מרשימת התורמים). כשל בקריאה
// מה-Blobs לא אמור לחסום את הבדיקה מול הרשימה הראשית - לכן נבלע שקט ל-null.
async function getExtraEligibleRecord(tz, event) {
  try {
    const store = getAdminStore(event);
    const list = (await store.get(EXTRA_BLOB_KEY, { type: 'json' })) || [];
    const cleanTz = String(tz).trim();
    const found = list.find(r => r.tz === cleanTz);
    return found ? { tz: found.tz, phone: found.phone || '' } : null;
  } catch (e) {
    return null;
  }
}

async function getCommunityRecord(tz, event) {
  const cleanTz = String(tz).trim();
  const key = tzKey(cleanTz);

  if (key) {
    let list = await fetchCommunityList(event);
    let found = list.find(r => tzKey(r.tz) === key);
    if (!found) {
      list = await fetchCommunityList(event, MISS_REFRESH_MIN_MS);
      found = list.find(r => tzKey(r.tz) === key);
    }
    if (found) return found;
  }

  return getExtraEligibleRecord(cleanTz, event);
}

// מאפשר רענון כפוי מיידי של המטמון בזיכרון (הבקשה הבאה תפנה שוב לנדרים פלוס)
function clearCache() {
  cache = { data: null, fetchedAt: 0 };
}

module.exports = { fetchCommunityList, getCommunityRecord, clearCache, parseTormimCsv };
