// render-sprites.mjs: render Noodle's SVG figures (SMIL animation) frame by frame into transparent PNG sprite sheets
// Usage: node render-sprites.mjs <browser exe> <figures folder> <output folder>
//   figures folder layout: <character>/<pose>.svg (as in assets/figures)
// No npm packages needed: talks to Edge/Chrome over the DevTools Protocol using Node's built-in WebSocket.
// Each frame is rendered alone in the same 120x120 viewport, so identical poses produce identical bytes;
// consecutive identical frames are merged into one frame with a longer duration (idle is mostly still, so it barely uses CPU).
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [, , browserExe, figDir, outDir] = process.argv;
if (!browserExe || !figDir || !outDir) {
  console.error('usage: node render-sprites.mjs <browser exe> <figures dir> <out dir>');
  process.exit(1);
}

const CELL = 120, PAD = 12, SCALE = 2, COLS = 8;           // 120 CSS px per cell (12px margin), rendered at 2x
const CHARACTERS = ['rigatoni', 'spaghetti', 'penne', 'lasagna', 'sprout'];
const POSES = {                                            // name: fps (loop length = the longest dur in the SVG)
  idle: 12, walk: 20, wave: 15, hang: 15,
  'chin-tuck': 15, doorway: 15, scap: 15, 't-ext': 15, 'hip-flexor': 15, 'neck-side': 15, 'calf-raise': 15,
};
// Sprout's idle has a 3.2s sway and a 4.2s blink: 12.8s loops the sway exactly, and the blink being off by 0.2s is invisible
const LOOP_OVERRIDE = { 'sprout/idle': 12.8 };
const loopOf = (svg) => Math.max(1, ...[...svg.matchAll(/dur="([\d.]+)s"/g)].map((m) => +m[1]));
// Peek is split into two layers (same as Noodle's card): the head goes behind the bubble, the hands grip its top edge
const STILLS = {
  'peek-back': ['peek', '[data-part="hands"]{display:none}'],
  'peek-front': ['peek', '[data-part="lift"],[data-part="paper"]{display:none}' +
    '[data-part="hands"] circle{stroke:#FFFFFF;stroke-width:2.6;paint-order:stroke}'],
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const profile = mkdtempSync(join(tmpdir(), 'noodle-cdp-'));
const browser = spawn(browserExe, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--hide-scrollbars', `--user-data-dir=${profile}`, '--remote-debugging-port=0', 'about:blank'], { stdio: 'ignore' });

let port, wsPath;
for (let i = 0; i < 200 && !wsPath; i++) {
  await sleep(100);
  const f = join(profile, 'DevToolsActivePort');
  if (existsSync(f)) [port, wsPath] = readFileSync(f, 'utf8').trim().split(/\r?\n/);
}
if (!wsPath) { browser.kill(); throw new Error('The browser did not open a DevTools connection'); }

const ws = new WebSocket(`ws://127.0.0.1:${port}${wsPath}`);
await new Promise((res, rej) => { ws.onopen = res; ws.onerror = () => rej(new Error('DevTools connection failed')); });
let nextId = 1;
const pending = new Map();
ws.onmessage = (ev) => {
  const m = JSON.parse(ev.data);
  if (!m.id || !pending.has(m.id)) return;
  const { res, rej } = pending.get(m.id);
  pending.delete(m.id);
  if (m.error) rej(new Error(m.error.message)); else res(m.result);
};
// Every call has a timeout: if a page hangs, close it and open a new one to retry, instead of waiting forever
const send = (method, params = {}, sessionId, ms = 20000) => new Promise((res, rej) => {
  const id = nextId++;
  const timer = setTimeout(() => { pending.delete(id); rej(new Error(`${method} timed out`)); }, ms);
  pending.set(id, { res: (v) => { clearTimeout(timer); res(v); }, rej: (e) => { clearTimeout(timer); rej(e); } });
  ws.send(JSON.stringify({ id, method, params, sessionId }));
});
let targetId, sessionId;
async function openPage() {
  if (targetId) await send('Target.closeTarget', { targetId }).catch(() => {});
  ({ targetId } = await send('Target.createTarget', { url: 'about:blank' }));
  ({ sessionId } = await send('Target.attachToTarget', { targetId, flatten: true }));
  await page('Emulation.setDefaultBackgroundColorOverride', { color: { r: 0, g: 0, b: 0, a: 0 } });
}
const page = (method, params) => send(method, params, sessionId);
const run = async (expression) => {
  const r = await page('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result.value;
};
await openPage();
const only = process.argv[5] ? process.argv[5].split(',') : null;   // optional: render only these characters

async function load(svg, cell, pad, scale, css = '') {
  await page('Emulation.setDeviceMetricsOverride', { width: cell, height: cell, deviceScaleFactor: scale, mobile: false });
  const style = `html,body{margin:0;background:transparent;overflow:hidden}` +
    `.c{position:relative;width:${cell}px;height:${cell}px}.c>svg{position:absolute;left:${pad}px;top:${pad}px;width:96px;height:96px}${css}`;
  const html = `<!doctype html><style>${style}</style><div class="c">${svg}</div>`;
  await run(`document.open();document.write(${JSON.stringify(html)});document.close();`);
}

// Pause at time t and capture one frame (PNG base64)
async function frameAt(t) {
  await run(`(async()=>{const s=document.querySelector('.c>svg');s.pauseAnimations();s.setCurrentTime(${t});` +
    `await Promise.race([new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))),new Promise(r=>setTimeout(r,150))]);})()`);
  const { data } = await page('Page.captureScreenshot', { format: 'png', fromSurface: true });
  return data;
}

// Assemble the deduplicated frames into one sheet with a canvas in the page (lossless PNG)
async function sheet(frames) {
  const S = CELL * SCALE;
  return run(`(async()=>{const src=${JSON.stringify(frames)};const S=${S},cols=${COLS};` +
    `const c=document.createElement('canvas');c.width=Math.min(src.length,cols)*S;c.height=Math.ceil(src.length/cols)*S;` +
    `const g=c.getContext('2d');g.imageSmoothingEnabled=false;` +
    `for(let i=0;i<src.length;i++){const im=new Image();im.src='data:image/png;base64,'+src[i];await im.decode();` +
    `g.drawImage(im,(i%cols)*S,Math.floor(i/cols)*S);}return c.toDataURL('image/png').split(',')[1];})()`);
}

async function renderPose(svg, dur, fps, css) {
  await load(svg, CELL, PAD, SCALE, css);
  const n = Math.max(1, Math.round(dur * fps)), ms = 1000 / fps;
  const unique = [], index = new Map(), seq = [];
  for (let i = 0; i < n; i++) {
    const data = await frameAt(+(i * dur / n).toFixed(4));
    if (!index.has(data)) { index.set(data, unique.length); unique.push(data); }
    const f = index.get(data), last = seq[seq.length - 1];
    if (last && last[0] === f) last[1] += ms; else seq.push([f, ms]);
  }
  for (const s of seq) s[1] = Math.round(s[1]);
  return { png: Buffer.from(await sheet(unique), 'base64'), frames: unique.length, seq };
}

// PNG-compressed .ico (supported on Windows Vista and later)
function ico(entries) {
  const head = Buffer.alloc(6 + 16 * entries.length);
  head.writeUInt16LE(1, 2);
  head.writeUInt16LE(entries.length, 4);
  let offset = head.length;
  entries.forEach(({ size, buf }, i) => {
    const e = 6 + 16 * i;
    head.writeUInt8(size >= 256 ? 0 : size, e);
    head.writeUInt8(size >= 256 ? 0 : size, e + 1);
    head.writeUInt16LE(1, e + 4);
    head.writeUInt16LE(32, e + 6);
    head.writeUInt32LE(buf.length, e + 8);
    head.writeUInt32LE(offset, e + 12);
    offset += buf.length;
  });
  return Buffer.concat([head, ...entries.map((x) => x.buf)]);
}

async function icon(svg, px) {
  await load(svg, 96, 0, px / 96);
  return Buffer.from(await frameAt(0.4), 'base64');
}

try {
  const t0 = Date.now();
  for (const ch of CHARACTERS.filter((c) => !only || only.includes(c))) {
    const dir = join(outDir, 'sprites', ch);
    mkdirSync(dir, { recursive: true });
    const svgOf = (pose) => readFileSync(join(figDir, ch, `${pose}.svg`), 'utf8');
    const manifest = { cell: CELL, scale: SCALE, cols: COLS, poses: {} };
    const jobs = [...Object.entries(POSES).map(([p, fps]) => [p, p, LOOP_OVERRIDE[`${ch}/${p}`] || loopOf(svgOf(p)), fps, '']),
      ...Object.entries(STILLS).map(([p, [src, css]]) => [p, src, 1, 1, css])];
    for (const [name, src, dur, fps, css] of jobs) {
      let r;
      for (let attempt = 1; !r; attempt++) {
        try { r = await renderPose(svgOf(src), dur, fps, css); }
        catch (e) {
          if (attempt >= 3) throw e;
          console.log(`  ${ch}/${name} retry ${attempt}: ${e.message}`);
          await openPage();
        }
      }
      writeFileSync(join(dir, `${name}.png`), r.png);
      manifest.poses[name] = { frames: r.frames, seq: r.seq };
    }
    writeFileSync(join(dir, 'sprites.json'), JSON.stringify(manifest));
    writeFileSync(join(outDir, `tray-${ch}.png`), await icon(svgOf('wave'), 64));
    const counts = Object.entries(manifest.poses).map(([p, v]) => `${p} ${v.frames}`).join(', ');
    console.log(`${ch}: ${counts}`);
  }
  const wave = readFileSync(join(figDir, 'rigatoni', 'wave.svg'), 'utf8');
  const icons = [];
  for (const size of [256, 64, 48, 32, 16]) icons.push({ size, buf: await icon(wave, size) });
  writeFileSync(join(outDir, 'icon.ico'), ico(icons));
  console.log(`done in ${Math.round((Date.now() - t0) / 1000)}s`);
} finally {
  ws.close();
  browser.kill();
  await sleep(300);
  try { rmSync(profile, { recursive: true, force: true }); } catch {}
}
