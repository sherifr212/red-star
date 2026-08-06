// Generates RedStar logo concept SVGs (marks + lockups) into ./svg
const fs = require('fs');
const path = require('path');
const OUT = path.join(__dirname, '..', 'svg');
fs.mkdirSync(OUT, { recursive: true });

// ---------- palette ----------
const RED = '#E1332D';
const RED_DEEP = '#A81E19';
const RED_LIT = '#FF6A5E';
const INK = '#0D0F12';
const TILE = '#15181E';
const PAPER = '#FFFFFF';
const OFFWHITE = '#F4F1EE';
const MUTED_D = '#7C8698';
const MUTED_L = '#6B7280';

// ---------- geometry helpers ----------
const rad = d => (d * Math.PI) / 180;
const P = (x, y) => [round(x), round(y)];
const round = n => Math.round(n * 1000) / 1000;

function starPoints(cx, cy, R, r, n = 5, rot = -90) {
  const pts = [];
  for (let i = 0; i < n * 2; i++) {
    const ang = rad(rot + (i * 180) / n);
    const rr = i % 2 === 0 ? R : r;
    pts.push(P(cx + rr * Math.cos(ang), cy + rr * Math.sin(ang)));
  }
  return pts;
}
const poly = pts => pts.map(p => p.join(',')).join(' ');

// concave 4-point sparkle via cubic curves
function sparkle(cx, cy, R, waist = 0.30) {
  const w = R * waist;
  const p = [];
  const tips = [[0, -R], [R, 0], [0, R], [-R, 0]];
  let d = `M ${round(cx)} ${round(cy - R)}`;
  const ctrl = R * waist;
  d += ` C ${round(cx + ctrl * 0.35)} ${round(cy - ctrl)} ${round(cx + ctrl)} ${round(cy - ctrl * 0.35)} ${round(cx + R)} ${round(cy)}`;
  d += ` C ${round(cx + ctrl)} ${round(cy + ctrl * 0.35)} ${round(cx + ctrl * 0.35)} ${round(cy + ctrl)} ${round(cx)} ${round(cy + R)}`;
  d += ` C ${round(cx - ctrl * 0.35)} ${round(cy + ctrl)} ${round(cx - ctrl)} ${round(cy + ctrl * 0.35)} ${round(cx - R)} ${round(cy)}`;
  d += ` C ${round(cx - ctrl)} ${round(cy - ctrl * 0.35)} ${round(cx - ctrl * 0.35)} ${round(cy - ctrl)} ${round(cx)} ${round(cy - R)} Z`;
  return d;
}

// ---------- concept marks (512x512 viewBox) ----------
// Each returns { defs, body } so lockups can reuse the same artwork.

function markPromptStar() {
  const s = starPoints(330, 258, 96, 40);
  return {
    defs: `<linearGradient id="c1g" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="${RED_LIT}"/><stop offset="1" stop-color="${RED}"/></linearGradient>`,
    body: `
    <rect x="0" y="0" width="512" height="512" rx="116" fill="${TILE}"/>
    <rect x="3" y="3" width="506" height="506" rx="113" fill="none" stroke="#2A3038" stroke-width="6"/>
    <polyline points="152,190 224,258 152,326" fill="none" stroke="${OFFWHITE}" stroke-width="34"
              stroke-linecap="round" stroke-linejoin="round"/>
    <polygon points="${poly(s)}" fill="url(#c1g)" stroke="url(#c1g)" stroke-width="14" stroke-linejoin="round"/>
    <rect x="152" y="366" width="208" height="24" rx="12" fill="${RED}" opacity="0.35"/>`,
  };
}

