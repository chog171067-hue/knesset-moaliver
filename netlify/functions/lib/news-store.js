// אחסון אירועי "מהנעשה ונשמע". הקבצים עצמם (תמונות/סרטונים) לא נשמרים כאן
// אלא בגוגל דרייב של המנהל - כאן נשמרים רק הכותרת, התאריך, התיאור ומזהי
// הקבצים בדרייב. מקור אמת יחיד: מסמך JSON בודד ב-Blobs, באותה שיטה של
// donation-app-store.js (כמות האירועים הצפויה קטנה).
const { getStore, connectLambda } = require('@netlify/blobs');
const crypto = require('crypto');

const STORE_KEY = 'events.json';
const MAX_MEDIA = 40;

function getNewsStore(event) {
  connectLambda(event);
  return getStore('news');
}

async function readEvents(event) {
  const store = getNewsStore(event);
  return (await store.get(STORE_KEY, { type: 'json' })) || [];
}

// מהחדש לישן לפי תאריך האירוע (ולא לפי תאריך העדכון)
function sortEvents(events) {
  return events.slice().sort((a, b) =>
    String(b.date || '').localeCompare(String(a.date || '')) ||
    String(b.createdAt || '').localeCompare(String(a.createdAt || ''))
  );
}

async function listEvents(event) {
  return sortEvents(await readEvents(event));
}

// התצוגה הציבורית מקבלת רק את השדות שנחוצים להצגה
async function listPublicEvents(event) {
  return (await listEvents(event)).map(e => ({
    id: e.id, title: e.title, date: e.date, description: e.description, media: e.media
  }));
}

// מזהה קובץ בדרייב: אותיות, ספרות, מקף וקו תחתון (ראו extractDriveId ב-admin.html)
const DRIVE_ID_RE = /^[A-Za-z0-9_-]{10,200}$/;

function normalizeInput(input) {
  const title = String(input.title || '').trim();
  const date = String(input.date || '').trim();
  const description = String(input.description || '').trim();

  if (!title) throw new Error('יש להזין כותרת לאירוע');
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) throw new Error('יש לבחור תאריך לאירוע');
  if (title.length > 200) throw new Error('הכותרת ארוכה מדי');
  if (description.length > 10000) throw new Error('התיאור ארוך מדי');

  const rawMedia = Array.isArray(input.media) ? input.media : [];
  if (rawMedia.length > MAX_MEDIA) throw new Error(`אפשר לצרף עד ${MAX_MEDIA} קבצים לאירוע`);
  const media = rawMedia.map(m => {
    const type = m && m.type === 'video' ? 'video' : 'image';
    const fileId = String((m && m.fileId) || '').trim();
    if (!DRIVE_ID_RE.test(fileId)) throw new Error('אחד מקישורי הדרייב אינו תקין');
    return { type, fileId };
  });

  return { title, date, description, media };
}

async function saveEvent(event, input, savedBy) {
  const clean = normalizeInput(input);
  const store = getNewsStore(event);
  const events = (await store.get(STORE_KEY, { type: 'json' })) || [];
  const now = new Date().toISOString();

  if (input.id) {
    const existing = events.find(e => e.id === input.id);
    if (!existing) throw new Error('האירוע לעדכון לא נמצא');
    Object.assign(existing, clean, { updatedAt: now, updatedBy: savedBy || existing.updatedBy || null });
    await store.setJSON(STORE_KEY, events);
    return existing;
  }

  const record = Object.assign({ id: crypto.randomBytes(6).toString('hex') }, clean, {
    createdAt: now,
    updatedAt: now,
    createdBy: savedBy || null,
    updatedBy: savedBy || null
  });
  events.push(record);
  await store.setJSON(STORE_KEY, events);
  return record;
}

async function deleteEvent(event, id) {
  const store = getNewsStore(event);
  const events = (await store.get(STORE_KEY, { type: 'json' })) || [];
  const filtered = events.filter(e => e.id !== id);
  await store.setJSON(STORE_KEY, filtered);
  return { deleted: filtered.length !== events.length };
}

module.exports = { listEvents, listPublicEvents, saveEvent, deleteEvent };
