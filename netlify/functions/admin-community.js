// דף ניהול - טאב "רשימת הזכאים": העלאת קובץ התורמים מנדרים פלוס, צפייה בכל
// הזכאים (מי פתח חשבון ומי לא), וחסימה / ביטול חסימה.
//
// GET  - הרשימה המאוחדת: כל ת"ז שמופיעה בקובץ, ברשימה הידנית, בחשבון קיים או בחסימות.
// POST { action: 'upload', rows, fileName, dryRun } - החלפת רשימת התורמים בקובץ חדש.
//      dryRun=true מחזיר רק סיכום שינויים (תצוגה מקדימה) בלי לשמור דבר.
//      בשמירה: ת"ז חסומות שמופיעות בקובץ החדש - החסימה שלהן מתבטלת; ת"ז שהיו בקובץ
//      הקודם, הוסרו בחדש ויש להן חשבון - נחסמות (החשבון מושהה, לא נמחק).
// POST { action: 'block' | 'unblock', tz, name }
//
// הקובץ עצמו נקרא בדפדפן של המנהל (admin.html), ולשרת נשלחים רק ת"ז, שם, מייל
// וטלפונים - שאר פרטי התורמים (כתובת, הערות וכו') לא מגיעים לאתר בכלל.
const { requireAdmin, ADMIN_TZ } = require('./lib/admin-auth');
const { listAllUsers } = require('./lib/identity-admin');
const {
  tzKey, cleanPhone, normalizeEmail,
  getRoster, getExtraList, getBlockedList, saveRoster, saveBlockedList
} = require('./lib/community');

const MAX_ROWS = 20000;

function respond(statusCode, headers, data) {
  return { statusCode, headers, body: JSON.stringify(data) };
}

function fullName(r) {
  return [r.firstName, r.lastName].filter(Boolean).join(' ');
}

function accountSummary(u) {
  return {
    id: u.id,
    email: u.email,
    createdAt: u.created_at || null,
    lastSignInAt: u.last_sign_in_at || null,
    confirmed: !!u.confirmed_at
  };
}

// מנקה ומאחד את השורות שהגיעו מהדפדפן. ת"ז כפולה - נשמרת השורה הראשונה.
function sanitizeRows(rawRows) {
  const seen = new Set();
  const rows = [];
  let invalid = 0;
  let duplicates = 0;

  rawRows.forEach(r => {
    const tz = String((r && r.tz) || '').replace(/\D/g, '');
    const key = tzKey(tz);
    if (!key || tz.length > 9) { invalid++; return; }
    if (seen.has(key)) { duplicates++; return; }
    seen.add(key);

    const email = normalizeEmail(r.email);
    rows.push({
      tz,
      firstName: String(r.firstName || '').trim().slice(0, 100),
      lastName: String(r.lastName || '').trim().slice(0, 100),
      email: /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) ? email : '',
      phones: (Array.isArray(r.phones) ? r.phones : []).map(cleanPhone).filter(Boolean).slice(0, 3)
    });
  });

  return { rows, invalid, duplicates };
}

