// make-drink.mjs：從各角色的 idle.svg 產生「喝水」動作 drink.svg（SMIL 動畫，畫風沿用 Noodle 的貼紙描邊）
// 用法：node tools/make-drink.mjs assets/figures
// 杯子和拿杯子的手是新畫的；其餘（身體、臉、腿、左手）沿用角色的 idle 造型。
// 一輪 3.6 秒：杯子拿在胸前 → 舉到嘴邊傾斜 → 喝（閉眼）→ 放下 → 「哈～」小彈一下＋旁邊冒出爽快線。
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const dir = process.argv[2] || 'assets/figures';
const CHARACTERS = ['rigatoni', 'spaghetti', 'penne', 'lasagna', 'sprout'];
const DUR = '3.6s';
const ease = (n) => Array(n).fill('0.45 0 0.55 1').join(';');
const anim = (attr, values, keyTimes) =>
  `<animate attributeName="${attr}" values="${values}" keyTimes="${keyTimes}" calcMode="spline" keySplines="${ease(keyTimes.split(';').length - 1)}" dur="${DUR}" repeatCount="indefinite"/>`;
const animT = (type, values, keyTimes, extra = '') =>
  `<animateTransform attributeName="transform" type="${type}" values="${values}" keyTimes="${keyTimes}" calcMode="spline" keySplines="${ease(keyTimes.split(';').length - 1)}" dur="${DUR}" repeatCount="indefinite"${extra}/>`;

const MOVE = '0;0.16;0.32;0.5;0.66;0.8;1';   // 拿著 → 舉起 → 喝 → 多喝一口 → 放下
const EYES = '0;0.28;0.32;0.64;0.68;1';      // 杯子到嘴邊就閉眼
const BOB = '0;0.8;0.86;0.93;1';             // 放下後小彈一下
const SPARK = '0;0.8;0.85;0.95;1';

function split(svg) {
  const open = svg.indexOf('<g data-part="paper"');
  if (open < 0) {   // sprout：沒有貼紙濾鏡，整個就是一份
    const start = svg.indexOf('>') + 1, end = svg.lastIndexOf('</svg>');
    return { head: svg.slice(0, start), paperOpen: null, fig: svg.slice(start, end) };
  }
  const re = /<(\/?)g\b[^>]*?(\/?)>/g;
  re.lastIndex = open;
  let depth = 0, m, end = -1;
  while ((m = re.exec(svg))) {
    if (m[1]) depth--; else if (!m[2]) depth++;
    if (depth === 0) { end = m.index; break; }
  }
  const openEnd = svg.indexOf('>', open) + 1;
  const fig = svg.slice(openEnd, end);
  const tail = svg.slice(end + 4).replace('</svg>', '');
  if (tail.trim() !== fig.trim()) throw new Error('濾鏡內外的兩份圖不一樣，不能自動轉換');
  return { head: svg.slice(0, open), paperOpen: svg.slice(open, openEnd), fig };
}

function must(text, re, what) {
  if (!re.test(text)) throw new Error('找不到 ' + what);
  re.lastIndex = 0;
}

