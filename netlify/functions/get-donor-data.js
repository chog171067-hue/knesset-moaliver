exports.handler = async function(event, context) {
    if (event.httpMethod !== "POST") {
        return { statusCode: 405, body: "Method Not Allowed" };
    }

    try {
        const body = JSON.parse(event.body);
        const MOSAD_ID = "7012851";
        const API_PASSWORD = process.env.NEDARIM_API_PASSWORD; // מוגדר בנטליפיי

        if (!API_PASSWORD) {
            return { statusCode: 500, body: JSON.stringify({ error: "שגיאת שרת: מפתח ה-API של המוסד אינו מוגדר" }) };
        }

        // מצב ב': המשתמש ביקש היסטוריית חיובים מפורטת עבור הוראת קבע ספציפית (KevaId)
        if (body.kevaId) {
            const historyResponse = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaId&MosadId=${MOSAD_ID}&ApiPassword=${API_PASSWORD}&KevaId=${body.kevaId}`);
            
            if (!historyResponse.ok) {
                throw new Error("כשל בתקשורת מול שרתי נדרים פלוס בשליפת היסטוריה");
            }

            const kevaDetails = await historyResponse.json();

            // מחזירים רק את הנתונים הנחוצים לצורך תצוגת ההיסטוריה
            return {
                statusCode: 200,
                headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" },
                body: JSON.stringify({
                    success: true,
                    successCount: kevaDetails.KevaSuccess, // מספר חיובים מוצלחים
                    totalAmount: kevaDetails.TotalHistoryAmount, // סך הכל שנגבה
                    history: (kevaDetails.HistoryData || []).map(h => ({
                        status: h.ID, // 1 = הצלחה, 2 = סירוב, 3 = בוטל
                        amount: h.Amount,
                        date: h.Date,
                        transactionId: h.TransactionId
                    }))
                })
            };
        }

        // מצב א': כניסה ראשונית (שליפת רשימת ההוראות קבע לפי טלפון ותעודת זהות)
        const { phone, tz } = body;
        if (!phone || !tz) {
            return { statusCode: 400, body: JSON.stringify({ error: "מספר טלפון ותעודת זהות הם שדות חובה" }) };
        }

        const cleanPhone = phone.trim().replace(/[- ]/g, '');
        const cleanTz = tz.trim();

        const response = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_ID}&ApiPassword=${API_PASSWORD}`);
        
        if (!response.ok) {
            throw new Error("כשל בתקשורת מול שרתי נדרים פלוס");
        }

        const allKevot = await response.json();

        const donorKevot = allKevot.filter(keva => {
            const kevaPhone = (keva.Phone || "").trim().replace(/[- ]/g, '');
            const kevaTz = (keva.Zeout || "").trim();
            return kevaPhone === cleanPhone && kevaTz === cleanTz;
        });

        return {
            statusCode: 200,
            headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" },
            body: JSON.stringify({
                success: true,
                count: donorKevot.length,
                data: donorKevot.map(k => ({
                    kevaId: k.KevaId, // שמירת המזהה לצורך שליפת ההיסטוריה בהמשך
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