exports.handler = async function (event, context) {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Content-Type': 'application/json'
  };

  if (event.httpMethod === 'OPTIONS') {
    return { statusCode: 200, headers, body: '' };
  }

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return respond(auth.statusCode, headers, { success: false, error: auth.error });
  }

  const adminUser = context.clientContext.user;
  const adminEmail = (adminUser && adminUser.email) || '';
  const identity = context.clientContext && context.clientContext.identity;

  try {
    if (event.httpMethod === 'GET') {
      const [roster, extra, blocked, users] = await Promise.all([
        getRoster(event), getExtraList(event), getBlockedList(event), listAllUsers(identity)
      ]);

      const rows = new Map();
      const rowFor = (tz) => {
        const key = tzKey(tz);
        if (!rows.has(key)) {
          rows.set(key, { tz: String(tz), name: '', email: '', inRoster: false, inExtra: false, blocked: null, account: null });
        }
        return rows.get(key);
      };

      roster.list.forEach(r => {
        const row = rowFor(r.tz);
        row.inRoster = true;
        row.name = fullName(r);
        row.email = r.email || '';
      });
      extra.forEach(r => {
        if (!tzKey(r.tz)) return;
        const row = rowFor(r.tz);
        row.inExtra = true;
        if (!row.name) row.name = r.name || '';
        if (!row.email) row.email = r.email || '';
      });
      users.forEach(u => {
        const tz = u.app_metadata && u.app_metadata.verified_tz;
        if (!tzKey(tz)) return;
        const row = rowFor(tz);
        row.account = accountSummary(u);
        const um = u.user_metadata || {};
        if (!row.name) row.name = um.full_name || um.name || '';
      });
      blocked.forEach(b => {
        if (!tzKey(b.tz)) return;
        const row = rowFor(b.tz);
        row.blocked = { reason: b.reason || 'manual', blockedAt: b.blockedAt || null, blockedBy: b.blockedBy || '' };
        if (!row.name) row.name = b.name || '';
      });

      const list = Array.from(rows.values()).sort((a, b) => String(a.name).localeCompare(String(b.name), 'he'));

      return respond(200, headers, {
        success: true,
        roster: { count: roster.list.length, uploadedAt: roster.uploadedAt, uploadedBy: roster.uploadedBy, fileName: roster.fileName },
        list
      });
    }

    if (event.httpMethod !== 'POST') {
      return respond(405, headers, { success: false, error: 'Method Not Allowed' });
    }

    const body = JSON.parse(event.body || '{}');

    if (body.action === 'upload') {
      if (!Array.isArray(body.rows) || body.rows.length === 0) {
        return respond(400, headers, { success: false, error: 'לא נמצאו שורות בקובץ' });
      }
      if (body.rows.length > MAX_ROWS) {
        return respond(400, headers, { success: false, error: 'הקובץ גדול מדי' });
      }

      const { rows, invalid, duplicates } = sanitizeRows(body.rows);
      if (rows.length === 0) {
        return respond(400, headers, { success: false, error: 'לא נמצאו בקובץ מספרי זהות תקינים' });
      }

      const [roster, extra, blocked, users] = await Promise.all([
        getRoster(event), getExtraList(event), getBlockedList(event), listAllUsers(identity)
      ]);

      const newKeys = new Set(rows.map(r => tzKey(r.tz)));
      const oldKeys = new Set(roster.list.map(r => tzKey(r.tz)));
      const extraKeys = new Set(extra.map(r => tzKey(r.tz)));
      const accountByKey = new Map();
      users.forEach(u => {
        const key = tzKey(u.app_metadata && u.app_metadata.verified_tz);
        if (key) accountByKey.set(key, u);
      });
      const blockedKeys = new Set(blocked.map(b => tzKey(b.tz)));

      const added = rows.filter(r => !oldKeys.has(tzKey(r.tz)));
      const removed = roster.list.filter(r => !newKeys.has(tzKey(r.tz)));
      // יושהו: הוסרו מהקובץ, יש להם חשבון, ואינם ברשימה הידנית (שעדיין מזכה אותם)
      const toSuspend = removed.filter(r => {
        const key = tzKey(r.tz);
        return accountByKey.has(key) && !extraKeys.has(key) && !blockedKeys.has(key) && key !== tzKey(ADMIN_TZ);
      });
      const toUnblock = blocked.filter(b => newKeys.has(tzKey(b.tz)));

      const summary = {
        total: rows.length,
        invalid,
        duplicates,
        withoutEmail: rows.filter(r => !r.email).length,
        added: added.length,
        removed: removed.length,
        toSuspend: toSuspend.map(r => ({ tz: r.tz, name: fullName(r) })),
        toUnblock: toUnblock.map(b => ({ tz: b.tz, name: b.name || '' })),
        isFirstUpload: roster.list.length === 0
      };

      if (body.dryRun) {
        return respond(200, headers, { success: true, dryRun: true, summary });
      }

      const now = new Date().toISOString();
      await saveRoster(event, {
        list: rows,
        uploadedAt: now,
        uploadedBy: adminEmail,
        fileName: String(body.fileName || '').slice(0, 200)
      });

      const nextBlocked = blocked
        .filter(b => !newKeys.has(tzKey(b.tz)))
        .concat(toSuspend.map(r => ({ tz: r.tz, name: fullName(r), reason: 'removed', blockedAt: now, blockedBy: adminEmail })));
      await saveBlockedList(event, nextBlocked);

      return respond(200, headers, { success: true, summary });
    }

    if (body.action === 'block' || body.action === 'unblock') {
      const tz = String(body.tz || '').replace(/\D/g, '');
      const key = tzKey(tz);
      if (!key) {
        return respond(400, headers, { success: false, error: 'מספר תעודת זהות לא תקין' });
      }

      const blocked = await getBlockedList(event);
      const rest = blocked.filter(b => tzKey(b.tz) !== key);

      if (body.action === 'block') {
        if (key === tzKey(ADMIN_TZ)) {
          return respond(400, headers, { success: false, error: 'לא ניתן לחסום את חשבון המנהל' });
        }
        rest.push({
          tz,
          name: String(body.name || '').trim().slice(0, 200),
          reason: 'manual',
          blockedAt: new Date().toISOString(),
          blockedBy: adminEmail
        });
      }

      await saveBlockedList(event, rest);
      return respond(200, headers, { success: true });
    }

    return respond(400, headers, { success: false, error: 'פעולה לא מוכרת' });
  } catch (error) {
    return respond(500, headers, { success: false, error: 'שגיאת שרת: ' + error.message });
  }
};
