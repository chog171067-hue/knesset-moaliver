// אחסון אירועי "מהנעשה ונשמע". מקור אמת יחיד: מסמך JSON בודד ב-Blobs, באותה
// שיטה של donation-app-store.js (כמות האירועים הצפויה קטנה).
//
// תמונות מועלות לאתר עצמו (store נפרד 'news-images', מוגשות דרך
// /news-images/<id> - ראו news-image.js), כי תמונות מגוגל דרייב נחסמות אצל
// חלק מהמשתמשים ע"י נטפרי. סרטונים נשארים בגוגל דרייב - נשמר רק מזהה הקובץ.
//
// פריט ב-media הוא אחד מאלה:
//   { type: 'image', imageId }  - תמונה שהועלתה לאתר
//   { type: 'video', fileId }   - סרטון בגוגל דרייב
//   { type: 'image', fileId }   - תמונה מדרייב (אירועים ישנים, לפני המעבר להעלאה לאתר)
// התמונה הראשונה ברשימה היא תמונת השער שמוצגת בריבוע האירוע.
const { getStore, connectLambda } = require('@netlify/blobs');
const crypto = require('crypto');

const STORE_KEY = 'events.json';
const MAX_MEDIA = 40;

function getNewsStore(event) {
  connectLambda(event);
  return getStore('news');
}

function getImagesStore(event) {
  connectLambda(event);
  return getStore('news-images');
}

const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
const MAX_IMAGE_BYTES = 4 * 1024 * 1024;

// הדפדפן של המנהל מקטין ומכווץ כל תמונה לפני ההעלאה (ראו admin.html), כך
// שהמגבלה כאן היא רק רשת ביטחון - מגבלת גוף הבקשה של פונקציה בנטליפיי היא כ-6MB
async function saveImage(event, base64, contentType) {
  if (!IMAGE_TYPES.includes(contentType)) throw new Error('סוג קובץ לא נתמך - רק תמונות JPG / PNG / WEBP');
  const buffer = Buffer.from(String(base64 || ''), 'base64');
  if (!buffer.length) throw new Error('הקובץ ריק');
  if (buffer.length > MAX_IMAGE_BYTES) throw new Error('התמונה גדולה מדי');
  const imageId = crypto.randomBytes(10).toString('hex');
  await getImagesStore(event).set(imageId, buffer, { metadata: { contentType } });
  return imageId;
}

async function getImage(event, imageId) {
  if (!IMAGE_ID_RE.test(String(imageId || ''))) return null;
  const result = await getImagesStore(event).getWithMetadata(imageId, { type: 'arrayBuffer' });
  if (!result) return null;
  return { buffer: Buffer.from(result.data), contentType: (result.metadata && result.metadata.contentType) || 'image/jpeg' };
}

async function deleteImages(event, imageIds) {
  const store = getImagesStore(event);
  await Promise.all(imageIds.map(id => store.delete(id).catch(() => {})));
}

function imageIdsOf(record) {
  return ((record && record.media) || []).filter(m => m.imageId).map(m => m.imageId);
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
// מזהה תמונה שהועלתה לאתר (נוצר ב-saveImage)
const IMAGE_ID_RE = /^[a-f0-9]{20}$/;

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
    if (m && m.type === 'image' && m.imageId) {
      const imageId = String(m.imageId);
      if (!IMAGE_ID_RE.test(imageId)) throw new Error('אחת התמונות אינה תקינה');
      return { type: 'image', imageId };
    }
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
    const keptIds = imageIdsOf(clean);
    const removedIds = imageIdsOf(existing).filter(id => !keptIds.includes(id));
    Object.assign(existing, clean, { updatedAt: now, updatedBy: savedBy || existing.updatedBy || null });
    await store.setJSON(STORE_KEY, events);
    await deleteImages(event, removedIds);
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
  const removed = events.find(e => e.id === id);
  const filtered = events.filter(e => e.id !== id);
  await store.setJSON(STORE_KEY, filtered);
  if (removed) await deleteImages(event, imageIdsOf(removed));
  return { deleted: !!removed };
}

module.exports = { listEvents, listPublicEvents, saveEvent, deleteEvent, saveImage, getImage };
