// אחסון פרופילי "ערב התרמה" - הלוגואים, הסטריפ התחתון וסף הסכום שנעולים לכל
// מקבל קישור ל-control.html/display.html (ראו donation-app/). מקור אמת יחיד:
// מסמך JSON בודד ב-Blobs עם כל הפרופילים, באותה שיטה שכבר נמצאת בשימוש
// ברשימת הזכאים (admin-eligible-list.js) - כמות הפרופילים הצפויה קטנה, כך
// שאין צורך במסמך נפרד לכל פרופיל.
const { getStore, connectLambda } = require('@netlify/blobs');
const crypto = require('crypto');

const STORE_KEY = 'profiles.json';

// עד כ-650KB בפועל לכל תמונה (base64 מנפח בכ-33% מהגודל המקורי) - מספיק בנוחות
// ללוגו/סטריפ, ומונע פרופיל שמנפח את מסמך ה-Blob לגדלים לא סבירים.
const MAX_IMAGE_BASE64_LEN = 900000;

function getDonationStore(event) {
  connectLambda(event);
  return getStore('donation-app');
}

async function listProfiles(event) {
  const store = getDonationStore(event);
  const profiles = (await store.get(STORE_KEY, { type: 'json' })) || [];
  // רשימה לתצוגה בלבד - בלי התמונות עצמן (כבדות, ולא נחוצות ברשימה)
  return profiles
    .map(p => ({ id: p.id, name: p.name, threshold: p.threshold, updatedAt: p.updatedAt }))
    .sort((a, b) => String(b.updatedAt || '').localeCompare(String(a.updatedAt || '')));
}

async function getConfigById(event, id) {
  const store = getDonationStore(event);
  const profiles = (await store.get(STORE_KEY, { type: 'json' })) || [];
  return profiles.find(p => p.id === id) || null;
}

function validateProfileInput({ name, threshold, logoLeft, logoRight, strip, background }) {
  if (!name || !String(name).trim()) throw new Error('יש להזין שם לזיהוי הפרופיל');

  const numThreshold = Number(threshold);
  if (!Number.isFinite(numThreshold) || numThreshold <= 0) throw new Error('סף הסכום חייב להיות מספר חיובי');

  for (const [label, val] of [['לוגו ימין', logoRight], ['לוגו שמאל', logoLeft], ['סטריפ', strip], ['רקע', background]]) {
    if (val && typeof val === 'string' && val.length > MAX_IMAGE_BASE64_LEN) {
      throw new Error(`התמונה "${label}" גדולה מדי - יש להקטין/לדחוס אותה`);
    }
  }

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
    if (input.logoLeft) existing.logoLeft = input.logoLeft;
    if (input.logoRight) existing.logoRight = input.logoRight;
    if (input.strip) existing.strip = input.strip;
    if (input.background) existing.background = input.background;
    existing.updatedAt = now;
    existing.updatedBy = savedBy || existing.updatedBy || null;

    await store.setJSON(STORE_KEY, profiles);
    return existing;
  }

  if (!input.logoLeft || !input.logoRight || !input.strip) {
    throw new Error('ליצירת פרופיל חדש יש להעלות את שני הלוגואים ואת תמונת הסטריפ');
  }

  const record = {
    id: crypto.randomBytes(6).toString('hex'),
    name: String(input.name).trim(),
    threshold: numThreshold,
    logoLeft: input.logoLeft,
    logoRight: input.logoRight,
    strip: input.strip,
    background: input.background || null,
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
