// Service Worker לעבודה אופליין: שומר במטמון את "מעטפת" האפליקציה (HTML/JS,
// מניפסטים, אייקונים) של שני דפי ערב ההתרמה, כדי שאפשר לפתוח אותם גם בלי
// אינטרנט אחרי שנפתחו לפחות פעם אחת עם חיבור. נתוני התרומות עצמם וההגדרות
// (לוגואים וכו') לא עוברים דרך כאן בכלל - אלה ב-localStorage בלבד
// (ראו control.html/display.html ו-config-loader.js).
const CACHE_NAME = 'donation-app-shell-v1';
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

// Cache-first, ורק עבור קבצי מעטפת האפליקציה עצמה. בקשות אחרות (כמו הפונקציה
// get-donation-config, שחייבת תמיד לנסות רשת קודם - הלוגיקה הזו כבר קיימת
// ב-config-loader.js עצמו) לא נוגעים בהן כאן בכלל.
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (url.origin !== location.origin || !APP_SHELL.some(f => url.pathname.endsWith('/' + f))) return;

  // ignoreSearch: הקבצים נשמרו במטמון לפי הכתובת "הנקייה" (בלי ?event=...),
  // אבל הקישור ששולחים לנמען כן מכיל את זה - בלי ignoreSearch, פתיחה חוזרת
  // של אותו קישור המקורי (לא רק רענון של דף שכבר פתוח) לא הייתה מוצאת התאמה
  // במטמון ונופלת לרשת, שנכשלת כשאין חיבור.
  event.respondWith(
    caches.match(event.request, { ignoreSearch: true }).then(cached => cached || fetch(event.request))
  );
});
