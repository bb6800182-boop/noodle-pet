// Lamian desktop pet · Google Calendar relay (Google Apps Script)
// Lets the pet read your Google Calendar. Read-only; requests without the right key are refused.
//
// 1. With the Google account whose calendar you want to read, open https://script.new (a new Apps Script project),
//    paste this whole file into Code.gs (replacing what is there) and save.
//    (Recommended) Project Settings → tick "Show appsscript.json manifest file in editor", then add this line to appsscript.json
//      "oauthScopes": ["https://www.googleapis.com/auth/calendar.readonly"],
//    so the consent screen only asks to view your calendars.
// 2. Top right: Deploy → New deployment → type "Web app":
//      Execute as: Me   /   Who has access: Anyone
//    Press Deploy. The first time it asks for calendar access ("Google hasn't verified this app" → Advanced → Go to … → Allow).
// 3. Copy the web app URL (https://script.google.com/macros/s/…/exec) and paste it into the pet's settings under Google Calendar.
//
// After changing the key or the code: Deploy → Manage deployments → Edit → Version: New version → Deploy (the URL stays the same).
const TOKEN = '{{TOKEN}}';
const VERSION = 1;

function doPost(e) {
  let req = null;
  try { req = JSON.parse(e.postData.contents); } catch (err) { return reply({ ok: false, error: 'bad-request' }); }
  if (!req || typeof req.token !== 'string' || req.token !== TOKEN) return reply({ ok: false, error: 'unauthorized' });
  try {
    const now = new Date();
    const from = new Date(now.getTime() - 6 * 3600e3);
    const to = new Date(now.getTime() + 36 * 3600e3);
    const primary = CalendarApp.getDefaultCalendar().getId();
    const all = CalendarApp.getAllCalendars().filter(function (c) { return !c.isHidden(); });
    const want = Array.isArray(req.calendars) && req.calendars.length ? req.calendars : [primary];
    const events = [];
    all.filter(function (c) { return want.indexOf(c.getId()) >= 0; }).forEach(function (c) {
      const calId = c.getId();
      c.getEvents(from, to).forEach(function (ev) {
        const declined = status(ev, calId) === CalendarApp.GuestStatus.NO;
        events.push({
          cal: calId,
          id: ev.getId(),
          title: ev.getTitle(),
          location: ev.getLocation(),
          start: ev.getStartTime().getTime(),
          end: ev.getEndTime().getTime(),
          allDay: ev.isAllDayEvent(),
          declined: declined,
          busy: !declined && !free(ev),
        });
      });
    });
    const calendars = all.map(function (c) { return { id: c.getId(), name: c.getName(), primary: c.getId() === primary }; });
    return reply({ ok: true, v: VERSION, calendars: calendars, events: events });
  } catch (err) {
    return reply({ ok: false, error: String((err && err.message) || err) });
  }
}

// Whether the owner of this calendar declined the event
function status(ev, calId) {
  try { const g = ev.getGuestByEmail(calId); if (g) return g.getGuestStatus(); } catch (x) {}
  try { return ev.getMyStatus(); } catch (x) { return null; }
}

// Events marked Free do not count as busy
function free(ev) {
  try { return ev.getTransparency() === CalendarApp.EventTransparency.TRANSPARENT; } catch (x) { return false; }
}

function reply(o) {
  return ContentService.createTextOutput(JSON.stringify(o)).setMimeType(ContentService.MimeType.JSON);
}
