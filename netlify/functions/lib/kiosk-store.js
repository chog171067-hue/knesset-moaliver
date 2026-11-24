// שכבת גישה לנתונים של "עמדת הקיוסק לספרייה" (משתמשים, יתרות, עסקאות) - ראו
// מסמך האפיון: users.json ו-transactions.json הם מקור האמת היחיד, וכל שאר
// הקוד (עמוד ניהול, עמדת הקיוסק בעתיד) עובר דרך הפונקציות כאן ולא נוגע
// ב-Blobs ישירות. כשירצו בעתיד להחליף את האחסון (למשל ל-Postgres, כדי לתמוך
// בכמה עמדות עם עומס גבוה יותר), רק הקובץ הזה אמור להשתנות.
//
// שני המסמכים (users.json, transactions.json) נשמרים כ-blob יחיד כל אחד -
// סביר לגמרי לעמדת ספרייה בודדת עם מאות משתמשים לכל היותר, ומאפשר עדכון
// אטומי-בפועל (optimistic concurrency לפי ETag, ראו writeWithRetry) בלי
// לנהל טרנזקציות אמיתיות שה-Blobs לא תומך בהן.
const { getStore, connectLambda } = require('@netlify/blobs');
const crypto = require('crypto');

const USERS_KEY = 'users.json';
const TRANSACTIONS_KEY = 'transactions.json';
const CONFIG_KEY = 'config.json';
const PENDING_TOPUPS_KEY = 'pending-topups.json';
// אישורי תשלום מנדרים פלוס שלא הצלחנו לשייך לחשבון - נשמרים לטיפול ידני, כדי
// שתשלום אמיתי לעולם לא "ייעלם" בשקט (נדרים פלוס לא שולחים אישור פעם שנייה)
const ORPHAN_TOPUPS_KEY = 'orphan-topups.json';
// יומן שחזורי סיסמה בטלפון (ימות המשיח) - כל ניסיון נרשם כאן, גם כושל. מאותו
// יומן מחושבות גם מגבלות הניסיונות (ראו PHONE_RESET_LIMITS), כך שאין מקור אמת נוסף.
const PASSWORD_RESETS_KEY = 'password-resets.json';
const MAX_PASSWORD_RESET_LOG = 3000;
// עסקאות שכבר טופלו מה-Webhook של נדרים פלוס (מזהה עסקה ← תוצאה) - כדי שעסקה לא תיטען פעמיים
const WEBHOOK_PROCESSED_KEY = 'nedarim-webhook-processed.json';
// תשלומים בקטגוריית הקיוסק שלא נמצא להם חשבון - ממתינים לפתיחת חשבון עם אותה ת"ז או לשיוך ידני
const WAITING_CREDITS_KEY = 'waiting-credits.json';
// יתרות מהמערכת הישנה (סייפר) - רשימה שהמנהל מעלה פעם אחת; כל יתרה ממומשת פעם אחת
const LEGACY_BALANCES_KEY = 'legacy-balances.json';
const MAX_WRITE_ATTEMPTS = 8;

// passwordResetPhone: מספר הטלפון של שחזור הסיסמה בימות המשיח, מוצג במסך הכניסה בעמדה
// signupAmount: ההטענה הראשונה שנדרשת כדי לפתוח חשבון בעמדה (נכנסת כולה ליתרה)
// communityJoinUrl: דף נדרים פלוס להקמת הוראת קבע "דמי חבר" (ריק = בלי כפתור הצטרפות)
// memberKevaGroupe / memberKevaMinAmount: איזו הוראת קבע מזכה בחברות מיידית (עד עדכון הקובץ)
const DEFAULT_CONFIG = {
  pricePerPage: 0.2,
  allowedApps: [],
  textEditorEnabled: false,
  passwordResetPhone: '',
  signupAmount: 10,
  communityJoinUrl: 'https://nedar.im/icYF',
  memberKevaGroupe: 'דמי חבר',
  memberKevaMinAmount: 100,
  // חיוב על זמן שימוש בעמדה (לא לחברי קהילה): לדקה, נגבה בסוף כל 10 דקות
  usagePricePerMinute: 0.2
};

const USAGE_BLOCK_MINUTES = 10;

// ⚠️ עקביות הנתונים - קריטי כאן כי מדובר בכסף:
// כברירת מחדל, קריאה מ-Netlify Blobs עוברת דרך מטמון (CDN) ועלולה להחזיר נתונים
// ישנים של כמה שניות. קריאה ישנה + כתיבה = דריסת עדכון שנעשה בינתיים (למשל החזר
// שקרא יתרה מלפני החיוב). לכן מבקשים "עקביות חזקה" (קריאה ישירות מהמקור, בלי מטמון)
// בכל פעם שהסביבה מספקת את הכתובת הדרושה לכך. בכל מקרה, הכתיבות מותנות ב-ETag
// (ראו writeWithRetry), כך שגם קריאה ישנה לא יכולה לדרוס נתונים - רק לגרום לניסיון חוזר.
let lastStoreMode = 'unknown';

function findUncachedEdgeURL(event) {
  try {
    const data = JSON.parse(Buffer.from(event.blobs, 'base64').toString('utf8'));
    if (data.uncachedEdgeURL) return data.uncachedEdgeURL;
    if (data.uncached_edge_url) return data.uncached_edge_url;
  } catch (e) { /* אין event.blobs תקין - ננסה מקור אחר */ }
  try {
    const raw = globalThis.netlifyBlobsContext || process.env.NETLIFY_BLOBS_CONTEXT;
    if (raw) {
      const ctx = JSON.parse(Buffer.from(raw, 'base64').toString('utf8'));
      if (ctx.uncachedEdgeURL) return ctx.uncachedEdgeURL;
    }
  } catch (e) { /* אין הקשר סביבה */ }
  return null;
}

function getKioskStore(event) {
  connectLambda(event);
  const uncachedEdgeURL = findUncachedEdgeURL(event);
  if (uncachedEdgeURL) {
    lastStoreMode = 'strong';
    return getStore({ name: 'library-kiosk', consistency: 'strong', uncachedEdgeURL });
  }
  lastStoreMode = 'eventual';
  return getStore('library-kiosk');
}

function getStoreMode(event) {
  getKioskStore(event);
  return lastStoreMode;
}

// כתיבה בטוחה מול עדכונים מקבילים: קוראים את המסמך הנוכחי (עם ה-ETag שלו),
// מפעילים עליו את מוטציית הקריאה (mutateFn יכולה לזרוק כדי לבטל את הכתיבה -
// למשל "שם משתמש כבר תפוס"), וכותבים בחזרה רק אם ה-ETag לא השתנה בינתיים.
// אם כן השתנה (modified: false) - קוראים מחדש ומנסים שוב, עד MAX_WRITE_ATTEMPTS.
//
// ⚠️ משתמשים ב-store.set ולא ב-store.setJSON: ב-@netlify/blobs 10.x יש באג שבו setJSON
// לא שולח את תנאי ה-ETag לשרת (הכתיבה תמיד "מצליחה" ודורסת). זה גרם בפועל לאובדן
// עסקאות ולחישובי יתרה שגויים. store.set מעביר את התנאי כראוי.
async function writeWithRetry(store, key, defaultValue, mutateFn) {
  for (let attempt = 0; attempt < MAX_WRITE_ATTEMPTS; attempt++) {
    if (attempt > 0) {
      // המתנה הולכת וגדלה - נותנת למטמון זמן להתעדכן אם הקריאה הקודמת הייתה ישנה
      await new Promise(resolve => setTimeout(resolve, 250 * attempt));
    }
    const existing = await store.getWithMetadata(key, { type: 'json' });
    const data = (existing && existing.data) || defaultValue;
    const etag = existing && existing.etag;

    const result = mutateFn(data);

    const writeResult = await store.set(key, JSON.stringify(data), etag ? { onlyIfMatch: etag } : { onlyIfNew: true });
    if (writeResult.modified) return result;
  }
  throw new Error('שגיאת שרת: לא ניתן לשמור את השינוי כרגע עקב עומס, נסה שוב');
}

function hashPassword(password) {
  const salt = crypto.randomBytes(16).toString('hex');
  const hash = crypto.scryptSync(password, salt, 64).toString('hex');
  return { salt, hash };
}

function verifyPassword(password, salt, hash) {
  const candidate = crypto.scryptSync(password, salt, 64);
  const expected = Buffer.from(hash, 'hex');
  return candidate.length === expected.length && crypto.timingSafeEqual(candidate, expected);
}

// טלפון ישראלי: ספרות בלבד (מקפים/רווחים מוסרים), 9-10 ספרות, מתחיל ב-0
function normalizePhone(phone) {
  const clean = String(phone || '').replace(/[-\s]/g, '');
  return /^0\d{8,9}$/.test(clean) ? clean : null;
}

function toPublicUser(user) {
  if (!user) return null;
  const { passwordSalt, passwordHash, tempPasswordSalt, tempPasswordHash, tempFailedAttempts, ...publicFields } = user;
  return publicFields;
}

// שם המשתמש בקיוסק הוא תעודת זהות. בטלפון אנשים מקלידים לפעמים בלי אפס מוביל,
// לכן משווים ספרות בלבד, בלי אפסים מובילים, ורק כששם המשתמש עצמו הוא מספר.
function normalizeTz(value) {
  const digits = String(value || '').trim();
  if (!/^\d{5,9}$/.test(digits)) return null;
  return digits.replace(/^0+/, '') || null;
}