function drink(ch, idle) {
  const { head, paperOpen, fig: src } = split(idle);
  // idle 原本的動畫（小芽頭上葉子的擺動）也改成 3.6 秒一輪，整段才會無縫循環
  let fig = src.replace(/dur="[\d.]+s"/g, `dur="${DUR}"`);
  const ink = /stroke="currentColor"/.test(head) ? 'currentColor' : '#2B2420';

  // 1. 拿掉右手（idle 垂著的那隻），等一下換成拿杯子的手
  const arm = /\s*<path d="M59 41 Q67 47 65 57"\/>/, hand = /\s*<circle cx="65" cy="58\.5" r="2\.8"[^>]*\/>/;
  must(fig, arm, ch + ' 的右手');
  must(fig, hand, ch + ' 的右手掌');
  fig = fig.replace(arm, '').replace(hand, '');

  // 2. 臉：眼睛換成「喝的時候閉起來」，加上閉眼的 ^ ^，喝的時候臉微微往上
  const face = /<g data-part="face"(?: transform="translate\(0 (-?[\d.]+)\)")?>/;
  must(fig, face, ch + ' 的臉');
  const off = +(fig.match(face)[1] || 0);
  fig = fig.replace(face, (m) => m + animT('translate', '0 0;0 0;0 -0.8;0 -0.8;0 0;0 0', EYES, ' additive="sum"'));
  const eye = /<ellipse cx="(44|52)" cy="28" rx="1\.8" ry="2\.4"([^>]*?)(?:\/>|>[\s\S]*?<\/ellipse>)/g;
  must(fig, eye, ch + ' 的眼睛');
  fig = fig.replace(eye, (m, cx, attrs) =>
    `<ellipse cx="${cx}" cy="28" rx="1.8" ry="2.4"${attrs}>${anim('ry', '2.4;2.4;0.01;0.01;2.4;2.4', EYES)}</ellipse>`);
  fig = fig.replace(/<circle cx="(44\.6|52\.6)" cy="27\.1" r="0\.62"([^>]*?)(?:\/>|>[\s\S]*?<\/circle>)/g, (m, cx, attrs) =>
    `<circle cx="${cx}" cy="27.1" r="0.62"${attrs}>${anim('opacity', '1;1;0;0;1;1', EYES)}</circle>`);
  const mouth = /<path d="M44\.8 32\.5 Q48 37\.2 51\.2 32\.5 Z"/;
  must(fig, mouth, ch + ' 的嘴巴');
  const happy = [44, 52].map((x) => `<path d="M${x - 2.2} 28.6 Q${x} 25.6 ${x + 2.2} 28.6" stroke-width="1.7" opacity="0">` +
    anim('opacity', '0;0;1;1;0;0', EYES) + '</path>').join('');
  fig = fig.replace(mouth, (m) => happy + m);

  // 3. 拿杯子的手：手掌就是杯子的旋轉中心，手臂終點跟手掌用同一組關鍵影格，移動時不會脫節
  const mouthY = 34 + off;                        // 嘴巴中心；杯緣中點轉 -45° 後剛好碰到嘴角
  const A = [64.5, 52], B = [60.4, +(mouthY + 3.2).toFixed(1)];
  const at = (p) => p.join(' ');
  const armA = `M59 41 Q68 44 ${at(A)}`, armB = `M59 41 Q67 37 ${at(B)}`;
  const armD = [armA, armA, armB, armB, armB, armA, armA].join(';');
  // 上寬下窄的玻璃杯＋杯口橢圓，水裝到六分滿（一眼看得出是杯水，不會像手機）
  const cup =
    `<path d="M-9.5 -9 L0.5 -9 L-0.9 2.6 Q-1.1 3.8 -2.3 3.8 L-6.7 3.8 Q-7.9 3.8 -8.1 2.6 Z" fill="#EAF7FC" stroke-width="1.8"/>` +
    `<path d="M-8.75 -3.4 L-0.25 -3.4 L-1.35 2.4 Q-1.5 3.1 -2.3 3.1 L-6.7 3.1 Q-7.5 3.1 -7.65 2.4 Z" fill="#6EC1E8" stroke="none"/>` +
    `<path d="M-8.6 -3.4 L-0.4 -3.4" stroke="#B8E3F5" stroke-width="1.2"/>` +
    `<ellipse cx="-4.5" cy="-9" rx="5" ry="1.4" fill="#F6FCFE" stroke-width="1.4"/>` +
    `<path d="M-7.2 -6.6 L-6.4 1" stroke="#FFFFFF" stroke-width="1.3" opacity="0.9"/>` +
    `<circle cx="0" cy="0" r="2.8" fill="${ink}" stroke="none"/>`;
  const drinkArm =
    `<path d="${armA}">${anim('d', armD, MOVE)}</path>` +
    `<g data-part="cup">${animT('translate', [A, A, B, B, B, A, A].map(at).join(';'), MOVE)}` +
    `<g>${animT('rotate', '0;0;-45;-53;-45;0;0', MOVE)}${cup}</g></g>`;

  // 4. 喝完「哈～」：頭的右邊冒出三條爽快線
  const x0 = ch === 'lasagna' ? 71 : 68, y0 = 25 + off;
  const spark = `<g stroke-width="1.7" opacity="0">${anim('opacity', '0;0;1;1;0', SPARK)}` +
    `<path d="M${x0} ${y0} L${x0 + 3.5} ${y0 - 3}"/><path d="M${x0 + 1.5} ${y0 + 5} L${x0 + 6} ${y0 + 4.5}"/>` +
    `<path d="M${x0} ${y0 + 10} L${x0 + 3.5} ${y0 + 12}"/></g>`;

  // 5. 腿不動，上半身（含新的手、杯子）放下杯子後小彈一下
  const legs = fig.indexOf('<g data-part="legs">');
  const legsEnd = fig.indexOf('</g>', legs) + 4;
  if (legs < 0) throw new Error(ch + ' 找不到腿');
  fig = fig.slice(0, legsEnd) +
    `\n  <g>${animT('translate', '0 0;0 0;0 -1.6;0 0;0 0', BOB)}` + fig.slice(legsEnd) + drinkArm + spark + '</g>\n';

  let top = head.replace('data-figure="idle"', 'data-figure="drink"');
  if (!paperOpen) return top + fig + '</svg>\n';
  const id = (paperOpen.match(/url\(#([^)]+)\)/) || [])[1];
  top = top.split(id).join('paper-drink');
  return top + paperOpen.replace(id, 'paper-drink') + fig + '</g>' + fig + '</svg>\n';
}

for (const ch of CHARACTERS) {
  const out = drink(ch, readFileSync(join(dir, ch, 'idle.svg'), 'utf8'));
  writeFileSync(join(dir, ch, 'drink.svg'), out);
  console.log(ch + '/drink.svg ' + out.length + ' bytes');
}
