// "שליחת הודעה לגבאי" מעמדת הקיוסק - אותו מנגנון של "הודעות לגבאי" באזור האישי באתר:
// אותו store (admin-data), אותו אינדקס שיחות, ואותו מבנה שרשור. כך השיחות מהעמדה
// מופיעות בדף הניהול ← "הודעות לגבאי" יחד עם השאר, והגבאי עונה משם כרגיל.
// מזהה השיחה: "kiosk-<מזהה חשבון הקיוסק>" (כדי שלא יתנגש במזהי משתמשי האתר).
const { getAdminStore } = require('./blobs-store');

const GABAI_EMAIL = 'b0799186827@gmail.com';
const THREADS_INDEX_KEY = 'gabai-threads-index.json';

function kioskThreadUserId(kioskUserId) {
  return `kiosk-${kioskUserId}`;
}

function threadKey(userId) {
  return `gabai-thread-${userId}.json`;
}

function escapeHtml(text) {
  return String(text || '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

async function notifyGabai(userName, userTz, text) {
  try {
    const apiKey = process.env.SENDGRID_API_KEY;
    if (!apiKey) return;
    await fetch('https://api.resend.com/emails', {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${apiKey}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        from: 'בית הכנסת מוהליבר <gabay@moaliver.org.il>',
        to: GABAI_EMAIL,
        subject: `הודעה חדשה מ${userName} בעמדת המחשב בספרייה`,
        html: `
          <div dir="rtl" style="font-family:'Segoe UI',sans-serif; padding:15px; text-align:right;">
            <p><b>שם:</b> ${escapeHtml(userName)}</p>
            <p><b>ת.ז:</b> ${escapeHtml(userTz) || '-'}</p>
            <p><b>נשלח מ:</b> עמדת המחשב בספרייה</p>
            <p><b>הודעה:</b></p>
            <p style="background:#f7fafc; padding:12px; border-radius:8px;">${escapeHtml(text)}</p>
            <p><a href="https://moaliver.org.il/admin.html">מענה מתוך דף הניהול</a></p>
          </div>
        `
      })
    });
  } catch (e) {
    console.warn('[kiosk-gabai] כשל בשליחת התראה לגבאי:', e.message);
  }
}

async function getKioskThread(event, kioskUserId, { markRead } = {}) {
  const store = getAdminStore(event);
  const userId = kioskThreadUserId(kioskUserId);
  const messages = (await store.get(threadKey(userId), { type: 'json' })) || [];
  const index = (await store.get(THREADS_INDEX_KEY, { type: 'json' })) || [];
  const entry = index.find(t => t.userId === userId);
  let hasUnread = !!(entry && entry.unreadByUser);
  if (markRead && hasUnread) {
    entry.unreadByUser = false;
    await store.setJSON(THREADS_INDEX_KEY, index);
    hasUnread = false;
  }
  return { messages, hasUnread };
}

async function sendKioskMessage(event, kioskUser, text) {
  const cleanText = String(text || '').trim();
  if (!cleanText) throw new Error('ההודעה ריקה');
  if (cleanText.length > 2000) throw new Error('ההודעה ארוכה מדי (מקסימום 2000 תווים)');

  const store = getAdminStore(event);
  const userId = kioskThreadUserId(kioskUser.id);
  const now = new Date().toISOString();
  const userName = `${kioskUser.displayName || kioskUser.username} (עמדת מחשב)`;

  const messages = (await store.get(threadKey(userId), { type: 'json' })) || [];
  messages.push({ from: 'user', text: cleanText, timestamp: now });
  await store.setJSON(threadKey(userId), messages);

  const index = (await store.get(THREADS_INDEX_KEY, { type: 'json' })) || [];
  const summary = {
    userId,
    userName,
    userEmail: '',
    userTz: kioskUser.username,
    source: 'kiosk',
    lastMessageAt: now,
    lastMessageFrom: 'user',
    lastMessagePreview: cleanText.slice(0, 100),
    unreadByAdmin: true,
    unreadByUser: false
  };
  const existing = index.find(t => t.userId === userId);
  if (existing) Object.assign(existing, summary);
  else index.push(summary);
  await store.setJSON(THREADS_INDEX_KEY, index);

  await notifyGabai(userName, kioskUser.username, cleanText);
  return messages;
}

module.exports = { getKioskThread, sendKioskMessage };
