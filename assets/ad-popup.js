/* פרסומת מרחפת שנפתחת לכל מי שנכנס לאתר.
   כפתור הסגירה (עיגול קטן בצד) מציג ספירה לאחור 3, 2, 1 ורק אז הופך ל-X ומאפשר סגירה.
   נטען אוטומטית מ-header.js בכל הדפים (חוץ מדף הניהול).

   איך מחליפים את הפרסומת (בלי לגעת בקוד):
   מעלים לתיקייה assets/images תמונה בשם ad (למשל ad.png או ad.jpg).
   להחלפה - מעלים תמונה חדשה באותו שם במקום הישנה.
   להפסקת הפרסומת - מוחקים את התמונה (כשאין תמונה, שום דבר לא קופץ). */

(function () {
    var AD_CONFIG = {
        enabled: true,
        // שמות הקבצים שנבדקים לפי הסדר - הראשון שקיים הוא שיוצג
        images: ['assets/images/ad.png', 'assets/images/ad.jpg', 'assets/images/ad.jpeg', 'assets/images/ad.webp'],
        alt: 'פרסומת',
        link: '',             // קישור בלחיצה על הפרסומת (ריק = ללא קישור)
        countdownSeconds: 3,  // כמה שניות עד שאפשר לסגור
        oncePerVisit: true    // true = מוצג פעם אחת בכל ביקור (ולא שוב בכל מעבר בין דפים)
    };

    if (!AD_CONFIG.enabled) return;

    var SEEN_KEY = 'mohliver_ad_seen';
    if (AD_CONFIG.oncePerVisit) {
        try {
            if (sessionStorage.getItem(SEEN_KEY)) return;
        } catch (e) { /* sessionStorage חסום - פשוט מציגים */ }
    }

    var css =
        '.ad-popup-overlay{position:fixed;inset:0;z-index:2000;background:rgba(10,25,47,0.6);' +
        'display:flex;align-items:center;justify-content:center;padding:16px;box-sizing:border-box;' +
        'opacity:0;transition:opacity .3s ease;}' +
        '.ad-popup-overlay.show{opacity:1;}' +
        '.ad-popup-box{position:relative;max-width:min(520px,100%);max-height:100%;' +
        'transform:scale(.92);transition:transform .3s ease;}' +
        '.ad-popup-overlay.show .ad-popup-box{transform:scale(1);}' +
        '.ad-popup-box img{display:block;max-width:100%;max-height:calc(100vh - 32px);' +
        'border-radius:14px;box-shadow:0 12px 40px rgba(0,0,0,.45);background:#fff;}' +
        '.ad-popup-close{position:absolute;top:-14px;right:-14px;width:40px;height:40px;border-radius:50%;' +
        'border:3px solid #fff;background:#1a365d;color:#fff;font:bold 18px/1 "Segoe UI",Tahoma,sans-serif;' +
        'display:flex;align-items:center;justify-content:center;padding:0;cursor:not-allowed;' +
        'box-shadow:0 3px 10px rgba(0,0,0,.35);transition:background .2s,transform .1s;}' +
        '.ad-popup-close.ready{cursor:pointer;background:#c53030;font-size:24px;}' +
        '.ad-popup-close.ready:hover{transform:scale(1.08);}' +
        '@media (max-width:600px){.ad-popup-close{top:-10px;right:-6px;}}';

    function open(src) {
        var style = document.createElement('style');
        style.textContent = css;
        document.head.appendChild(style);

        var overlay = document.createElement('div');
        overlay.className = 'ad-popup-overlay';
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-modal', 'true');
        overlay.setAttribute('aria-label', 'פרסומת');

        var box = document.createElement('div');
        box.className = 'ad-popup-box';

        var img = document.createElement('img');
        img.src = src;
        img.alt = AD_CONFIG.alt;

        if (AD_CONFIG.link) {
            var a = document.createElement('a');
            a.href = AD_CONFIG.link;
            a.target = '_blank';
            a.rel = 'noopener';
            a.appendChild(img);
            box.appendChild(a);
        } else {
            box.appendChild(img);
        }

        var closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.className = 'ad-popup-close';
        closeBtn.disabled = true;
        box.appendChild(closeBtn);

        overlay.appendChild(box);
        document.body.appendChild(overlay);

        var prevOverflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';
        requestAnimationFrame(function () { overlay.classList.add('show'); });

        try { sessionStorage.setItem(SEEN_KEY, '1'); } catch (e) { /* לא קריטי */ }

        var ready = false;
        var remaining = AD_CONFIG.countdownSeconds;

        function tick() {
            if (remaining > 0) {
                closeBtn.textContent = String(remaining);
                closeBtn.setAttribute('aria-label', 'ניתן לסגור בעוד ' + remaining + ' שניות');
                remaining--;
                setTimeout(tick, 1000);
            } else {
                ready = true;
                closeBtn.disabled = false;
                closeBtn.classList.add('ready');
                closeBtn.textContent = '✕';
                closeBtn.setAttribute('aria-label', 'סגירת הפרסומת');
            }
        }
        tick();

        function close() {
            if (!ready) return;
            overlay.classList.remove('show');
            document.body.style.overflow = prevOverflow;
            document.removeEventListener('keydown', onKey);
            setTimeout(function () {
                overlay.remove();
                style.remove();
            }, 300);
        }

        function onKey(e) {
            if (e.key === 'Escape') close();
        }

        closeBtn.addEventListener('click', close);
        // לחיצה על הרקע הכהה סוגרת גם היא - אבל רק אחרי שהספירה הסתיימה
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) close();
        });
        document.addEventListener('keydown', onKey);
    }

    // טוענים את התמונה מראש: מציגים רק אם נמצאה תמונה, ורק כשהיא כבר מוכנה (בלי חלון ריק)
    function findImage(i) {
        if (i >= AD_CONFIG.images.length) return; // אין תמונת פרסומת - לא מציגים כלום
        var probe = new Image();
        probe.onload = function () { whenReady(function () { open(AD_CONFIG.images[i]); }); };
        probe.onerror = function () { findImage(i + 1); };
        probe.src = AD_CONFIG.images[i];
    }

    function whenReady(fn) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', fn);
        } else {
            fn();
        }
    }

    findImage(0);
})();