// מספר המתקשר כפי שימות המשיח מעבירה אותו (ApiPhone) - לפעמים בפורמט בינלאומי
function normalizeCallerPhone(phone) {
  let clean = String(phone || '').replace(/[^\d]/g, '');
  if (clean.startsWith('972')) clean = '0' + clean.slice(3);
  return normalizePhone(clean);
}

async function listUsers(event) {
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  return Object.values(users).map(toPublicUser).sort((a, b) => a.username.localeCompare(b.username, 'he'));
}

async function getUserById(event, userId) {
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  return toPublicUser(users[userId]) || null;
}

async function findUserByUsername(event, username) {
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  const needle = String(username || '').trim().toLowerCase();
  const exact = Object.values(users).find(u => u.username.toLowerCase() === needle);
  if (exact) return exact;
  // ת"ז עם/בלי אפס מוביל (012345678 / 12345678) - אותו חשבון
  const tz = normalizeTz(needle);
  return tz ? (Object.values(users).find(u => normalizeTz(u.username) === tz) || null) : null;
}

async function createUser(event, { username, password, displayName, phone }) {
  const cleanUsername = String(username || '').trim();
  if (!cleanUsername) throw new Error('שם משתמש הוא שדה חובה');
  if (String(displayName || '').trim().length < 2) throw new Error('שם מלא הוא שדה חובה (נדרש לתשלומים בנדרים פלוס)');
  const cleanPhone = normalizePhone(phone);
  if (!cleanPhone) throw new Error('יש להזין מספר טלפון תקין (נדרש לתשלומים בנדרים פלוס)');
  if (!password || String(password).length < 4) throw new Error('הסיסמה חייבת להכיל לפחות 4 תווים');

  const store = getKioskStore(event);
  const { salt, hash } = hashPassword(String(password));

  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const clash = Object.values(users).some(u => u.username.toLowerCase() === cleanUsername.toLowerCase());
    if (clash) throw new Error('שם המשתמש כבר תפוס');

    const id = crypto.randomUUID();
    const now = new Date().toISOString();
    users[id] = {
      id,
      username: cleanUsername,
      // שם מלא, לתצוגה בממשק הניהול ולשליחה כ"שם משלם" לנדרים פלוס בהטענת
      // כסף (ראו library-topup-init.js) - אופציונלי כדי לא לשבור משתמשים
      // ישנים שנוצרו בלי השדה הזה.
      displayName: String(displayName || '').trim(),
      phone: cleanPhone,
      passwordSalt: salt,
      passwordHash: hash,
      balance: 0,
      isActive: true,
      createdAt: now,
      updatedAt: now
    };
    return toPublicUser(users[id]);
  });
}

// עדכון שם מלא + טלפון (שניהם נשלחים לנדרים פלוס בכל הטענה - המוסד מחייב אותם)
async function setUserDetails(event, { userId, displayName, phone }) {
  const name = String(displayName || '').trim();
  if (name.length < 2) throw new Error('יש להזין שם מלא');
  const cleanPhone = normalizePhone(phone);
  if (!cleanPhone) throw new Error('יש להזין מספר טלפון תקין (למשל 0501234567)');

  const store = getKioskStore(event);
  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.displayName = name;
    user.phone = cleanPhone;
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });
}

async function resetPassword(event, { userId, newPassword }) {
  if (!newPassword || String(newPassword).length < 4) throw new Error('הסיסמה חייבת להכיל לפחות 4 תווים');

  const store = getKioskStore(event);
  const { salt, hash } = hashPassword(String(newPassword));

  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.passwordSalt = salt;
    user.passwordHash = hash;
    clearTempPassword(user);
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });
}

// ===================== שחזור סיסמה בטלפון (ימות המשיח) =====================
//
// העיקרון: הסיסמה הזמנית נשמרת בשדות נפרדים (tempPassword*), ולא מחליפה את
// הסיסמה הרגילה. כך, אם מישהו אחר ביקש איפוס (למשל התחזה למספר הטלפון), בעל
// החשבון עדיין נכנס עם הסיסמה הרגילה שלו, והסיסמה הזמנית פשוט פגה.
// כניסה עם הסיסמה הזמנית לא פותחת את הקיוסק - היא מאפשרת רק לקבוע סיסמה חדשה.

const TEMP_PASSWORD_DIGITS = 6;
const TEMP_PASSWORD_TTL_MINUTES = 15;
// כמה ניסיונות כניסה שגויים מותרים בזמן שיש סיסמה זמנית פעילה. אחרי זה היא נמחקת,
// כדי שאי אפשר יהיה לנחש אותה בעמדה (מיליון אפשרויות, אבל בלי הגבלה זה עדיין סיכון).
const TEMP_PASSWORD_MAX_FAILED_LOGINS = 5;

const PHONE_RESET_LIMITS = {
  // ניסיונות כושלים (תעודת זהות לא תואמת) מאותו מספר טלפון, בשעה האחרונה
  failedPerPhonePerHour: 3,
  // סיסמאות זמניות שהונפקו לאותו חשבון, ב-24 השעות האחרונות
  issuedPerUserPerDay: 3,
  // כל הניסיונות (מוצלחים וכושלים) מאותו מספר טלפון, ב-24 השעות האחרונות
  attemptsPerPhonePerDay: 10
};

function clearTempPassword(user) {
  delete user.tempPasswordSalt;
  delete user.tempPasswordHash;
  delete user.tempPasswordExpiresAt;
  delete user.tempFailedAttempts;
}

function hasActiveTempPassword(user, now = Date.now()) {
  return !!(user.tempPasswordHash && user.tempPasswordExpiresAt && Date.parse(user.tempPasswordExpiresAt) > now);
}

function generateTempPassword() {
  // crypto.randomInt - אקראיות קריפטוגרפית, לא Math.random
  let digits = '';
  for (let i = 0; i < TEMP_PASSWORD_DIGITS; i++) digits += String(crypto.randomInt(0, 10));
  return digits;
}

async function readPasswordResetLog(store) {
  return (await store.get(PASSWORD_RESETS_KEY, { type: 'json' })) || [];
}

async function appendPasswordResetLog(event, entry) {
  const store = getKioskStore(event);
  const record = { id: crypto.randomUUID(), timestamp: new Date().toISOString(), ...entry };
  await writeWithRetry(store, PASSWORD_RESETS_KEY, [], (log) => {
    log.push(record);
    if (log.length > MAX_PASSWORD_RESET_LOG) log.splice(0, log.length - MAX_PASSWORD_RESET_LOG);
    return record;
  });
  return record;
}

async function listPasswordResetLog(event) {
  const store = getKioskStore(event);
  const log = await readPasswordResetLog(store);
  return log.slice().sort((a, b) => b.timestamp.localeCompare(a.timestamp));
}

// טלפון מוסתר לתצוגה: הקידומת ו-2 הספרות האחרונות בלבד.
// נייד (10 ספרות): 050-•••••67 | קווי (9 ספרות): 02-•••••67
function maskPhone(phone) {
  const d = String(phone || '').replace(/\D/g, '');
  if (d.length < 9) return '';
  const prefixLength = d.length === 10 ? 3 : 2;
  return `${d.slice(0, prefixLength)}-${'•'.repeat(d.length - prefixLength - 2)}${d.slice(-2)}`;
}

// למסך "שכחתי סיסמה" בעמדה: לפי תעודת הזהות שהוקלדה, מחזיר את הטלפון הרשום
// בצורה מוסתרת בלבד. הטלפון המלא לעולם לא נשלח לעמדה.
async function getPasswordResetInfo(event, username) {
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  const needle = String(username || '').trim().toLowerCase();
  const tz = normalizeTz(needle);
  const user = Object.values(users).find(u =>
    u.username.toLowerCase() === needle || (tz && normalizeTz(u.username) === tz));
  if (!user || !user.isActive) return { found: false };
  const maskedPhone = maskPhone(user.phone);
  return maskedPhone ? { found: true, maskedPhone } : { found: false };
}

// בודק אם מותר למספר הטלפון הזה (ולחשבון, אם כבר זוהה) לבקש איפוס כרגע.
// מחזיר null אם מותר, או סיבת חסימה ('phone-locked' / 'user-limit').
async function checkPhoneResetLimits(event, { phone, userId }) {
  const store = getKioskStore(event);
  const log = await readPasswordResetLog(store);
  const now = Date.now();
  const since = (ms) => log.filter(e => Date.parse(e.timestamp) > now - ms);
  const lastHour = since(60 * 60 * 1000);
  const lastDay = since(24 * 60 * 60 * 1000);

  const failedThisHour = lastHour.filter(e => e.phone === phone && e.outcome === 'mismatch').length;
  if (failedThisHour >= PHONE_RESET_LIMITS.failedPerPhonePerHour) return 'phone-locked';

  const attemptsToday = lastDay.filter(e => e.phone === phone && (e.outcome === 'mismatch' || e.outcome === 'issued')).length;
  if (attemptsToday >= PHONE_RESET_LIMITS.attemptsPerPhonePerDay) return 'phone-locked';

  if (userId) {
    const issuedToday = lastDay.filter(e => e.userId === userId && e.outcome === 'issued').length;
    if (issuedToday >= PHONE_RESET_LIMITS.issuedPerUserPerDay) return 'user-limit';
  }
  return null;
}