function markOrbit() {
  const s = starPoints(256, 256, 118, 49);
  return {
    defs: `<linearGradient id="c2g" x1="0.15" y1="0" x2="0.85" y2="1">
      <stop offset="0" stop-color="${RED_LIT}"/><stop offset="1" stop-color="${RED_DEEP}"/></linearGradient>`,
    body: `
    <g transform="rotate(-24 256 256)">
      <ellipse cx="256" cy="256" rx="238" ry="104" fill="none" stroke="${RED}" stroke-width="11" opacity="0.42"/>
      <ellipse cx="256" cy="256" rx="168" ry="72" fill="none" stroke="${RED}" stroke-width="9" opacity="0.22"/>
    </g>
    <polygon points="${poly(s)}" fill="url(#c2g)" stroke="url(#c2g)" stroke-width="16" stroke-linejoin="round"/>
    <g transform="rotate(-24 256 256)">
      <circle cx="494" cy="256" r="26" fill="${RED}"/>
      <circle cx="18" cy="256" r="18" fill="${RED}" opacity="0.75"/>
      <circle cx="256" cy="184" r="15" fill="${RED_LIT}" opacity="0.9"/>
    </g>`,
  };
}

function markFacet() {
  const cx = 256, cy = 262, R = 224, r = 92;
  const p = starPoints(cx, cy, R, r);
  let tris = '';
  for (let i = 0; i < 10; i++) {
    const a = p[i], b = p[(i + 1) % 10];
    const fill = i % 2 === 0 ? RED : RED_DEEP;
    tris += `<polygon points="${cx},${cy} ${a.join(',')} ${b.join(',')}" fill="${fill}"/>`;
  }
  return {
    defs: '',
    body: `<g>${tris}</g>`,
  };
}

function markSparkle() {
  return {
    defs: `<linearGradient id="c4g" x1="0.1" y1="0" x2="0.9" y2="1">
        <stop offset="0" stop-color="${RED_LIT}"/><stop offset="0.55" stop-color="${RED}"/><stop offset="1" stop-color="${RED_DEEP}"/></linearGradient>
      <linearGradient id="c4h" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stop-color="${RED_LIT}"/><stop offset="1" stop-color="${RED}"/></linearGradient>`,
    body: `
    <path d="${sparkle(238, 262, 218, 0.30)}" fill="url(#c4g)"/>
    <path d="${sparkle(430, 108, 78, 0.32)}" fill="url(#c4h)"/>
    <path d="${sparkle(432, 400, 48, 0.32)}" fill="${RED}" opacity="0.8"/>`,
  };
}

function markPanel() {
  const s = starPoints(120, 120, 66, 27);
  return {
    defs: `<mask id="c5m">
        <rect x="0" y="0" width="512" height="512" fill="#fff"/>
        <circle cx="120" cy="120" r="92" fill="#000"/>
      </mask>`,
    body: `
    <rect x="48" y="48" width="416" height="416" rx="72" fill="none" stroke="${RED}" stroke-width="16" mask="url(#c5m)"/>
    <polygon points="${poly(s)}" fill="${RED}" stroke="${RED}" stroke-width="12" stroke-linejoin="round"/>
    <rect x="140" y="236" width="252" height="26" rx="13" fill="${RED}" opacity="0.85"/>
    <rect x="140" y="300" width="196" height="26" rx="13" fill="${RED}" opacity="0.55"/>
    <rect x="140" y="364" width="118" height="26" rx="13" fill="${RED}" opacity="0.30"/>`,
  };
}

function markComet() {
  const s = starPoints(330, 186, 130, 54);
  return {
    defs: `<linearGradient id="c6t" x1="1" y1="0" x2="0" y2="1">
        <stop offset="0" stop-color="${RED}" stop-opacity="0.95"/>
        <stop offset="1" stop-color="${RED}" stop-opacity="0"/></linearGradient>
      <linearGradient id="c6g" x1="0.2" y1="0" x2="0.9" y2="1">
        <stop offset="0" stop-color="${RED_LIT}"/><stop offset="1" stop-color="${RED}"/></linearGradient>`,
    body: `
    <g stroke="url(#c6t)" stroke-linecap="round" fill="none">
      <path d="M 250 262 L 66 446" stroke-width="46"/>
      <path d="M 318 344 L 210 452" stroke-width="26"/>
      <path d="M 168 224 L 74 318" stroke-width="20"/>
    </g>
    <polygon points="${poly(s)}" fill="url(#c6g)" stroke="url(#c6g)" stroke-width="16" stroke-linejoin="round"/>`,
  };
}

