
exports.handler = async function(event, context) {
    // מאפשרים רק פניות מסוג POST
    if (event.httpMethod !== "POST") {
        return { statusCode: 405, body: "Method Not Allowed" };
    }

    try {
        const { phone, tz } = JSON.parse(event.body);

        if (!phone || !tz) {
            return { statusCode: 400, body: JSON.stringify({ error: "מספר טלפון ותעודת זהות הם שדות חובה" }) };
        }

        // ניקוי תווים מיותרים מהטלפון ותעודת הזהות
        const cleanPhone = phone.trim().replace(/[- ]/g, '');
        const cleanTz = tz.trim();

        const MOSAD_ID = "7012851";
        const API_PASSWORD = process.env.NEDARIM_API_PASSWORD; // מוגדר בנטליפיי

        if (!API_PASSWORD) {
            return { statusCode: 500, body: JSON.stringify({ error: "שגיאת שרת: מפתח ה-API של המוסד אינו מוגדר" }) };
        }

        // פנייה ל-API הרשמי של נדרים פלוס למשיכת כל הוראות הקבע של האשראי במוסד
        const response = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_ID}&ApiPassword=${API_PASSWORD}`);
        
        if (!response.ok) {
            throw new Error("כשל בתקשורת מול שרתי נדרים פלוס");
        }

        const allKevot = await response.json();

        // סינון רשימת הוראות הקבע: שולפים רק את ההוראות שמתאימות לטלפון ולתעודת הזהות של הגולש
        const donorKevot = allKevot.filter(keva => {
            const kevaPhone = (keva.Phone || "").trim().replace(/[- ]/g, '');
            const kevaTz = (keva.Zeout || "").trim();
            return kevaPhone === cleanPhone && kevaTz === cleanTz;
        });

        // החזרת הנתונים המסוננים והבטוחים בלבד לדפדפן (ללא הסיסמה הסודית כמובן!)
        return {
            statusCode: 200,
            headers: {
                "Content-Type": "application/json",
                "Access-Control-Allow-Origin": "*"
            },
            body: JSON.stringify({
                success: true,
                count: donorKevot.length,
                data: donorKevot.map(k => ({
                    name: k.ClientName,
                    amount: k.Amount,
                    nextDate: k.NextDate,
                    lastNum: k.LastNum,
                    category: k.Groupe,
                    comments: k.Comments,
                    enabled: k.Enabled === "1"
                }))
            })
        };

    } catch (error) {
        return {
            statusCode: 500,
            body: JSON.stringify({ error: "שגיאה בעיבוד הנתונים: " + error.message })
        };
    }
};