// מחפש חשבון פעיל שתעודת הזהות שלו היא tz והטלפון הרשום בו הוא phone.
// מחזיר את המשתמש (בלי שדות סודיים) או null - בלי לפרט מה בדיוק לא התאים.
async function findUserForPhoneReset(event, { tz, phone }) {
  const cleanTz = normalizeTz(tz);
  if (!cleanTz || !phone) return null;
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  const user = Object.values(users).find(u => normalizeTz(u.username) === cleanTz);
  if (!user || !user.isActive || user.phone !== phone) return null;
  return toPublicUser(user);
}

// מנפיק סיסמה זמנית ומחזיר אותה (כטקסט גלוי - פעם אחת בלבד, להקראה בטלפון).
// בודק שוב את התאמת הטלפון בתוך הכתיבה עצמה, למקרה שהפרטים השתנו בינתיים.
async function issueTemporaryPassword(event, { userId, phone }) {
  const tempPassword = generateTempPassword();
  const { salt, hash } = hashPassword(tempPassword);
  const expiresAt = new Date(Date.now() + TEMP_PASSWORD_TTL_MINUTES * 60 * 1000).toISOString();

  const store = getKioskStore(event);
  const user = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const u = users[userId];
    if (!u || !u.isActive || u.phone !== phone) throw new Error('החשבון אינו זמין לאיפוס');
    u.tempPasswordSalt = salt;
    u.tempPasswordHash = hash;
    u.tempPasswordExpiresAt = expiresAt;
    u.tempFailedAttempts = 0;
    u.updatedAt = new Date().toISOString();
    return toPublicUser(u);
  });

  return { tempPassword, expiresAt, user, ttlMinutes: TEMP_PASSWORD_TTL_MINUTES };
}

// אימות כניסה בעמדה. מחזיר:
//   { ok: false }                         - פרטים שגויים
//   { ok: true, user, viaTemp: false }    - סיסמה רגילה
//   { ok: true, user, viaTemp: true }     - סיסמה זמנית בתוקף: חובה להחליף סיסמה
// בכישלון כשיש סיסמה זמנית פעילה - סופרים ניסיון שגוי, ואחרי כמה ניסיונות מוחקים אותה.
async function authenticateKioskUser(event, { username, password }) {
  const user = await findUserByUsername(event, username);
  if (!user) return { ok: false };
  const pwd = String(password);

  if (verifyPassword(pwd, user.passwordSalt, user.passwordHash)) {
    return { ok: true, user, viaTemp: false };
  }

  if (!hasActiveTempPassword(user)) return { ok: false };

  if (verifyPassword(pwd, user.tempPasswordSalt, user.tempPasswordHash)) {
    return { ok: true, user, viaTemp: true };
  }

  // ניסיון שגוי בזמן שיש סיסמה זמנית - נספר
  const store = getKioskStore(event);
  const burned = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const u = users[user.id];
    if (!u || !hasActiveTempPassword(u)) return false;
    u.tempFailedAttempts = (u.tempFailedAttempts || 0) + 1;
    if (u.tempFailedAttempts >= TEMP_PASSWORD_MAX_FAILED_LOGINS) {
      clearTempPassword(u);
      return true;
    }
    return false;
  });
  if (burned) {
    await appendPasswordResetLog(event, {
      userId: user.id, username: user.username, phone: user.phone,
      outcome: 'temp-burned', note: `הסיסמה הזמנית בוטלה אחרי ${TEMP_PASSWORD_MAX_FAILED_LOGINS} ניסיונות כניסה שגויים`
    }).catch(() => {});
  }
  return { ok: false };
}

// החלפת סיסמה ע"י המשתמש עצמו בעמדה (אחרי כניסה עם סיסמה זמנית, או בכלל).
// האימות של הסיסמה הנוכחית נעשה בתוך הכתיבה, כך שאין חלון זמן בין בדיקה לשינוי.
async function changeOwnPassword(event, { username, currentPassword, newPassword }) {
  const next = String(newPassword || '');
  if (next.length < 4) throw new Error('הסיסמה החדשה חייבת להכיל לפחות 4 תווים');

  const existing = await findUserByUsername(event, username);
  if (!existing) throw new Error('שם משתמש או סיסמה שגויים');

  const { salt, hash } = hashPassword(next);
  const store = getKioskStore(event);
  let usedTemp = false;

  const user = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const u = users[existing.id];
    if (!u) throw new Error('שם משתמש או סיסמה שגויים');
    const cur = String(currentPassword || '');
    const viaMain = verifyPassword(cur, u.passwordSalt, u.passwordHash);
    const viaTemp = !viaMain && hasActiveTempPassword(u) && verifyPassword(cur, u.tempPasswordSalt, u.tempPasswordHash);
    if (!viaMain && !viaTemp) throw new Error('הסיסמה הזמנית שגויה או שפג תוקפה - יש להתקשר שוב לקבלת סיסמה חדשה');
    if (!u.isActive) throw new Error('החשבון מושבת - יש לפנות לגבאים');
    if (next === cur) throw new Error('הסיסמה החדשה חייבת להיות שונה מהסיסמה הזמנית');
    usedTemp = viaTemp;
    u.passwordSalt = salt;
    u.passwordHash = hash;
    clearTempPassword(u);
    u.updatedAt = new Date().toISOString();
    return toPublicUser(u);
  });

  if (usedTemp) {
    await appendPasswordResetLog(event, {
      userId: user.id, username: user.username, phone: user.phone,
      outcome: 'completed', note: 'המשתמש קבע סיסמה חדשה בעמדה'
    }).catch(() => {});
  }
  return user;
}

async function setUserActive(event, { userId, isActive }) {
  const store = getKioskStore(event);
  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.isActive = !!isActive;
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });
}

// הרשאה לצאת מהקיוסק לשולחן העבודה של Windows (כפתור "יציאה למחשב" בעמדה).
// רק מנהל באתר יכול להעניק אותה - משתמש קיוסק לא יכול לשנות אותה לעצמו.
async function setUserCanExit(event, { userId, canExitKiosk }) {
  const store = getKioskStore(event);
  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.canExitKiosk = !!canExitKiosk;
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });
}

async function deleteUser(event, { userId }) {
  const store = getKioskStore(event);
  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    if (!users[userId]) throw new Error('משתמש לא נמצא');
    delete users[userId];
    return { deleted: true };
  });
}

async function appendTransaction(event, entry) {
  const store = getKioskStore(event);
  const record = {
    id: crypto.randomUUID(),
    timestamp: new Date().toISOString(),
    ...entry
  };
  await writeWithRetry(store, TRANSACTIONS_KEY, [], (transactions) => {
    transactions.push(record);
    return record;
  });
  return record;
}

// הוספת יתרה ידנית ע"י איש צוות (למשל תשלום במזומן בדלפק) - מעדכן את היתרה
// ורושם עסקה מסוג topup, שניהם באותה קריאה כדי למנוע מצב של יתרה מעודכנת
// בלי רישום מקביל בהיסטוריה (או להפך).
async function addManualBalance(event, { userId, amount, description, performedBy }) {
  const numericAmount = Number(amount);
  if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
    throw new Error('סכום הטעינה חייב להיות מספר חיובי');
  }

  const store = getKioskStore(event);
  const updatedUser = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.balance = Number((user.balance + numericAmount).toFixed(2));
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });

  const transaction = await appendTransaction(event, {
    userId,
    type: 'topup',
    source: 'manual',
    amount: numericAmount,
    description: description || 'הטענת כסף ידנית ע"י הצוות',
    performedBy: performedBy || null
  });

  return { user: updatedUser, transaction };
}

// חיוב הדפסה: בודק יתרה ומחייב באותה כתיבה אטומית (לא "בדוק ואז חייב" בשני
// שלבים נפרדים) - כדי שלא יהיה חלון זמן שבו שתי בקשות חיוב מקבילות לאותו
// משתמש יעברו את הבדיקה שתיהן על סמך אותה יתרה ישנה. במקרה של יתרה לא
// מספיקה זורקים שגיאה מסומנת (err.insufficientFunds) לפני כל כתיבה בפועל -
// כך שגם לא מתבזבזת כתיבה מיותרת על "אין שינוי".
function cleanPrintTitle(title) {
  return String(title || '').replace(/[\u0000-\u001f<>]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 80);
}

async function chargeForPrint(event, { userId, pages, title }) {
  const numericPages = Number(pages);
  if (!Number.isInteger(numericPages) || numericPages <= 0) {
    throw new Error('מספר עמודים לא תקין');
  }

  const config = await getConfig(event);
  const cost = Number((numericPages * config.pricePerPage).toFixed(2));

  const store = getKioskStore(event);
  const chargedUser = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    if (!user.isActive) throw new Error('החשבון מושבת');
    if (user.balance < cost) {
      const err = new Error('היתרה אינה מספיקה לביצוע ההדפסה');
      err.insufficientFunds = true;
      err.balance = user.balance;
      err.cost = cost;
      throw err;
    }
    user.balance = Number((user.balance - cost).toFixed(2));
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });

  const transaction = await appendTransaction(event, {
    userId,
    type: 'charge',
    source: 'print',
    amount: cost,
    // title - מה הודפס (למשל "אוצר החכמה - שם המסמך"), להיסטוריית ההדפסות בעמדה
    description: `הדפסת ${numericPages} עמודים` + (cleanPrintTitle(title) ? ` - ${cleanPrintTitle(title)}` : ''),
    pages: numericPages
  });

  return { balance: chargedUser.balance, cost, transaction };
}

