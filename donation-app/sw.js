// Service Worker לעבודה אופליין: שומר במטמון את "מעטפת" האפליקציה (HTML/JS,
// מניפסטים, אייקונים) של שני דפי ערב ההתרמה, כדי שאפשר לפתוח אותם גם בלי
// אינטרנט אחרי שנפתחו לפחות פעם אחת עם חיבור. נתוני התרומות עצמם וההגדרות
// (לוגואים וכו') לא עוברים דרך כאן בכלל - אלה ב-localStorage בלבד
// (ראו control.html/display.html ו-config-loader.js).
const CACHE_NAME = 'donation-app-shell-v4';
const APP_SHELL = [
  'control.html',
  'display.html',
  'config-loader.js',
  'manifest-control.json',
  'manifest-display.json',
  'icons/icon-control-192.png',
  'icons/icon-control-512.png',
  'icons/icon-display-192.png',
  'icons/icon-display-512.png'
];

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => cache.addAll(APP_SHELL))
      .then(() => console.log('[donation-app] sw.js: כל קבצי המעטפת נשמרו במטמון בהצלחה'))
      .catch(err => {
        // addAll הוא "הכל-או-כלום" - אם קובץ אחד ב-APP_SHELL מחזיר שגיאה (404 וכו'),
        // ההתקנה כולה נכשלת בלי שום קובץ נשמר, וזו הסיבה השכיחה ביותר לכך שהעמדה
        // "עובדת" כשיש רשת אבל נופלת לגמרי כשאין.
        console.error('[donation-app] sw.js: התקנת המטמון נכשלה -', err);
        throw err;
      })
  );
  self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k))))
  );
  self.clients.claim();
});

// Network-first, ורק עבור קבצי מעטפת האפליקציה עצמה. בקשות אחרות (כמו הפונקציה
// get-donation-config, שחייבת תמיד לנסות רשת קודם - הלוגיקה הזו כבר קיימת
// ב-config-loader.js עצמו) לא נוגעים בהן כאן בכלל.
//
// בכוונה *לא* cache-first: אם יש רשת - תמיד מביאים ומציגים את הגרסה העדכנית
// ביותר, ומרעננים איתה את המטמון (כדי שהעותק השמור להמשך אופליין גם יתעדכן).
// רק כשאין רשת בכלל (כשל ברמת fetch עצמו) נופלים להעתק השמור. כך אין יותר
// תלות בזיכרון לעדכן ידנית מספר גרסה ב-sw.js בכל שינוי, וגם אין צורך
// שהמשתמש "ינקה" ידנית Service Worker תקוע - זה פשוט תמיד מציג את מה שבאמת
// קיים באתר, כל עוד יש חיבור.
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (url.origin !== location.origin || !APP_SHELL.some(f => url.pathname.endsWith('/' + f))) return;

  event.respondWith(
    fetch(event.request).then(networkResponse => {
      const responseCopy = networkResponse.clone();
      caches.open(CACHE_NAME).then(cache => cache.put(event.request, responseCopy));
      return networkResponse;
    }).catch(() => caches.match(event.request, { ignoreSearch: true }))
  );
});
