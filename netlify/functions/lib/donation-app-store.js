// אחסון פרופילי "ערב התרמה" - שם לזיהוי וסף הסכום שנעולים לכל מקבל קישור
// ל-control.html/display.html (ראו donation-app/). מקור אמת יחיד: מסמך JSON
// בודד ב-Blobs עם כל הפרופילים, באותה שיטה שכבר נמצאת בשימוש ברשימת הזכאים
// (admin-eligible-list.js) - כמות הפרופילים הצפויה קטנה, כך שאין צורך במסמך
// נפרד לכל פרופיל.
//
// הלוגו/סטריפ/רקע *לא* חלק מהפרופיל: תמונות שעוברות דרך השרת מטושטשות אצל
// חלק מהמשתמשים ע"י תוכנות סינון תוכן (כמו נטפרי), גם כשמוטבעות כ-base64
// בתוך JSON. לכן המנהל שולח אותן לנמען ישירות (וואטסאפ/מייל), והנמען בוחר
// אותן מקומית במחשב שלו ב-display.html - בלי שהן יעברו ברשת בכלל.
const { getStore, connectLambda } = require('@netlify/blobs');
const crypto = require('crypto');

const STORE_KEY = 'profiles.json';

function getDonationStore(event) {
  connectLambda(event);
  return getStore('donation-app');
}

async function listProfiles(event) {
  const store = getDonationStore(event);
  const profiles = (await store.get(STORE_KEY, { type: 'json' })) || [];
  return profiles
    .map(p => ({ id: p.id, name: p.name, threshold: p.threshold, updatedAt: p.updatedAt }))
    .sort((a, b) => String(b.updatedAt || '').localeCompare(String(a.updatedAt || '')));
}

async function getConfigById(event, id) {
  const store = getDonationStore(event);
  const profiles = (await store.get(STORE_KEY, { type: 'json' })) || [];
  return profiles.find(p => p.id === id) || null;
}

function validateProfileInput({ name, threshold }) {
  if (!name || !String(name).trim()) throw new Error('יש להזין שם לזיהוי הפרופיל');

  const numThreshold = Number(threshold);
  if (!Number.isFinite(numThreshold) || numThreshold <= 0) throw new Error('סף הסכום חייב להיות מספר חיובי');

  return numThreshold;
}

async function saveProfile(event, input, savedBy) {
  const numThreshold = validateProfileInput(input);
  const store = getDonationStore(event);
  const profiles = (await store.get(STORE_KEY, { type: 'json' })) || [];
  const now = new Date().toISOString();

  if (input.id) {
    const existing = profiles.find(p => p.id === input.id);
    if (!existing) throw new Error('הפרופיל לעדכון לא נמצא');

    existing.name = String(input.name).trim();
    existing.threshold = numThreshold;
    existing.updatedAt = now;
    existing.updatedBy = savedBy || existing.updatedBy || null;

    await store.setJSON(STORE_KEY, profiles);
    return existing;
  }

  const record = {
    id: crypto.randomBytes(6).toString('hex'),
    name: String(input.name).trim(),
    threshold: numThreshold,
    createdAt: now,
    updatedAt: now,
    createdBy: savedBy || null,
    updatedBy: savedBy || null
  };

  profiles.push(record);
  await store.setJSON(STORE_KEY, profiles);
  return record;
}

async function deleteProfile(event, id) {
  const store = getDonationStore(event);
  const profiles = (await store.get(STORE_KEY, { type: 'json' })) || [];
  const filtered = profiles.filter(p => p.id !== id);
  await store.setJSON(STORE_KEY, filtered);
  return { deleted: filtered.length !== profiles.length };
}

module.exports = { listProfiles, getConfigById, saveProfile, deleteProfile };