// חיוב על זמן שימוש בעמדה: העמדה מבקשת חיוב בסוף כל 10 דקות (או על הדקות שנותרו
// כשהיתרה נגמרת והחשבון מתנתק). העלות = דקות × תעריף לדקה, ולעולם לא יותר מהיתרה
// (היתרה לא יורדת מתחת ל-0). חבר קהילה (isMember, נבדק בפונקציה) - לא מחויב.
async function chargeForUsage(event, { userId, minutes, isMember }) {
  const numericMinutes = Number(minutes);
  if (!Number.isInteger(numericMinutes) || numericMinutes <= 0 || numericMinutes > USAGE_BLOCK_MINUTES) {
    throw new Error('מספר דקות לא תקין');
  }
  const config = await getConfig(event);
  const price = Number(config.usagePricePerMinute) || 0;

  const store = getKioskStore(event);
  let cost = 0;
  const user = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const u = users[userId];
    if (!u) throw new Error('משתמש לא נמצא');
    if (!u.isActive) throw new Error('החשבון מושבת');
    cost = isMember || price <= 0 ? 0 : Math.min(Number((numericMinutes * price).toFixed(2)), Math.max(0, u.balance));
    if (cost <= 0) return toPublicUser(u);
    u.balance = Number((u.balance - cost).toFixed(2));
    u.updatedAt = new Date().toISOString();
    return toPublicUser(u);
  });

  if (cost > 0) {
    // כשהיתרה הספיקה רק לחלק מהזמן - רושמים את הדקות שבאמת שולמו
    const paidMinutes = Math.min(numericMinutes, Math.max(1, Math.round(cost / price)));
    await appendTransaction(event, {
      userId,
      type: 'charge',
      source: 'usage',
      amount: cost,
      description: `שימוש בעמדה - ${paidMinutes} ${paidMinutes === 1 ? 'דקה' : 'דקות'}`,
      minutes: paidMinutes
    });
  }
  return { balance: user.balance, cost, pricePerMinute: price };
}

// החזר על הדפסה שנכשלה (מדפסת מנותקת, נגמר נייר וכו'): עמדת הקיוסק מחייבת מראש
// ברגע שעבודת ההדפסה נכנסת לתור, ואם העבודה נכשלה ובוטלה - מבקשת כאן החזר.
// ההחזר מותר פעם אחת בלבד לכל חיוב (מסומן על עסקת החיוב עצמה), רק על חיוב
// הדפסה של אותו משתמש, ובדיוק בסכום שחויב - כך שאי אפשר "לייצר" כסף דרכו.
async function refundPrintCharge(event, { userId, transactionId }) {
  const store = getKioskStore(event);

  // אם הקריאה עדיין לא "רואה" חיוב שנרשם לפני רגע (מטמון) - ממתינים ומנסים שוב
  let charge;
  for (let attempt = 0; ; attempt++) {
    try {
      charge = await markChargeRefunded(store, { userId, transactionId });
      break;
    } catch (err) {
      if (!err.chargeNotFound || attempt >= 5) throw err;
      await new Promise(resolve => setTimeout(resolve, 500 * (attempt + 1)));
    }
  }

  const updatedUser = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.balance = Number((user.balance + charge.amount).toFixed(2));
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });

  const transaction = await appendTransaction(event, {
    userId,
    type: 'topup',
    source: 'print-refund',
    amount: charge.amount,
    description: `החזר על הדפסה שנכשלה (${charge.pages || 1} עמודים)`,
    refundOf: transactionId
  });

  return { balance: updatedUser.balance, amount: charge.amount, transaction };
}

function markChargeRefunded(store, { userId, transactionId }) {
  return writeWithRetry(store, TRANSACTIONS_KEY, [], (transactions) => {
    const tx = transactions.find(t => t.id === transactionId);
    if (!tx || tx.userId !== userId || tx.type !== 'charge' || tx.source !== 'print') {
      const err = new Error('עסקת החיוב לא נמצאה');
      err.chargeNotFound = true;
      throw err;
    }
    if (tx.refunded) {
      const err = new Error('החיוב הזה כבר הוחזר');
      err.alreadyRefunded = true;
      throw err;
    }
    tx.refunded = true;
    tx.refundedAt = new Date().toISOString();
    return { amount: tx.amount, pages: tx.pages };
  });
}

// תעריף עמוד הדפסה ורשימת התוכנות המאושרות - נקרא ע"י עמדת הקיוסק (לבניית
// מסך בחירת התוכנה ולחישוב עלות הדפסה) וע"י פאנל הניהול (לעריכה).
async function getConfig(event) {
  const store = getKioskStore(event);
  const config = (await store.get(CONFIG_KEY, { type: 'json' })) || {};
  return { ...DEFAULT_CONFIG, ...config };
}

// עדכון מלא: allowedApps (אם נשלח) מחליף את כל הרשימה הקיימת, לא ממזג איתה -
// כך שמסך הניהול פשוט שולח בחזרה את הרשימה השלמה אחרי הוספה/הסרה/סידור מחדש.
async function updateConfig(event, patch) {
  if (patch.pricePerPage !== undefined) {
    const price = Number(patch.pricePerPage);
    if (!Number.isFinite(price) || price < 0) throw new Error('תעריף עמוד ההדפסה חייב להיות מספר לא שלילי');
  }
  if (patch.allowedApps !== undefined && !Array.isArray(patch.allowedApps)) {
    throw new Error('רשימת התוכנות המאושרות חייבת להיות מערך');
  }
  if (patch.signupAmount !== undefined) {
    const amount = Number(patch.signupAmount);
    if (!Number.isFinite(amount) || amount < 1) throw new Error('סכום פתיחת החשבון חייב להיות לפחות 1 ₪');
  }
  let joinUrl;
  if (patch.communityJoinUrl !== undefined) {
    joinUrl = String(patch.communityJoinUrl || '').trim();
    if (joinUrl && !/^https:\/\/[^\s]+$/i.test(joinUrl)) throw new Error('הקישור לדף ההצטרפות חייב להתחיל ב-https://');
  }
  if (patch.memberKevaMinAmount !== undefined) {
    const min = Number(patch.memberKevaMinAmount);
    if (!Number.isFinite(min) || min < 0) throw new Error('הסכום המינימלי להוראת הקבע חייב להיות מספר לא שלילי');
  }
  if (patch.usagePricePerMinute !== undefined) {
    const p = Number(patch.usagePricePerMinute);
    if (!Number.isFinite(p) || p < 0) throw new Error('תעריף השימוש לדקה חייב להיות מספר לא שלילי');
  }
  let resetPhone;
  if (patch.passwordResetPhone !== undefined) {
    resetPhone = String(patch.passwordResetPhone || '').trim();
    // ספרות, מקפים ורווחים בלבד (ריק = שחזור בטלפון לא מוצג בעמדה)
    if (resetPhone && !/^[\d\- ]{3,20}$/.test(resetPhone)) throw new Error('מספר הטלפון לשחזור סיסמה אינו תקין');
  }

  const store = getKioskStore(event);
  return writeWithRetry(store, CONFIG_KEY, { ...DEFAULT_CONFIG }, (config) => {
    if (patch.pricePerPage !== undefined) config.pricePerPage = Number(patch.pricePerPage);
    if (patch.allowedApps !== undefined) config.allowedApps = patch.allowedApps;
    if (patch.textEditorEnabled !== undefined) config.textEditorEnabled = !!patch.textEditorEnabled;
    if (resetPhone !== undefined) config.passwordResetPhone = resetPhone;
    if (patch.signupAmount !== undefined) config.signupAmount = Number(patch.signupAmount);
    if (joinUrl !== undefined) config.communityJoinUrl = joinUrl;
    if (patch.memberKevaGroupe !== undefined) config.memberKevaGroupe = String(patch.memberKevaGroupe || '').trim();
    if (patch.memberKevaMinAmount !== undefined) config.memberKevaMinAmount = Number(patch.memberKevaMinAmount);
    if (patch.usagePricePerMinute !== undefined) config.usagePricePerMinute = Number(patch.usagePricePerMinute);
    config.updatedAt = new Date().toISOString();
    return { ...DEFAULT_CONFIG, ...config };
  });
}

// ===================== פתיחת חשבון עצמית בעמדה =====================
//
// המשתמש ממלא שם, ת"ז, טלפון וסיסמה, ומשלם הטענה ראשונה (signupAmount). החשבון
// נוצר רק כשנדרים פלוס מאשרים את התשלום (ב-CallBack, ראו confirmPendingTopup) -
// עד אז הפרטים שמורים כ"פתיחת חשבון ממתינה" ב-pending-topups.json, עם הסיסמה
// כבר מגובבת (hash). כך אין חשבונות "ריקים" שנפתחו בלי תשלום.

// ספרת ביקורת של תעודת זהות ישראלית
function isValidIsraeliId(value) {
  const digits = String(value || '').trim();
  if (!/^\d{5,9}$/.test(digits)) return false;
  const padded = digits.padStart(9, '0');
  if (/^0+$/.test(padded)) return false;
  let sum = 0;
  for (let i = 0; i < 9; i++) {
    let n = Number(padded[i]) * ((i % 2) + 1);
    if (n > 9) n -= 9;
    sum += n;
  }
  return sum % 10 === 0;
}

const SIGNUP_PASSWORD_PATTERN = /^\d{4,8}$/;

