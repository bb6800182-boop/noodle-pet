// 拉麵桌寵 · Google 月曆中繼（Google Apps Script）
// 讓桌寵讀你的 Google 月曆。只讀不寫；沒有正確金鑰的請求一律拒絕。
//
// 1. 用「要讀行事曆的那個 Google 帳號」打開 https://script.new（新增 Apps Script 專案），
//    把這整段貼進 Code.gs（取代原本的內容），按儲存。
//    （建議）專案設定 → 勾「在編輯器中顯示『appsscript.json』資訊清單檔案」，在 appsscript.json 加一行
//      "oauthScopes": ["https://www.googleapis.com/auth/calendar.readonly"],
//    授權畫面就只會要「查看日曆」的權限。
// 2. 右上「部署」→「新增部署作業」→ 類型選「網頁應用程式」：
//      執行身分：我　／　誰可以存取：所有人
//    按「部署」。第一次會要授權讀取日曆（出現「Google 尚未驗證這個應用程式」→「進階」→「前往…」→「允許」）。
// 3. 複製「網頁應用程式」網址（https://script.google.com/macros/s/…/exec），貼回桌寵設定的「Google 月曆」。
//
// 之後換了金鑰或改了程式碼：「部署」→「管理部署作業」→ 編輯 → 版本選「新版本」→ 部署（網址不變）。
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

// 這個行事曆的主人有沒有回覆「不參加」
function status(ev, calId) {
  try { const g = ev.getGuestByEmail(calId); if (g) return g.getGuestStatus(); } catch (x) {}
  try { return ev.getMyStatus(); } catch (x) { return null; }
}

// 活動標成「有空」就不算忙碌
function free(ev) {
  try { return ev.getTransparency() === CalendarApp.EventTransparency.TRANSPARENT; } catch (x) { return false; }
}

function reply(o) {
  return ContentService.createTextOutput(JSON.stringify(o)).setMimeType(ContentService.MimeType.JSON);
}
