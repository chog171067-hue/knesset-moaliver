// שכבת גישה יחידה לרשימת חברי הקהילה הזכאים ולחסימות.
// כל פונקציה אחרת (verify-tz, identity-signup, bind-tz, וכל פעולות האזור האישי)
// חייבת להשתמש בקובץ הזה ולא לממש בדיקה משלה - כך יש מקור אמת אחד בלבד.
//
// שלושה מקורות, כולם ב-Netlify Blobs (store "admin-data"):
// 1. רשימת התורמים (community-roster.json) - קובץ שהמנהל מייצא מכרטיס "תורמים"
//    בנדרים פלוס ומעלה בדף הניהול (ראו admin-community.js). כל העלאה מחליפה את
//    הרשימה כולה.
// 2. רשימה משלימה ידנית (extra-eligible-tz.json) - ת.ז שהמנהל מוסיף אחת-אחת
//    (ראו admin-eligible-list.js).
// 3. חסימות (blocked-tz.json) - ת.ז חסומות. חסימה מונעת פתיחת חשבון, ומשהה חשבון
//    קיים (בלי למחוק אותו). העלאת קובץ שמופיעה בו ת.ז חסומה מבטלת את החסימה שלה.
//
// אימות בעלות: בהרשמה חובה להשתמש במייל שרשום לת.ז ברשימה, ונטליפיי שולחת אליו
// מייל אימות - כך שמי שיודע רק ת.ז של חבר אחר לא יכול לפתוח חשבון על שמו.
const { getAdminStore } = require('./blobs-store');
const { ADMIN_TZ } = require('./admin-auth');

const ROSTER_KEY = 'community-roster.json';
const EXTRA_KEY = 'extra-eligible-tz.json';
const BLOCKED_KEY = 'blocked-tz.json';

// מפתח השוואה לת.ז: ספרות בלבד וללא אפסים מובילים - כדי ש-012345678 ו-12345678
// ייחשבו אותה ת.ז (אקסל נוטה להשמיט אפסים מובילים מעמודה מספרית).
function tzKey(tz) {
  return String(tz == null ? '' : tz).replace(/\D/g, '').replace(/^0+/, '');
}

function cleanPhone(phone) {
  return phone ? String(phone).trim().replace(/[-\s]/g, '') : '';
}

function normalizeEmail(email) {
  return String(email || '').trim().toLowerCase();
}

// "yossi@gmail.com" -> "yo***@gmail.com" - רמז למשתמש באיזה מייל להירשם, בלי לחשוף אותו
function maskEmail(email) {
  const clean = normalizeEmail(email);
  const at = clean.indexOf('@');
  if (at < 1) return '';
  return clean.slice(0, Math.min(2, at)) + '***' + clean.slice(at);
}

async function readJson(event, key, fallback) {
  const data = await getAdminStore(event).get(key, { type: 'json' });
  return data == null ? fallback : data;
}

async function getRoster(event) {
  return readJson(event, ROSTER_KEY, { list: [], uploadedAt: null, uploadedBy: '', fileName: '' });
}

async function getExtraList(event) {
  return readJson(event, EXTRA_KEY, []);
}

async function getBlockedList(event) {
  return readJson(event, BLOCKED_KEY, []);
}

async function saveRoster(event, roster) {
  await getAdminStore(event).setJSON(ROSTER_KEY, roster);
}

async function saveBlockedList(event, list) {
  await getAdminStore(event).setJSON(BLOCKED_KEY, list);
}

async function isTzBlocked(tz, event) {
  const key = tzKey(tz);
  if (!key || key === tzKey(ADMIN_TZ)) return false; // המנהל לעולם לא נחסם - אחרת אין דרך לבטל
  const blocked = await getBlockedList(event);
  return blocked.some(b => tzKey(b.tz) === key);
}

// מחזיר את רשומת הזכאות של ת.ז (מהקובץ, ואם אין - מהרשימה הידנית), או null.
// לא בודק חסימה - לזה יש את isTzBlocked.
async function getCommunityRecord(tz, event) {
  const key = tzKey(tz);
  if (!key) return null;

  const roster = await getRoster(event);
  const fromRoster = roster.list.find(r => tzKey(r.tz) === key);
  if (fromRoster) {
    return {
      tz: fromRoster.tz,
      name: [fromRoster.firstName, fromRoster.lastName].filter(Boolean).join(' '),
      email: normalizeEmail(fromRoster.email),
      phone: (fromRoster.phones || [])[0] || '',
      source: 'roster'
    };
  }

  const extra = await getExtraList(event);
  const fromExtra = extra.find(r => tzKey(r.tz) === key);
  if (fromExtra) {
    return {
      tz: fromExtra.tz,
      name: fromExtra.name || '',
      email: normalizeEmail(fromExtra.email),
      phone: fromExtra.phone || '',
      source: 'extra'
    };
  }

  return null;
}

// בדיקה משותפת לכל פעולה באזור האישי: משתמש מחובר, עם ת.ז מקושרת, שאינה חסומה.
// מחזיר { ok: true, user, tz, phone } או { ok: false, statusCode, error, suspended }.
async function requireActiveMember(event, context) {
  const user = context.clientContext && context.clientContext.user;
  const tz = user && user.app_metadata && String(user.app_metadata.verified_tz || '').trim();
  if (!tz) {
    return { ok: false, statusCode: 401, error: 'עליך להתחבר עם חשבון מאומת כדי לצפות בנתונים' };
  }
  let blocked;
  try {
    blocked = await isTzBlocked(tz, event);
  } catch (e) {
    console.error('[community] כשל בבדיקת חסימה:', e.message);
    return { ok: false, statusCode: 500, error: 'שגיאת שרת, נסה שוב מאוחר יותר' };
  }
  if (blocked) {
    return { ok: false, statusCode: 403, suspended: true, error: 'החשבון שלך באזור האישי מושהה. לפרטים יש לפנות לגבאי.' };
  }
  return { ok: true, user, tz, phone: String(user.app_metadata.verified_phone || '').trim() };
}

module.exports = {
  tzKey,
  cleanPhone,
  normalizeEmail,
  maskEmail,
  getRoster,
  getExtraList,
  getBlockedList,
  saveRoster,
  saveBlockedList,
  isTzBlocked,
  getCommunityRecord,
  requireActiveMember
};