// בדיקת הפרטים שהוקלדו בעמדה. מחזיר אותם מנוקים, או זורק שגיאה בעברית.
function validateSignupDetails({ tz, displayName, phone, password }) {
  const name = String(displayName || '').trim().replace(/\s+/g, ' ');
  if (name.length < 2) throw new Error('יש להזין שם מלא');
  if (name.split(' ').length < 2) throw new Error('יש להזין שם פרטי ושם משפחה');

  const cleanTz = String(tz || '').replace(/\D/g, '');
  if (!isValidIsraeliId(cleanTz)) throw new Error('מספר תעודת הזהות אינו תקין. יש לבדוק את המספר (כולל ספרת הביקורת)');

  const cleanPhone = normalizePhone(phone);
  if (!cleanPhone) throw new Error('יש להזין מספר טלפון תקין (למשל 0501234567)');

  const pwd = String(password || '');
  if (!SIGNUP_PASSWORD_PATTERN.test(pwd)) throw new Error('הסיסמה צריכה להיות בין 4 ל-8 ספרות (ספרות בלבד)');

  return { username: cleanTz, displayName: name, phone: cleanPhone, password: pwd };
}

function isTzTaken(users, username) {
  const tz = normalizeTz(username);
  return Object.values(users).some(u => u.username.toLowerCase() === username.toLowerCase()
    || (tz && normalizeTz(u.username) === tz));
}

const TZ_TAKEN_MESSAGE = 'כבר קיים חשבון עם תעודת הזהות הזאת. יש לחזור למסך הפתיחה, להקליד את תעודת הזהות ולהיכנס עם הסיסמה (ואם הסיסמה נשכחה - "שכחתי סיסמה").';

// מסך הפתיחה בעמדה: האם יש כבר חשבון (פעיל או מושבת) לת"ז / שם המשתמש הזה
async function accountExists(event, username) {
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  return isTzTaken(users, String(username || '').trim());
}

async function assertTzAvailable(event, username) {
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  if (isTzTaken(users, username)) throw new Error(TZ_TAKEN_MESSAGE);
}

// יוצר "פתיחת חשבון ממתינה" ומחזיר את האסימון לנדרים פלוס (Param1)
async function createPendingSignup(event, details, amount, channel = 'iframe') {
  const numericAmount = Number(amount);
  if (!Number.isFinite(numericAmount) || numericAmount <= 0) throw new Error('סכום פתיחת החשבון אינו מוגדר כראוי');

  await assertTzAvailable(event, details.username);

  const { salt, hash } = hashPassword(details.password);
  const token = crypto.randomUUID();
  const store = getKioskStore(event);
  return writeWithRetry(store, PENDING_TOPUPS_KEY, {}, (pending) => {
    pending[token] = {
      token,
      type: 'signup',
      channel,
      userId: null,
      amount: numericAmount,
      status: 'pending',
      createdAt: new Date().toISOString(),
      signup: {
        username: details.username,
        displayName: details.displayName,
        phone: details.phone,
        passwordSalt: salt,
        passwordHash: hash
      }
    };
    return { token, amount: numericAmount };
  });
}

// הטענת כסף מקוונת מול נדרים פלוס: זרימת "אישור שרתי בלבד" (ראו תיעוד ה-API,
// "אימות תשלום ואבטחה") - לעולם לא סומכים על מה שהדפדפן/העמדה "מספרים" לנו
// שהתשלום הצליח, אלא רק על ה-CallBack שמגיע ישירות משרתי נדרים פלוס.
//
// pending-topups.json הוא מקור האמת לזיהוי "לאיזה משתמש ואיזה סכום מיועד
// אסימון (token) ששלחנו ב-Param1" - כי Param1 עצמו לא נשמר אצל נדרים פלוס
// (ראו התיעוד: "טקסט חופשי מיועד לקאלבק בלבד, לא נשמר").
//
// שני מסמכים נפרדים (pending-topups.json, users.json) לא יכולים להיכתב
// כטרנזקציה אחת אמיתית ב-Blobs - לכן מטפלים בזה בשני שלבים מסודרים:
// 1) "תפיסת" האסימון (pending -> processing) באופן אטומי, כדי שקריאת קאלבק
//    כפולה (או race) לא תזכה את היתרה פעמיים.
// 2) זיכוי היתרה בפועל, ואז סימון האסימון כ-confirmed. אם שלב 2 נכשל
//    (למשל המשתמש נמחק בינתיים), מסמנים failed במקום להשאיר תקוע ב-processing
//    לצמיתות - מקרה קצה נדיר שדורש בדיקה ידנית, לא זיכוי אוטומטי כפול.
// channel: 'iframe' (טופס האשראי בעמדה/באתר) או 'online' (הדף הרגיל של נדרים - האזור האישי)
async function createPendingTopup(event, { userId, amount, channel = 'iframe' }) {
  const numericAmount = Number(amount);
  if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
    throw new Error('סכום ההטענה חייב להיות מספר חיובי');
  }

  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  if (!users[userId]) throw new Error('משתמש לא נמצא');

  const token = crypto.randomUUID();
  return writeWithRetry(store, PENDING_TOPUPS_KEY, {}, (pending) => {
    pending[token] = { token, userId, amount: numericAmount, channel, status: 'pending', createdAt: new Date().toISOString() };
    return { token, amount: numericAmount };
  });
}

// הרשומה "תשלום ממתין" נוצרת שניות ספורות לפני שהאישור מנדרים פלוס מגיע, והקריאה
// עלולה לבוא מהמטמון ולא לראות אותה עדיין. במקום לדחות מיד - ממתינים ומנסים שוב.
async function retryWhileTokenUnknown(fn) {
  const delaysMs = [400, 800, 1200, 1600, 2000]; // סה"כ כ-6 שניות - בתוך מגבלת 10 השניות של פונקציה
  for (let attempt = 0; ; attempt++) {
    try {
      return await fn();
    } catch (err) {
      if (!err.unknownToken || attempt >= delaysMs.length) throw err;
      await new Promise(resolve => setTimeout(resolve, delaysMs[attempt]));
    }
  }
}

function unknownTokenError() {
  const err = new Error('אסימון תשלום לא מוכר');
  err.unknownToken = true;
  return err;
}

async function claimPendingTopupForProcessing(event, token) {
  const store = getKioskStore(event);
  return retryWhileTokenUnknown(() => writeWithRetry(store, PENDING_TOPUPS_KEY, {}, (pending) => {
    const entry = pending[token];
    if (!entry) throw unknownTokenError();
    if (entry.status !== 'pending') {
      return { alreadyHandled: true, entry: { ...entry } };
    }
    entry.status = 'processing';
    return { alreadyHandled: false, entry: { ...entry } };
  }));
}

// שמירת אישור תשלום שלא שויך לחשבון, לטיפול ידני (ראו ORPHAN_TOPUPS_KEY)
async function recordOrphanTopup(event, details) {
  const store = getKioskStore(event);
  await writeWithRetry(store, ORPHAN_TOPUPS_KEY, [], (orphans) => {
    orphans.push({ ...details, receivedAt: new Date().toISOString() });
    return null;
  });
}

async function markPendingTopupOutcome(event, token, patch) {
  const store = getKioskStore(event);
  await retryWhileTokenUnknown(() => writeWithRetry(store, PENDING_TOPUPS_KEY, {}, (pending) => {
    const entry = pending[token];
    if (!entry) throw unknownTokenError();
    Object.assign(entry, patch);
    return entry;
  }));
}

// נקרא ע"י ה-CallBack בלבד, אחרי אימות ה-IP מול נדרים פלוס. הסכום שמזוכה הוא
// הסכום שנדרים פלוס בפועל מדווחת עליו (לא מה שביקשנו ביצירת האסימון) - כך
// שאם משום מה יש פער, היתרה תמיד תשקף את מה שבאמת חויב בכרטיס האשראי.
async function confirmPendingTopup(event, { token, nedarimAmount, nedarimTransactionId, confirmation, lastNum, raw }) {
  // זיכוי רק על עסקה שאושרה בפועל: לעסקת אשראי מאושרת יש תמיד מספר אישור.
  // הודעה בלי מספר אישור (למשל עסקה שנדחתה) לא מזכה - והקוראת שומרת אותה לבדיקה ידנית.
  if (!confirmation || !String(confirmation).trim()) {
    throw new Error('התקבלה הודעה מנדרים פלוס ללא מספר אישור - לא זוכה (ייתכן שהעסקה נדחתה)');
  }

  const claim = await claimPendingTopupForProcessing(event, token);
  if (claim.alreadyHandled) {
    return { duplicate: true, status: claim.entry.status };
  }

  const creditAmount = Number(nedarimAmount) > 0 ? Number(nedarimAmount) : claim.entry.amount;
  const isSignup = claim.entry.type === 'signup';

  try {
    const store = getKioskStore(event);
    const updatedUser = await writeWithRetry(store, USERS_KEY, {}, (users) => {
      if (isSignup) {
        // פתיחת חשבון: החשבון נוצר עכשיו, עם התשלום כיתרה התחלתית
        const s = claim.entry.signup;
        if (!s) throw new Error('פרטי פתיחת החשבון חסרים');
        if (isTzTaken(users, s.username)) throw new Error('התשלום התקבל, אך כבר קיים חשבון עם תעודת הזהות הזאת');
        const id = crypto.randomUUID();
        const now = new Date().toISOString();
        users[id] = {
          id,
          username: s.username,
          displayName: s.displayName,
          phone: s.phone,
          passwordSalt: s.passwordSalt,
          passwordHash: s.passwordHash,
          balance: Number(creditAmount.toFixed(2)),
          isActive: true,
          createdVia: 'kiosk-signup',
          createdAt: now,
          updatedAt: now
        };
        return toPublicUser(users[id]);
      }
      const user = users[claim.entry.userId];
      if (!user) throw new Error('משתמש לא נמצא (נמחק בזמן התשלום)');
      user.balance = Number((user.balance + creditAmount).toFixed(2));
      user.updatedAt = new Date().toISOString();
      return toPublicUser(user);
    });

    const transaction = await appendTransaction(event, {
      userId: updatedUser.id,
      type: 'topup',
      source: 'nedarim',
      amount: creditAmount,
      description: (isSignup ? 'פתיחת חשבון בעמדה - הטענה ראשונה' : 'הטענת כסף') +
        (claim.entry.channel === 'online' ? ' מהאזור האישי בנדרים פלוס'
          : claim.entry.channel === 'keva' ? ' מכרטיס הוראת הקבע' : ' באשראי דרך נדרים פלוס'),
      nedarimTransactionId: nedarimTransactionId || null,
      confirmation: confirmation || null,
      nedarimRaw: raw || null
    });

    await markPendingTopupOutcome(event, token, {
      status: 'confirmed',
      userId: updatedUser.id,
      // הסיסמה המגובבת כבר בחשבון עצמו - לא משאירים עותק ברשומת התשלום
      ...(isSignup ? { signup: { username: claim.entry.signup.username, displayName: claim.entry.signup.displayName } } : {}),
      confirmedAt: new Date().toISOString(),
      nedarimTransactionId: nedarimTransactionId || null,
      confirmation: confirmation || null,
      lastNum: lastNum || null
    });

    if (isSignup) {
      // תשלומים שחיכו לת"ז הזו (למשל תרומה בעמדת נדרים פלוס לפני פתיחת החשבון)
      await applyWaitingCredits(event, updatedUser).catch(e => console.error('[signup] טעינת תשלומים ממתינים נכשלה:', e.message));
    }

    return { duplicate: false, user: updatedUser, transaction };
  } catch (err) {
    const failPatch = { status: 'failed', failReason: err.message };
    if (isSignup && claim.entry.signup) {
      // נשמרים הפרטים לטיפול ידני, בלי הסיסמה המגובבת
      failPatch.signup = { username: claim.entry.signup.username, displayName: claim.entry.signup.displayName, phone: claim.entry.signup.phone };
    }
    await markPendingTopupOutcome(event, token, failPatch).catch(() => {});
    throw err;
  }
}

