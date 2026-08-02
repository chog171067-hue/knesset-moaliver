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
const MAX_WRITE_ATTEMPTS = 5;

function getKioskStore(event) {
  connectLambda(event);
  return getStore('library-kiosk');
}

// כתיבה בטוחה מול עדכונים מקבילים: קוראים את המסמך הנוכחי (עם ה-ETag שלו),
// מפעילים עליו את מוטציית הקריאה (mutateFn יכולה לזרוק כדי לבטל את הכתיבה -
// למשל "שם משתמש כבר תפוס"), וכותבים בחזרה רק אם ה-ETag לא השתנה בינתיים.
// אם כן השתנה (modified: false) - קוראים מחדש ומנסים שוב, עד MAX_WRITE_ATTEMPTS.
async function writeWithRetry(store, key, defaultValue, mutateFn) {
  for (let attempt = 0; attempt < MAX_WRITE_ATTEMPTS; attempt++) {
    const existing = await store.getWithMetadata(key, { type: 'json' });
    const data = (existing && existing.data) || defaultValue;
    const etag = existing && existing.etag;

    const result = mutateFn(data);

    const writeResult = await store.setJSON(key, data, etag ? { onlyIfMatch: etag } : { onlyIfNew: true });
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

function toPublicUser(user) {
  if (!user) return null;
  const { passwordSalt, passwordHash, ...publicFields } = user;
  return publicFields;
}

async function listUsers(event) {
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  return Object.values(users).map(toPublicUser).sort((a, b) => a.username.localeCompare(b.username, 'he'));
}

async function findUserByUsername(event, username) {
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  const needle = String(username || '').trim().toLowerCase();
  return Object.values(users).find(u => u.username.toLowerCase() === needle) || null;
}

async function createUser(event, { username, password }) {
  const cleanUsername = String(username || '').trim();
  if (!cleanUsername) throw new Error('שם משתמש הוא שדה חובה');
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

async function resetPassword(event, { userId, newPassword }) {
  if (!newPassword || String(newPassword).length < 4) throw new Error('הסיסמה חייבת להכיל לפחות 4 תווים');

  const store = getKioskStore(event);
  const { salt, hash } = hashPassword(String(newPassword));

  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.passwordSalt = salt;
    user.passwordHash = hash;
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });
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

async function listUserTransactions(event, userId) {
  const store = getKioskStore(event);
  const transactions = (await store.get(TRANSACTIONS_KEY, { type: 'json' })) || [];
  return transactions
    .filter(t => t.userId === userId)
    .sort((a, b) => b.timestamp.localeCompare(a.timestamp));
}

module.exports = {
  hashPassword,
  verifyPassword,
  toPublicUser,
  listUsers,
  findUserByUsername,
  createUser,
  resetPassword,
  setUserActive,
  deleteUser,
  addManualBalance,
  appendTransaction,
  listUserTransactions
};