// ---------- concepts ----------
const TAGLINE = '.NET CLI FOR SELF-HOSTED LLMS';

const CONCEPTS = [
  {
    id: '01-prompt-star', title: 'Prompt Star', mark: markPromptStar,
    word: { font: "'Cascadia Mono','Consolas',monospace", size: 104, weight: 700, tracking: -2,
            parts: [['red', 'red'], ['star', 'fg']], cursor: true },
  },
  {
    id: '02-orbit', title: 'Orbit', mark: markOrbit,
    word: { font: "'Bahnschrift','Segoe UI',sans-serif", size: 116, weight: 600, tracking: -1,
            parts: [['Red', 'red'], ['Star', 'fg']] },
  },
  {
    id: '03-facet-star', title: 'Facet Star', mark: markFacet,
    word: { font: "'Bahnschrift','Segoe UI',sans-serif", size: 100, weight: 700, tracking: 9,
            parts: [['RED', 'red'], ['STAR', 'fg']] },
  },
  {
    id: '04-sparkle', title: 'Sparkle', mark: markSparkle,
    word: { font: "'Segoe UI','Arial',sans-serif", size: 112, weight: 700, tracking: -3,
            parts: [['Red', 'red'], ['Star', 'fg']] },
  },
  {
    id: '05-panel', title: 'Panel', mark: markPanel,
    word: { font: "'Cascadia Mono','Consolas',monospace", size: 100, weight: 700, tracking: -2,
            parts: [['Red', 'red'], ['Star', 'fg']] },
  },
  {
    id: '06-comet', title: 'Comet', mark: markComet,
    word: { font: "'Bahnschrift','Segoe UI',sans-serif", size: 116, weight: 600, tracking: -1,
            parts: [['Red', 'red'], ['Star', 'fg']] },
  },
];

// ---------- text measurement ----------
// Chrome renders these SVG <text> nodes, JS reads getComputedTextLength(), and --dump-dom hands the
// numbers back so lockup canvases can be sized to the real glyph widths instead of an estimate.
let MEASURES = {};
const measuresPath = path.join(__dirname, 'measures.json');
if (fs.existsSync(measuresPath)) MEASURES = JSON.parse(fs.readFileSync(measuresPath, 'utf8'));

function writeMeasurePage() {
  const nodes = CONCEPTS.map(c => {
    const w = c.word;
    const text = w.parts.map(p => p[0]).join('');
    return `<text id="w_${c.id}" x="0" y="200" font-family="${w.font}" font-size="${w.size}"
      font-weight="${w.weight}" letter-spacing="${w.tracking}">${text}</text>`;
  }).join('\n');
  const html = `<html><body style="margin:0">
<svg xmlns="http://www.w3.org/2000/svg" width="3000" height="400">
${nodes}
<text id="tagline" x="0" y="380" font-family="'Bahnschrift','Segoe UI',sans-serif" font-size="27"
      font-weight="500" letter-spacing="6.5">${TAGLINE}</text>
</svg>
<pre id="out"></pre>
<script>
  const ids = [${CONCEPTS.map(c => `'w_${c.id}'`).join(',')}, 'tagline'];
  const o = {};
  for (const id of ids) o[id] = document.getElementById(id).getComputedTextLength();
  document.getElementById('out').textContent = 'MEASURES=' + JSON.stringify(o);
</script></body></html>`;
  fs.writeFileSync(path.join(__dirname, 'measure.html'), html);
}

function wordWidth(c) {
  const key = `w_${c.id}`;
  if (MEASURES[key]) return MEASURES[key];
  const w = c.word;
  const text = w.parts.map(p => p[0]).join('');
  const per = w.font.includes('Cascadia') ? 0.6 : 0.56;
  return text.length * (w.size * per + w.tracking);
}
const taglineWidth = () => MEASURES.tagline || TAGLINE.length * 20;