async function getPendingTopupStatus(event, token) {
  const store = getKioskStore(event);
  const pending = (await store.get(PENDING_TOPUPS_KEY, { type: 'json' })) || {};
  const entry = pending[token];
  // מהמטמון ייתכן שהרשומה עוד לא נראית - מבחינת העמדה זה פשוט "עדיין ממתין"
  if (!entry) return { status: 'pending', amount: 0, confirmedAt: null };
  return { status: entry.status, amount: entry.amount, confirmedAt: entry.confirmedAt || null };
}

async function listUserTransactions(event, userId) {
  const store = getKioskStore(event);
  const transactions = (await store.get(TRANSACTIONS_KEY, { type: 'json' })) || [];
  return transactions
    .filter(t => t.userId === userId)
    .sort((a, b) => b.timestamp.localeCompare(a.timestamp));
}

// ===================== Webhook של נדרים פלוס: תשלומים שנכנסים ליתרה =====================
//
// נדרים פלוס שולחים הודעה על כל עסקה מוצלחת במוסד. כאן מטפלים רק בעסקאות בקטגוריית
// הקיוסק (KIOSK_NEDARIM_GROUPE), שלא נוצרו באייפרם שלנו (אלה כבר נטענות דרך ה-CallBack
// שלהן - ראו confirmPendingTopup). סוגי העסקאות:
// - תשלום בדף הרגיל של נדרים (האזור האישי, כרטיס שמור) שנפתח מהעמדה: בהערות יש את
//   האסימון שלנו (Analytic) ← בדיוק החשבון/פתיחת החשבון שביקשו אותו.
// - תרומה בעמדת נדרים פלוס / חיוב חודשי של הוראת קבע בקטגוריה: לפי ת"ז, ואם חסרה - לפי
//   טלפון (רק כשיש חשבון אחד בדיוק עם הטלפון הזה).
// - אין חשבון מתאים ← "ממתין": נטען לבד כשנפתח חשבון עם אותה ת"ז, או בשיוך ידני בדף הניהול.

const TOKEN_IN_TEXT = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i;

function describeNedarimPayment(data) {
  if (data.KevaId) return 'חיוב חודשי של הוראת קבע בנדרים פלוס';
  const masof = String(data.MasofId || '').trim();
  if (masof && masof.toLowerCase() !== 'online') return 'תשלום בעמדת נדרים פלוס';
  return 'תשלום בנדרים פלוס';
}

// "תופס" מזהה עסקה באופן אטומי. false = כבר טופלה (הודעה כפולה)
async function claimWebhookTransaction(event, txId) {
  const store = getKioskStore(event);
  return writeWithRetry(store, WEBHOOK_PROCESSED_KEY, {}, (processed) => {
    if (processed[txId]) return false;
    processed[txId] = { at: new Date().toISOString(), outcome: 'processing' };
    return true;
  });
}

async function markWebhookOutcome(event, txId, outcome) {
  const store = getKioskStore(event);
  await writeWithRetry(store, WEBHOOK_PROCESSED_KEY, {}, (processed) => {
    processed[txId] = { ...(processed[txId] || {}), outcome, doneAt: new Date().toISOString() };
    return null;
  });
}

async function creditUserFromNedarim(event, { userId, amount, description, nedarimTransactionId, confirmation, raw }) {
  const store = getKioskStore(event);
  const user = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const u = users[userId];
    if (!u) throw new Error('משתמש לא נמצא');
    u.balance = Number((u.balance + amount).toFixed(2));
    u.updatedAt = new Date().toISOString();
    return toPublicUser(u);
  });
  await appendTransaction(event, {
    userId,
    type: 'topup',
    source: 'nedarim',
    amount,
    description,
    nedarimTransactionId: nedarimTransactionId || null,
    confirmation: confirmation || null,
    nedarimRaw: raw || null
  });
  return user;
}

async function findUserForNedarimPayment(event, { tz, phone }) {
  const store = getKioskStore(event);
  const users = Object.values((await store.get(USERS_KEY, { type: 'json' })) || {});
  const cleanTz = normalizeTz(String(tz || '').replace(/\D/g, ''));
  if (cleanTz) {
    const byTz = users.find(u => normalizeTz(u.username) === cleanTz);
    if (byTz) return { user: byTz, matchedBy: 'tz' };
    return null; // יש ת"ז ואין לה חשבון - לא מנחשים לפי טלפון (אולי בן משפחה)
  }
  const cleanPhone = normalizeCallerPhone(phone);
  if (cleanPhone) {
    const byPhone = users.filter(u => u.phone === cleanPhone);
    if (byPhone.length === 1) return { user: byPhone[0], matchedBy: 'phone' };
  }
  return null;
}

async function addWaitingCredit(event, entry) {
  const store = getKioskStore(event);
  await writeWithRetry(store, WAITING_CREDITS_KEY, [], (list) => {
    list.push({ ...entry, status: 'waiting', receivedAt: new Date().toISOString() });
    return null;
  });
}

// עסקה אחת מה-Webhook (אחרי אימות החתימה). מחזיר { outcome } לרישום ביומן.
async function processNedarimWebhookTransaction(event, data, { groupe }) {
  if (String(data.Groupe || '').trim() !== groupe) return { outcome: 'ignored-category' };
  if (String(data.DebitIframe || '') === '1') return { outcome: 'ignored-iframe' };
  const txId = String(data.TransactionId || '').trim();
  if (!txId) return { outcome: 'ignored-no-transaction-id' };

  if (!(await claimWebhookTransaction(event, txId))) return { outcome: 'duplicate' };

  const amount = Number(data.Amount);
  const confirmation = String(data.Confirmation || '').trim();
  const tz = String(data.Zeout || '').replace(/\D/g, '');
  const base = {
    transactionId: txId,
    amount: Number.isFinite(amount) ? amount : 0,
    tz,
    phone: String(data.Phone || '').trim(),
    name: String(data.ClientName || '').trim(),
    confirmation,
    description: describeNedarimPayment(data),
    comments: String(data.Comments || '').slice(0, 300)
  };

  try {
    // עסקה בלי מספר אישור = עסקה זמנית; מטבע זר / סכום לא תקין - לטיפול ידני, בלי טעינה
    if (!confirmation || String(data.Currency || '1') !== '1' || !(amount > 0)) {
      const reason = !confirmation ? 'עסקה ללא מספר אישור' : String(data.Currency || '1') !== '1' ? 'מטבע שאינו שקל' : 'סכום לא תקין';
      await addWaitingCredit(event, { ...base, reason, manualOnly: true });
      await markWebhookOutcome(event, txId, 'manual');
      return { outcome: 'manual', reason };
    }

    // תשלום שנפתח מהעמדה (האזור האישי בנדרים) - לפי האסימון שבהערות
    const tokenMatch = String(data.Comments || '').match(TOKEN_IN_TEXT);
    if (tokenMatch) {
      const store = getKioskStore(event);
      const pending = (await store.get(PENDING_TOPUPS_KEY, { type: 'json' })) || {};
      if (pending[tokenMatch[0].toLowerCase()] || pending[tokenMatch[0]]) {
        const token = pending[tokenMatch[0]] ? tokenMatch[0] : tokenMatch[0].toLowerCase();
        const result = await confirmPendingTopup(event, {
          token, nedarimAmount: data.Amount, nedarimTransactionId: txId,
          confirmation, lastNum: data.LastNum, raw: data
        });
        await markWebhookOutcome(event, txId, result.duplicate ? 'token-duplicate' : 'token');
        return { outcome: 'token' };
      }
    }

    const match = await findUserForNedarimPayment(event, { tz, phone: data.Phone });
    if (match) {
      await creditUserFromNedarim(event, {
        userId: match.user.id, amount, description: base.description,
        nedarimTransactionId: txId, confirmation, raw: data
      });
      await markWebhookOutcome(event, txId, `credited-${match.matchedBy}`);
      return { outcome: `credited-${match.matchedBy}` };
    }

    await addWaitingCredit(event, base);
    await markWebhookOutcome(event, txId, 'waiting');
    return { outcome: 'waiting' };
  } catch (err) {
    await markWebhookOutcome(event, txId, 'failed: ' + err.message).catch(() => {});
    throw err;
  }
}

