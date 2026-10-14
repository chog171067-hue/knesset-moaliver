// אזור אישי: האם החשבון של המשתמש המחובר פעיל או מושהה (ת"ז חסומה בדף הניהול).
// personal.html קורא לזה מיד אחרי ההתחברות כדי להציג הודעת השהיה במקום האזור
// האישי. האכיפה האמיתית נעשית בשרת בכל פעולה (requireActiveMember ב-lib/community.js).
const { requireActiveMember } = require('./lib/community');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const member = await requireActiveMember(event, context);
  if (!member.ok && !member.suspended) {
    return { statusCode: member.statusCode, headers, body: JSON.stringify({ success: false, error: member.error }) };
  }

  return { statusCode: 200, headers, body: JSON.stringify({ success: true, active: member.ok, suspended: !!member.suspended }) };
};