// ---------- emitters ----------
function svgMark(c) {
  const m = c.mark();
  return `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
  <defs>${m.defs}</defs>${m.body}
</svg>`;
}

// Wordmark drawn at its native font size at the origin, so callers can scale the whole group
// (text + block cursor together) rather than rescaling the font size and leaving the cursor behind.
function wordmarkSvg(c, dark) {
  const w = c.word;
  const fg = dark ? OFFWHITE : INK;
  const tspans = w.parts
    .map(([t, kind]) => `<tspan fill="${kind === 'red' ? RED : fg}">${t}</tspan>`)
    .join('');
  const cursor = w.cursor
    ? `<rect x="${round(wordWidth(c) + 18)}" y="${round(-w.size * 0.66)}" width="${round(w.size * 0.46)}" height="${round(w.size * 0.78)}" fill="${RED}" opacity="0.85"/>`
    : '';
  return `<text x="0" y="0" font-family="${w.font}" font-size="${w.size}" font-weight="${w.weight}"
      letter-spacing="${w.tracking}">${tspans}</text>${cursor}`;
}

function wordBlockWidth(c) {
  return wordWidth(c) + (c.word.cursor ? 18 + c.word.size * 0.46 : 0);
}

function svgLockup(c, dark) {
  const m = c.mark();
  const bg = dark ? INK : PAPER;
  const muted = dark ? MUTED_D : MUTED_L;
  const H = 420;
  const markSize = 216, markX = 96, markY = (H - markSize) / 2;
  const textX = markX + markSize + 68;
  const contentW = Math.max(wordBlockWidth(c), taglineWidth() + 4);
  const W = Math.round(textX + contentW + 96);
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">
  <defs>${m.defs}</defs>
  <rect width="${W}" height="${H}" fill="${bg}"/>
  <g transform="translate(${markX} ${markY}) scale(${round(markSize / 512)})">${m.body}</g>
  <g transform="translate(${textX} 226)">${wordmarkSvg(c, dark)}</g>
  <text x="${textX + 4}" y="286" font-family="'Bahnschrift','Segoe UI',sans-serif" font-size="27"
        font-weight="500" letter-spacing="6.5" fill="${muted}">${TAGLINE}</text>
</svg>`;
}

// contact sheet: every concept, mark + lockup, on both backgrounds
function svgSheet() {
  const rowH = 300, W = 2000, headH = 190;
  const H = headH + CONCEPTS.length * rowH + 60;
  let rows = '';
  CONCEPTS.forEach((c, i) => {
    const m = c.mark();
    const y = headH + i * rowH;
    const half = W / 2;
    const ws = 0.62;
    rows += `
    <g>
      <rect x="0" y="${y}" width="${half}" height="${rowH}" fill="${INK}"/>
      <rect x="${half}" y="${y}" width="${half}" height="${rowH}" fill="${PAPER}"/>
      <line x1="0" y1="${y}" x2="${half}" y2="${y}" stroke="#3A424E" stroke-width="2"/>
      <line x1="${half}" y1="${y}" x2="${W}" y2="${y}" stroke="#D8DCE2" stroke-width="2"/>
      <text x="34" y="${y + 46}" font-family="'Cascadia Mono','Consolas',monospace" font-size="22"
            font-weight="700" fill="${RED}">${String(i + 1).padStart(2, '0')}</text>
      <text x="74" y="${y + 46}" font-family="'Bahnschrift','Segoe UI',sans-serif" font-size="24"
            font-weight="600" letter-spacing="3" fill="${OFFWHITE}">${c.title.toUpperCase()}</text>
      <g transform="translate(44 ${y + 82}) scale(${round(168 / 512)})">${m.body}</g>
      <g transform="translate(262 ${y + 158}) scale(${ws})">${wordmarkSvg(c, true)}</g>
      <text x="266" y="${y + 200}" font-family="'Bahnschrift','Segoe UI',sans-serif" font-size="17"
            font-weight="500" letter-spacing="4" fill="${MUTED_D}">${TAGLINE}</text>
      <g transform="translate(${half + 44} ${y + 82}) scale(${round(168 / 512)})">${m.body}</g>
      <g transform="translate(${half + 262} ${y + 158}) scale(${ws})">${wordmarkSvg(c, false)}</g>
      <text x="${half + 266}" y="${y + 200}" font-family="'Bahnschrift','Segoe UI',sans-serif" font-size="17"
            font-weight="500" letter-spacing="4" fill="${MUTED_L}">${TAGLINE}</text>
      <text x="${half - 280}" y="${y + 46}" font-family="'Bahnschrift','Segoe UI',sans-serif" font-size="15"
            font-weight="500" letter-spacing="3" fill="${MUTED_D}">FAVICON CHECK</text>
      <g transform="translate(${half - 280} ${y + 82}) scale(${round(64 / 512)})">${m.body}</g>
      <g transform="translate(${half - 180} ${y + 100}) scale(${round(32 / 512)})">${m.body}</g>
      <g transform="translate(${half - 120} ${y + 108}) scale(${round(20 / 512)})">${m.body}</g>
      <text x="${W - 280}" y="${y + 46}" font-family="'Bahnschrift','Segoe UI',sans-serif" font-size="15"
            font-weight="500" letter-spacing="3" fill="${MUTED_L}">FAVICON CHECK</text>
      <g transform="translate(${W - 280} ${y + 82}) scale(${round(64 / 512)})">${m.body}</g>
      <g transform="translate(${W - 180} ${y + 100}) scale(${round(32 / 512)})">${m.body}</g>
      <g transform="translate(${W - 120} ${y + 108}) scale(${round(20 / 512)})">${m.body}</g>
    </g>`;
  });
  const defs = CONCEPTS.map(c => c.mark().defs).join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">
  <defs>${defs}</defs>
  <rect width="${W}" height="${H}" fill="${INK}"/>
  <text x="40" y="86" font-family="'Bahnschrift','Segoe UI',sans-serif" font-size="54" font-weight="600"
        letter-spacing="-1"><tspan fill="${RED}">RedStar</tspan><tspan fill="${OFFWHITE}"> — logo concepts</tspan></text>
  <text x="42" y="128" font-family="'Bahnschrift','Segoe UI',sans-serif" font-size="22" font-weight="500"
        letter-spacing="4" fill="${MUTED_D}">SIX DIRECTIONS · MARK + LOCKUP · DARK AND LIGHT · MARK LEGIBILITY AT 64, 32 AND 20 PX</text>
  ${rows}
</svg>`;
}

// ---------- write ----------
writeMeasurePage();
const svgSize = s => {
  const mm = s.match(/width="(\d+)" height="(\d+)"/);
  return [Number(mm[1]), Number(mm[2])];
};
const manifest = [];
for (const c of CONCEPTS) {
  const dark = svgLockup(c, true), light = svgLockup(c, false);
  const files = [
    [`${c.id}-mark.svg`, svgMark(c), 512, 512],
    [`${c.id}-lockup-dark.svg`, dark, ...svgSize(dark)],
    [`${c.id}-lockup-light.svg`, light, ...svgSize(light)],
  ];
  for (const [name, body, w, h] of files) {
    fs.writeFileSync(path.join(OUT, name), body);
    manifest.push({ name, w, h, transparent: name.endsWith('-mark.svg') });
  }
}
const sheet = svgSheet();
fs.writeFileSync(path.join(OUT, 'contact-sheet.svg'), sheet);
const [sw, sh] = svgSize(sheet);
manifest.push({ name: 'contact-sheet.svg', w: sw, h: sh, transparent: false });
fs.writeFileSync(path.join(__dirname, 'manifest.json'), JSON.stringify(manifest, null, 2));
console.log(`wrote ${manifest.length} svgs`);