// נקרא כשנפתח חשבון (בעמדה או בדף הניהול): טוען לו תשלומים שחיכו לת"ז שלו
async function applyWaitingCredits(event, user) {
  const tz = normalizeTz(user && user.username);
  if (!tz) return [];
  const store = getKioskStore(event);
  const claimed = await writeWithRetry(store, WAITING_CREDITS_KEY, [], (list) => {
    const mine = list.filter(c => c.status === 'waiting' && !c.manualOnly && normalizeTz(c.tz) === tz);
    mine.forEach(c => { c.status = 'applying'; });
    return mine.map(c => ({ ...c }));
  });
  for (const c of claimed) {
    await creditUserFromNedarim(event, {
      userId: user.id, amount: c.amount, description: c.description + ' (נטען בפתיחת החשבון)',
      nedarimTransactionId: c.transactionId, confirmation: c.confirmation
    });
    await writeWithRetry(store, WAITING_CREDITS_KEY, [], (list) => {
      const e = list.find(x => x.transactionId === c.transactionId);
      if (e) { e.status = 'applied'; e.userId = user.id; e.appliedAt = new Date().toISOString(); }
      return null;
    });
  }
  return claimed;
}

// ===================== פתיחת חשבון על חשבון תשלום ממתין =====================
//
// מי ששילם בנדרים פלוס (קטגוריית הקיוסק, עם הת"ז שלו) לפני שהיה לו חשבון - התשלום מחכה
// ב-waiting-credits.json. אם סך התשלומים הממתינים לת"ז הוא לפחות סכום פתיחת החשבון (10 ₪),
// פותחים את החשבון בלי תשלום נוסף, והתשלומים נטענים ליתרה. כל תשלום מסומן "applied" עם
// החשבון שקיבל אותו - נטען פעם אחת בלבד.

async function getWaitingCreditTotal(event, username) {
  const tz = normalizeTz(username);
  if (!tz) return 0;
  const store = getKioskStore(event);
  const list = (await store.get(WAITING_CREDITS_KEY, { type: 'json' })) || [];
  const total = list
    .filter(c => c.status === 'waiting' && !c.manualOnly && normalizeTz(c.tz) === tz)
    .reduce((sum, c) => sum + (Number(c.amount) || 0), 0);
  return Math.round(total * 100) / 100;
}

async function createPrepaidSignup(event, details) {
  const config = await getConfig(event);
  const required = Number(config.signupAmount) || 0;
  const available = await getWaitingCreditTotal(event, details.username);
  if (required <= 0 || available < required) {
    throw new Error(`לא נמצא תשלום ממתין של לפחות ${required} ₪ לתעודת הזהות הזו`);
  }

  const store = getKioskStore(event);
  const { salt, hash } = hashPassword(details.password);
  const user = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    if (isTzTaken(users, details.username)) throw new Error(TZ_TAKEN_MESSAGE);
    const id = crypto.randomUUID();
    const now = new Date().toISOString();
    users[id] = {
      id,
      username: details.username,
      displayName: details.displayName,
      phone: details.phone,
      passwordSalt: salt,
      passwordHash: hash,
      balance: 0,
      isActive: true,
      createdVia: 'kiosk-signup-prepaid',
      createdAt: now,
      updatedAt: now
    };
    return toPublicUser(users[id]);
  });

  const applied = await applyWaitingCredits(event, user);
  const total = Math.round(applied.reduce((sum, c) => sum + (Number(c.amount) || 0), 0) * 100) / 100;
  const fresh = await getUserById(event, user.id);
  return { user: fresh || user, loaded: total };
}

async function listWaitingCredits(event) {
  const store = getKioskStore(event);
  const list = (await store.get(WAITING_CREDITS_KEY, { type: 'json' })) || [];
  return list.slice().reverse();
}

// שיוך ידני מדף הניהול של תשלום ממתין לחשבון (לפי ת"ז / שם משתמש)
async function assignWaitingCredit(event, { transactionId, username, performedBy }) {
  const user = await findUserByUsername(event, username);
  if (!user) throw new Error('לא נמצא חשבון עם תעודת הזהות / שם המשתמש הזה');
  const store = getKioskStore(event);
  const entry = await writeWithRetry(store, WAITING_CREDITS_KEY, [], (list) => {
    const e = list.find(x => x.transactionId === String(transactionId));
    if (!e) throw new Error('התשלום לא נמצא');
    if (e.status !== 'waiting') throw new Error('התשלום כבר שויך');
    e.status = 'applying';
    return { ...e };
  });
  try {
    const updated = await creditUserFromNedarim(event, {
      userId: user.id, amount: entry.amount, description: entry.description + ' (שיוך ידני)',
      nedarimTransactionId: entry.transactionId, confirmation: entry.confirmation
    });
    await writeWithRetry(store, WAITING_CREDITS_KEY, [], (list) => {
      const e = list.find(x => x.transactionId === entry.transactionId);
      if (e) { e.status = 'applied'; e.userId = user.id; e.appliedAt = new Date().toISOString(); e.assignedBy = performedBy || ''; }
      return null;
    });
    return updated;
  } catch (err) {
    await writeWithRetry(store, WAITING_CREDITS_KEY, [], (list) => {
      const e = list.find(x => x.transactionId === entry.transactionId);
      if (e && e.status === 'applying') e.status = 'waiting';
      return null;
    }).catch(() => {});
    throw err;
  }
}

// ===================== יתרות מהמערכת הישנה (סייפר) =====================
//
// בסייפר לא נשמרו תעודות זהות - לכן ההתאמה לפי שם פרטי + שם משפחה (בכל סדר, בלי
// גרשיים/מקפים/רווחים כפולים). כשמשתמש נכנס לקיוסק ויש ברשימה יתרה שלא מומשה עם אותו שם
// בדיוק (ורק אחת כזו) - הקיוסק מציע לו לממש אותה, והיא נכנסת ליתרה שלו פעם אחת בלבד.

