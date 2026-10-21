// Shared mock scene: picture plane, square grid, vanishing-point fans, horizon, a box, trace lines, cone of vision.
// Palette values copy src/Model/Palette.cs.
const PALETTES = {
  atelier: { void: '#2B2724', border: '#1A1715', paper: '#F5F0E6', ink: '#23201D', grid: '#8C8272', horizon: '#8C8272', prompt: '#A8563C',
    vp: ['#8C6A46', '#A85A62', '#6F8158', '#B07A2E', '#7C5C74', '#9A6B4F', '#6B7A6A', '#A8563C', '#7A6A52'] },
  ink: { void: '#262626', border: '#1A1A1A', paper: '#F7F7F4', ink: '#111111', grid: '#8C8C8C', horizon: '#6E6E6E', prompt: '#B07A2E',
    vp: ['#3A3A3A', '#4E4E4E', '#626262', '#767676', '#8A8A8A', '#9E9E9E', '#B2B2B2', '#C6C6C6', '#B07A2E'] },
  charcoal: { void: '#121110', border: '#0C0B0A', paper: '#1E1C1A', ink: '#EDE6D8', grid: '#6E655A', horizon: '#8A7D6C', prompt: '#D98E5F',
    vp: ['#D8D0C0', '#C8B89A', '#E0CFA8', '#B89C78', '#D6B98C', '#A89070', '#C4B49C', '#E2D6BE', '#C08A5A'] },
};

const NS = 'http://www.w3.org/2000/svg';
const PLANE = { x: 120, y: 80, w: 560, h: 440 };
const HZ = 260, VP1 = [40, HZ], VP2 = [760, HZ];

function el(tag, attrs, parent) {
  const e = document.createElementNS(NS, tag);
  for (const k in attrs) e.setAttribute(k, attrs[k]);
  if (parent) parent.appendChild(e);
  return e;
}

function intersect([x1, y1], [x2, y2], [x3, y3], [x4, y4]) {
  const d = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
  const t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / d;
  return [x1 + t * (x2 - x1), y1 + t * (y2 - y1)];
}

// Point on line p->vp at the given x.
function atX(p, vp, x) {
  return [x, p[1] + (x - p[0]) * (vp[1] - p[1]) / (vp[0] - p[0])];
}

function drawScene(svg, name = 'atelier', opts = {}) {
  const c = PALETTES[name];
  const o = { grid: true, gridStyle: 0, cone: false, reference: false, box: true, trace: true, selected: 0, ...opts };
  svg.innerHTML = '';
  svg.setAttribute('viewBox', '0 0 800 600');
  svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');
  svg.style.background = c.void;

  const defs = el('defs', {}, svg);
  const clip = el('clipPath', { id: 'plane-' + svg.id }, defs);
  el('rect', { x: PLANE.x, y: PLANE.y, width: PLANE.w, height: PLANE.h }, clip);

  el('rect', { x: PLANE.x - 3, y: PLANE.y - 3, width: PLANE.w + 6, height: PLANE.h + 6, fill: c.border }, svg);
  el('rect', { x: PLANE.x, y: PLANE.y, width: PLANE.w, height: PLANE.h, fill: c.paper }, svg);
  const g = el('g', { 'clip-path': `url(#plane-${svg.id})` }, svg);

  if (o.reference) {
    // Stand-in for a traced background photo.
    el('rect', { x: PLANE.x, y: HZ, width: PLANE.w, height: PLANE.h, fill: c.grid, opacity: 0.18 }, g);
    el('path', { d: 'M120 330 L260 290 L400 300 L560 280 L680 320 L680 520 L120 520Z', fill: c.grid, opacity: 0.22 }, g);
  }

  if (o.grid) {
    // gridStyle matches Prim.cs: 0 both, 1 vertical, 2 horizontal.
    if (o.gridStyle !== 2) for (let x = PLANE.x + 40; x < PLANE.x + PLANE.w; x += 40) el('line', { x1: x, y1: PLANE.y, x2: x, y2: PLANE.y + PLANE.h, stroke: c.grid, 'stroke-width': 0.6, opacity: 0.35 }, g);
    if (o.gridStyle !== 1) for (let y = PLANE.y + 40; y < PLANE.y + PLANE.h; y += 40) el('line', { x1: PLANE.x, y1: y, x2: PLANE.x + PLANE.w, y2: y, stroke: c.grid, 'stroke-width': 0.6, opacity: 0.35 }, g);
  }

  {
    [[VP1, c.vp[0]], [VP2, c.vp[1]]].forEach(([vp, col]) => {
      for (let i = -14; i <= 14; i++) {
        const ex = vp[0] < 400 ? 900 : -100;
        el('line', { x1: vp[0], y1: vp[1], x2: ex, y2: HZ + i * 48, stroke: col, 'stroke-width': 0.8, opacity: 0.55 }, g);
      }
    });
  }

  el('line', { x1: 0, y1: HZ, x2: 800, y2: HZ, stroke: c.horizon, 'stroke-width': 1.2 }, g);

  if (o.box) {
    const B = [400, 430], T = [400, 320];
    const BL = atX(B, VP1, 290), BR = atX(B, VP2, 530);
    const TL = atX(T, VP1, 290), TR = atX(T, VP2, 530);
    const TB = intersect(TL, VP2, TR, VP1);
    const s = { stroke: c.ink, 'stroke-width': 1.8, fill: 'none', 'stroke-linejoin': 'round' };
    el('path', { ...s, d: `M${BL} L${B} L${BR} L${TR} L${TB} L${TL} Z M${B} L${T} L${TL} M${T} L${TR}` }, g);
  }

  if (o.trace) {
    el('line', { x1: 160, y1: 470, x2: 640, y2: 150, stroke: c.prompt, 'stroke-width': 1.4, 'stroke-dasharray': '6 4' }, g);
    el('line', { x1: 180, y1: 140, x2: 660, y2: 450, stroke: c.prompt, 'stroke-width': 1.4, 'stroke-dasharray': '6 4' }, g);
  }

  if (o.cone) {
    // 2-point station distance sqrt(360*360) = 360; 60 deg cone radius = 360 * tan(30deg).
    el('circle', { cx: 400, cy: HZ, r: 207.8, fill: 'none', stroke: c.prompt, 'stroke-width': 1.2, 'stroke-dasharray': '3 5' }, svg);
    el('circle', { cx: 400, cy: HZ, r: 3, fill: c.prompt }, svg);
  }

  [VP1, VP2].forEach((vp, i) => {
    const sel = o.selected === i;
    el('circle', { cx: vp[0], cy: vp[1], r: sel ? 15 : 12, fill: c.vp[i], stroke: sel ? c.paper : c.border, 'stroke-width': sel ? 2.5 : 1.5 }, svg);
    const t = el('text', { x: vp[0], y: vp[1] + 4.5, 'text-anchor': 'middle', 'font-size': 13, 'font-family': 'system-ui, sans-serif', 'font-weight': 700, fill: c.paper }, svg);
    t.textContent = i + 1;
  });
  return c;
}
