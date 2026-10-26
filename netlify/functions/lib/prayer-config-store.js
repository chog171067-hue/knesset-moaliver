// הגדרות זמני התפילות האוטומטיים (טאב "הגדרת תפילות" בדף הניהול). מסמך JSON
// בודד ב-store הניהולי - המבנה והאימות מוגדרים ב-assets/prayer-schedule.js,
// שמשותף לדפי התפילה, לפונקציית המיילים ולתצוגה המקדימה בדף הניהול.
const { getAdminStore } = require('./blobs-store');
const { defaultConfig, normalizeConfig } = require('../../../assets/prayer-schedule.js');

const STORE_KEY = 'prayer-config.json';

async function readPrayerConfig(event) {
  const saved = await getAdminStore(event).get(STORE_KEY, { type: 'json' });
  return saved ? normalizeConfig(saved) : defaultConfig();
}

async function savePrayerConfig(event, input, savedBy) {
  const config = normalizeConfig(input);
  await getAdminStore(event).setJSON(STORE_KEY, Object.assign({}, config, {
    updatedAt: new Date().toISOString(),
    updatedBy: savedBy || null
  }));
  return config;
}

module.exports = { readPrayerConfig, savePrayerConfig };