function legacyNameKey(fullName) {
  return String(fullName || '')
    .replace(/["'`\u05F3\u05F4.,()\-]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .split(' ')
    .filter(Boolean)
    .sort()
    .join(' ');
}

async function listLegacyBalances(event) {
  const store = getKioskStore(event);
  return (await store.get(LEGACY_BALANCES_KEY, { type: 'json' })) || [];
}

// העלאה (או העלאה חוזרת) של הרשימה. שורה שכבר מומשה (לפי המזהה מסייפר) נשארת ממומשת.
async function saveLegacyBalances(event, rows) {
  if (!Array.isArray(rows) || rows.length === 0) throw new Error('הרשימה ריקה');
  if (rows.length > 2000) throw new Error('הרשימה ארוכה מדי');
  const clean = rows.map((r, i) => {
    const amount = Math.round(Number(r.balance) * 100) / 100;
    return {
      legacyId: String(r.legacyId || `row-${i + 1}`).trim().slice(0, 40),
      firstName: String(r.firstName || '').trim().slice(0, 40),
      lastName: String(r.lastName || '').trim().slice(0, 40),
      userName: String(r.userName || '').trim().slice(0, 40),
      balance: Number.isFinite(amount) ? amount : 0,
      lastUse: String(r.lastUse || '').trim().slice(0, 30)
    };
  }).filter(r => r.balance > 0 && (r.firstName || r.lastName));
  if (clean.length === 0) throw new Error('לא נמצאו ברשימה שורות עם שם ויתרה גדולה מ-0');

  const store = getKioskStore(event);
  return writeWithRetry(store, LEGACY_BALANCES_KEY, [], (list) => {
    const claimedById = new Map(list.filter(e => e.claimed).map(e => [e.legacyId, e.claimed]));
    const next = clean.map(r => ({ ...r, claimed: claimedById.get(r.legacyId) || null }));
    list.splice(0, list.length, ...next);
    return { count: next.length, total: Math.round(next.reduce((sum, e) => sum + e.balance, 0) * 100) / 100 };
  });
}

function legacyMatchesFor(list, user) {
  const key = legacyNameKey(user.displayName);
  if (!key) return [];
  return list.filter(e => !e.claimed && e.balance > 0 && legacyNameKey(`${e.firstName} ${e.lastName}`) === key);
}

// לעמדה: האם יש למשתמש יתרה ישנה שלא מומשה. שני אנשים עם אותו שם ברשימה - לא מציעים
// (הגבאי מטפל ידנית), כדי שאף אחד לא יקבל יתרה של מישהו אחר.
async function findLegacyBalanceForUser(event, userId) {
  const user = await getUserById(event, userId);
  if (!user || !user.isActive) return null;
  const matches = legacyMatchesFor(await listLegacyBalances(event), user);
  if (matches.length !== 1) return null;
  return { legacyId: matches[0].legacyId, amount: matches[0].balance };
}

async function claimLegacyBalance(event, { userId, legacyId }) {
  const store = getKioskStore(event);
  const user = await getUserById(event, userId);
  if (!user || !user.isActive) throw new Error('החשבון לא נמצא');

  // קודם מסמנים כממומש (מונע מימוש כפול), ורק אז מוסיפים ליתרה
  const entry = await writeWithRetry(store, LEGACY_BALANCES_KEY, [], (list) => {
    const matches = legacyMatchesFor(list, user);
    const match = matches.length === 1 ? matches[0] : null;
    if (!match || match.legacyId !== String(legacyId)) {
      throw new Error('היתרה כבר מומשה, או שאינה תואמת לחשבון הזה. יש לפנות לגבאים.');
    }
    match.claimed = { userId: user.id, username: user.username, displayName: user.displayName || '', at: new Date().toISOString() };
    return { ...match };
  });

  try {
    const updated = await writeWithRetry(store, USERS_KEY, {}, (users) => {
      const u = users[userId];
      if (!u) throw new Error('משתמש לא נמצא');
      u.balance = Number((u.balance + entry.balance).toFixed(2));
      u.updatedAt = new Date().toISOString();
      return toPublicUser(u);
    });
    await appendTransaction(event, {
      userId,
      type: 'topup',
      source: 'legacy',
      amount: entry.balance,
      description: 'העברת יתרה מהמערכת הישנה (סייפר)',
      legacyId: entry.legacyId
    });
    return { balance: updated.balance, amount: entry.balance };
  } catch (error) {
    // ההוספה ליתרה נכשלה - מחזירים את היתרה הישנה למצב "לא מומש"
    await writeWithRetry(store, LEGACY_BALANCES_KEY, [], (list) => {
      const e = list.find(x => x.legacyId === entry.legacyId);
      if (e) e.claimed = null;
      return null;
    });
    throw error;
  }
}

// ===================== חיוב בודד מכרטיס הוראת הקבע =====================
//
// מי שיש לו הוראת קבע פעילה במוסד הראשי (אותה ת"ז ואותו טלפון כמו בחשבון) יכול להטעין
// יתרה בחיוב בודד מהכרטיס השמור בהוראה (TashlumBodedNew) - בלי להקליד כרטיס.
// הגבלות: עד KEVA_CHARGE_MAX_PER_CHARGE לחיוב ועד KEVA_CHARGE_MAX_PER_DAY ביום, ורק אחרי
// הסכמה חד-פעמית שנשמרת בחשבון (kevaChargeConsentAt). כל חיוב שמור כ"תשלום ממתין" עם
// channel 'keva' - כך הסכום היומי נספר, וה-Webhook מזהה את האסימון שבהערות ולא מזכה פעמיים.

const KEVA_CHARGE_MAX_PER_CHARGE = 50;
const KEVA_CHARGE_MAX_PER_DAY = 100;

// התאריך בישראל (YYYY-MM-DD) - "יום" לצורך ההגבלה היומית
function israelDate(iso) {
  return new Date(iso).toLocaleDateString('en-CA', { timeZone: 'Asia/Jerusalem' });
}

async function setKevaChargeConsent(event, userId) {
  const store = getKioskStore(event);
  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const u = users[userId];
    if (!u) throw new Error('משתמש לא נמצא');
    if (!u.kevaChargeConsentAt) u.kevaChargeConsentAt = new Date().toISOString();
    return u.kevaChargeConsentAt;
  });
}

// הוראת הקבע שנמצאה בבדיקה האחרונה (כדי שהחיוב עצמו לא יחכה לשליפה נוספת מנדרים)
async function setKevaChargeCache(event, userId, keva) {
  const store = getKioskStore(event);
  await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const u = users[userId];
    if (!u) return null;
    u.kevaChargeCache = keva ? { kevaId: keva.kevaId, last4: keva.last4, checkedAt: new Date().toISOString() } : null;
    return null;
  });
}

// סך החיובים מהוראת הקבע היום (כולל חיוב שעוד בתהליך; חיוב שנדחה לא נספר)
function kevaChargedToday(pending, userId, today) {
  return Object.values(pending)
    .filter(p => p.channel === 'keva' && p.userId === userId && p.status !== 'failed' && israelDate(p.createdAt) === today)
    .reduce((sum, p) => sum + (Number(p.amount) || 0), 0);
}

async function getKevaChargeTodayTotal(event, userId) {
  const store = getKioskStore(event);
  const pending = (await store.get(PENDING_TOPUPS_KEY, { type: 'json' })) || {};
  return Math.round(kevaChargedToday(pending, userId, israelDate(new Date().toISOString())) * 100) / 100;
}

// בדיקת ההגבלות ושמירת החיוב כ"ממתין" - באותה כתיבה, כך ששני חיובים במקביל לא יעברו את התקרה
async function reserveKevaCharge(event, { userId, amount, kevaId }) {
  const numericAmount = Math.round(Number(amount) * 100) / 100;
  if (!Number.isFinite(numericAmount) || numericAmount <= 0) throw new Error('סכום לא תקין');
  if (numericAmount > KEVA_CHARGE_MAX_PER_CHARGE) {
    throw new Error(`אפשר לחייב עד ${KEVA_CHARGE_MAX_PER_CHARGE} ₪ בכל פעם`);
  }

  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  const user = users[userId];
  if (!user) throw new Error('משתמש לא נמצא');
  if (!user.kevaChargeConsentAt) throw new Error('נדרש אישור חד-פעמי לפני חיוב מכרטיס הוראת הקבע');

  const token = crypto.randomUUID();
  const now = new Date().toISOString();
  return writeWithRetry(store, PENDING_TOPUPS_KEY, {}, (pending) => {
    const today = kevaChargedToday(pending, userId, israelDate(now));
    if (today + numericAmount > KEVA_CHARGE_MAX_PER_DAY + 0.001) {
      const left = Math.max(0, KEVA_CHARGE_MAX_PER_DAY - today);
      throw new Error(left > 0
        ? `אפשר לחייב עד ${KEVA_CHARGE_MAX_PER_DAY} ₪ ביום. היום נשאר עוד ${left} ₪.`
        : `הגעת לתקרה היומית (${KEVA_CHARGE_MAX_PER_DAY} ₪). אפשר לחייב שוב מחר.`);
    }
    pending[token] = { token, userId, amount: numericAmount, channel: 'keva', kevaId: String(kevaId), status: 'pending', createdAt: now };
    return { token, amount: numericAmount };
  });
}

// החיוב בנדרים הצליח: מזכים פעם אחת בלבד. אם ה-Webhook של אותה עסקה כבר הגיע וזיכה
// (לפי האסימון או לפי ת"ז) - רק מסמנים את החיוב כמאושר, בלי זיכוי נוסף.
async function completeKevaCharge(event, { token, nedarimAmount, nedarimTransactionId, confirmation, lastNum, raw }) {
  const txId = String(nedarimTransactionId || '').trim();
  if (txId && !(await claimWebhookTransaction(event, txId))) {
    await markPendingTopupOutcome(event, token, {
      status: 'confirmed', confirmedAt: new Date().toISOString(), creditedBy: 'webhook',
      nedarimTransactionId: txId, confirmation: confirmation || null, lastNum: lastNum || null
    }).catch(() => {});
    return { duplicate: true };
  }
  try {
    const result = await confirmPendingTopup(event, { token, nedarimAmount, nedarimTransactionId: txId, confirmation, lastNum, raw });
    if (txId) await markWebhookOutcome(event, txId, 'keva-charge').catch(() => {});
    return result;
  } catch (err) {
    if (txId) await markWebhookOutcome(event, txId, 'keva-charge failed: ' + err.message).catch(() => {});
    throw err;
  }
}

async function failKevaCharge(event, token, reason) {
  await markPendingTopupOutcome(event, token, { status: 'failed', failReason: String(reason || '').slice(0, 300) });
}

module.exports = {
  KEVA_CHARGE_MAX_PER_CHARGE,
  KEVA_CHARGE_MAX_PER_DAY,
  setKevaChargeConsent,
  setKevaChargeCache,
  getKevaChargeTodayTotal,
  reserveKevaCharge,
  completeKevaCharge,
  failKevaCharge,
  normalizeTz,
  normalizePhone,
  listLegacyBalances,
  saveLegacyBalances,
  findLegacyBalanceForUser,
  claimLegacyBalance,
  getStoreMode,
  hashPassword,
  verifyPassword,
  toPublicUser,
  listUsers,
  getUserById,
  findUserByUsername,
  createUser,
  resetPassword,
  normalizeCallerPhone,
  maskPhone,
  getPasswordResetInfo,
  findUserForPhoneReset,
  checkPhoneResetLimits,
  issueTemporaryPassword,
  appendPasswordResetLog,
  listPasswordResetLog,
  authenticateKioskUser,
  changeOwnPassword,
  setUserDetails,
  setUserCanExit,
  setUserActive,
  deleteUser,
  addManualBalance,
  appendTransaction,
  listUserTransactions,
  getConfig,
  updateConfig,
  chargeForPrint,
  refundPrintCharge,
  createPendingTopup,
  validateSignupDetails,
  assertTzAvailable,
  accountExists,
  getWaitingCreditTotal,
  createPrepaidSignup,
  chargeForUsage,
  createPendingSignup,
  confirmPendingTopup,
  recordOrphanTopup,
  getPendingTopupStatus,
  processNedarimWebhookTransaction,
  applyWaitingCredits,
  listWaitingCredits,
  assignWaitingCredit
};
