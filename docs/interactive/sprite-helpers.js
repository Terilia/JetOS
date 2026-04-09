// ═══════════════════════════════════════════════════════════════
// 1:1 port of Mdk.PbScript2/Utilities/SpriteHelpers.cs and the
// MFDTheme palette (UI/UIController.cs).
//
// SE renders sprites by issuing batched draw calls each tick. We
// mirror that here with a Frame object that records calls and
// then flushes them to a 2D canvas. Coordinates and sizes match
// the in-game pixel layout (typically 512×512 per LCD surface).
//
// Loaded as a regular <script> (not a module) so it works over
// file:// without a local server. Everything is attached to the
// global `JOS` namespace.
// ═══════════════════════════════════════════════════════════════
(function (global) {
'use strict';

// ── MFDTheme — exact RGB triples from UI/UIController.cs:14-44 ──
const MFDTheme = {
  BG:           [5, 8, 5],
  PANEL_BG:     [8, 14, 8],
  HEADER_BG:    [10, 18, 10],
  BORDER:       [24, 40, 24],
  BORDER_LIGHT: [20, 30, 20],
  DIM_TEXT:     [42, 74, 42],
  DIM_TEXT_MID: [58, 90, 58],
  MID_TEXT:     [74, 122, 74],
  NORMAL_TEXT:  [90, 154, 90],
  BRIGHT_TEXT:  [144, 208, 144],
  ACCENT:       [64, 160, 64],
  CORP_GOLD:    [138, 122, 80],
  GOLD_DIM:     [55, 49, 32],
  GOLD_LINE:    [58, 53, 32],
  SEL_FILL:     [14, 28, 14],
  SEL_BORDER:   [26, 48, 26],
  ROW_DIVIDER:  [12, 20, 12],
  CORNER:       [42, 74, 42],
  BC_BG:        [8, 14, 8],
  BC_BORDER:    [16, 26, 16],
  STATUS_RDY:   [80, 160, 80],
  BAR_TRACK:    [6, 10, 6],
  BAR_FILL:     [48, 144, 48],
  STATUS_VAL:   [80, 144, 80],
  WARN:         [192, 160, 48],
  FONT:         "Monospace",
  SQ:           "SquareSimple",
  NC:           "NYINAH CORP",
};

// ── HUD theme palette — HUDModule.cs:17-37 ──
// Theme 0=Green, 1=Cyan, 2=Amber, 3=White
const HUD_THEMES = [
  { primary:[50,205,50],   secondary:[0,128,0],     horizon:[50,205,50],   radarFriendly:[0,100,0]  },
  { primary:[0,255,255],   secondary:[30,144,255],  horizon:[0,191,255],   radarFriendly:[0,0,139]  },
  { primary:[255,165,0],   secondary:[184,134,11],  horizon:[218,165,32],  radarFriendly:[184,134,11]},
  { primary:[255,255,255], secondary:[128,128,128], horizon:[211,211,211], radarFriendly:[169,169,169]},
];
const HUD_EMPHASIS = [255, 255, 0];   // Color.Yellow
const HUD_WARNING  = [255, 0, 0];     // Color.Red
const HUD_INFO     = [255, 255, 255]; // Color.White

// ── Vector helpers (mirroring Shortcuts.cs) ──
const PI = Math.PI;
const TWO_PI = Math.PI * 2;
const ToRad = d => d * Math.PI / 180;
const ToDeg = r => r * 180 / Math.PI;
const Mn = Math.min;
const Mx = Math.max;
const Ab = Math.abs;
const Cl = (v, mn, mx) => Math.max(mn, Math.min(mx, v));
function V2(x, y) { return { x, y }; }
function vAdd(a, b) { return { x: a.x + b.x, y: a.y + b.y }; }
function vSub(a, b) { return { x: a.x - b.x, y: a.y - b.y }; }
function vMul(a, s) { return { x: a.x * s, y: a.y * s }; }
function vLen(a) { return Math.sqrt(a.x * a.x + a.y * a.y); }
function vNorm(a) { const l = vLen(a) || 1; return { x: a.x / l, y: a.y / l }; }
function vDist(a, b) { return Math.hypot(a.x - b.x, a.y - b.y); }

// 3D
const VZ = { x: 0, y: 0, z: 0 };
function V3(x, y, z) { return { x, y, z }; }
function v3Add(a, b) { return { x: a.x + b.x, y: a.y + b.y, z: a.z + b.z }; }
function v3Sub(a, b) { return { x: a.x - b.x, y: a.y - b.y, z: a.z - b.z }; }
function v3Mul(a, s) { return { x: a.x * s, y: a.y * s, z: a.z * s }; }
function v3Dot(a, b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
function v3Cross(a, b) {
  return {
    x: a.y * b.z - a.z * b.y,
    y: a.z * b.x - a.x * b.z,
    z: a.x * b.y - a.y * b.x,
  };
}
function v3Len(a) { return Math.sqrt(a.x * a.x + a.y * a.y + a.z * a.z); }
function v3LenSq(a) { return a.x * a.x + a.y * a.y + a.z * a.z; }
function v3Norm(a) { const l = v3Len(a) || 1; return { x: a.x / l, y: a.y / l, z: a.z / l }; }
function v3Dist(a, b) { return Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z); }

// MatrixD (4x4 row-major). VRageMath is right-handed; Forward = -Z.
function matFromForwardUp(forward, up, pos) {
  const f = v3Norm(forward);
  const r = v3Norm(v3Cross(up, f));
  const u = v3Cross(f, r);
  return {
    m11: r.x, m12: r.y, m13: r.z, m14: 0,
    m21: u.x, m22: u.y, m23: u.z, m24: 0,
    m31: -f.x, m32: -f.y, m33: -f.z, m34: 0,
    m41: pos.x, m42: pos.y, m43: pos.z, m44: 1,
    Right: r, Up: u, Forward: f, Translation: pos,
  };
}
function matTranspose(m) {
  return {
    m11: m.m11, m12: m.m21, m13: m.m31, m14: m.m41,
    m21: m.m12, m22: m.m22, m23: m.m32, m24: m.m42,
    m31: m.m13, m32: m.m23, m33: m.m33, m34: m.m43,
    m41: m.m14, m42: m.m24, m43: m.m34, m44: m.m44,
  };
}
// VTN = Vector3D.TransformNormal — applies the upper 3x3 only (no translation)
function VTN(v, m) {
  return {
    x: v.x * m.m11 + v.y * m.m21 + v.z * m.m31,
    y: v.x * m.m12 + v.y * m.m22 + v.z * m.m32,
    z: v.x * m.m13 + v.y * m.m23 + v.z * m.m33,
  };
}

// ── rgba helper ──
function rgba(c, a) {
  if (a === undefined) {
    if (c.length === 4) a = c[3];
    else a = 255;
  }
  return `rgba(${c[0]},${c[1]},${c[2]},${(a / 255).toFixed(3)})`;
}
function colorAlpha(c, a) {
  return [c[0], c[1], c[2], Math.round(a * 255)];
}

// ═══════════════════════════════════════════════════════════════
// Frame — mirrors VRage's MySpriteDrawFrame
// ═══════════════════════════════════════════════════════════════
function Frame(ctx, surfaceSize) {
  this.ctx = ctx;
  this.surfaceSize = surfaceSize;
  this.sprites = [];
}
Frame.prototype.add = function (s) { this.sprites.push(s); };
Frame.prototype.flush = function () {
  const ctx = this.ctx;
  for (const s of this.sprites) {
    if (s.kind === 'rect') {
      ctx.save();
      ctx.translate(s.x, s.y);
      if (s.rot) ctx.rotate(s.rot);
      ctx.fillStyle = s.color;
      ctx.fillRect(-s.w / 2, -s.h / 2, s.w, s.h);
      ctx.restore();
    } else if (s.kind === 'text') {
      ctx.save();
      const fontSize = s.scale * 28;
      ctx.font = `${fontSize}px ${s.font}`;
      ctx.fillStyle = s.color;
      ctx.textBaseline = 'top';
      ctx.textAlign = s.align === 'right' ? 'right' : s.align === 'center' ? 'center' : 'left';
      ctx.fillText(s.text, s.x, s.y);
      ctx.restore();
    } else if (s.kind === 'sprite') {
      ctx.save();
      ctx.translate(s.x, s.y);
      if (s.rot) ctx.rotate(s.rot);
      ctx.fillStyle = s.color;
      ctx.strokeStyle = s.color;
      if (s.data === 'SquareSimple') {
        ctx.fillRect(-s.w / 2, -s.h / 2, s.w, s.h);
      } else if (s.data === 'Circle') {
        ctx.beginPath();
        ctx.arc(0, 0, s.w / 2, 0, TWO_PI);
        ctx.fill();
      } else if (s.data === 'CircleHollow') {
        ctx.lineWidth = Math.max(1, s.w * 0.08);
        ctx.beginPath();
        ctx.arc(0, 0, s.w / 2, 0, TWO_PI);
        ctx.stroke();
      } else if (s.data === 'Triangle') {
        ctx.beginPath();
        ctx.moveTo(0, -s.h / 2);
        ctx.lineTo(s.w / 2, s.h / 2);
        ctx.lineTo(-s.w / 2, s.h / 2);
        ctx.closePath();
        ctx.fill();
      }
      ctx.restore();
    }
  }
  this.sprites.length = 0;
};

// ═══════════════════════════════════════════════════════════════
// SpriteHelpers — port of SpriteHelpers.cs:27-117
// ═══════════════════════════════════════════════════════════════
const SpriteHelpers = {
  CIRC_SEGS: 24,
  CSin: null,
  CCos: null,

  init() {
    if (this.CSin) return;
    this.CSin = new Float32Array(this.CIRC_SEGS + 1);
    this.CCos = new Float32Array(this.CIRC_SEGS + 1);
    for (let i = 0; i <= this.CIRC_SEGS; i++) {
      const a = (i * 2 * Math.PI) / this.CIRC_SEGS;
      this.CSin[i] = Math.sin(a);
      this.CCos[i] = Math.cos(a);
    }
  },

  Bx(frame, x, y, w, h, c, r) {
    frame.add({ kind: 'rect', x, y, w, h, color: rgba(c), rot: r || 0 });
  },

  Sp(frame, data, x, y, w, h, c, r) {
    frame.add({ kind: 'sprite', data, x, y, w, h, color: rgba(c), rot: r || 0 });
  },

  Tt(frame, text, x, y, scale, c, align, font) {
    align = align || 'left';
    font = font || MFDTheme.FONT;
    frame.add({ kind: 'text', text: String(text), x, y, scale, color: rgba(c), align, font });
  },

  FBx(x, y, w, h, c) {
    return { kind: 'rect', x, y, w, h, color: rgba(c), rot: 0 };
  },

  FTt(text, x, y, scale, c, align, font) {
    return { kind: 'text', text: String(text), x, y, scale, color: rgba(c), align, font };
  },

  AddLineSprite(frame, start, end, thickness, c) {
    const dx = end.x - start.x;
    const dy = end.y - start.y;
    const length = Math.hypot(dx, dy);
    if (length < 0.1) return;
    const px = start.x + dx / 2;
    const py = start.y + dy / 2;
    const rotation = Math.atan2(dy, dx) - Math.PI / 2;
    this.Bx(frame, px, py, thickness, length, c, rotation);
  },

  DrawRectangleOutline(frame, x, y, w, h, lineWidth, c) {
    this.Bx(frame, x + w / 2, y, w, lineWidth, c);
    this.Bx(frame, x + w / 2, y + h, w, lineWidth, c);
    this.Bx(frame, x, y + h / 2, lineWidth, h, c);
    this.Bx(frame, x + w, y + h / 2, lineWidth, h, c);
  },

  DrawCircleOutline(frame, center, radius, c, thickness) {
    this.init();
    for (let i = 0; i < this.CIRC_SEGS; i++) {
      const p1 = { x: center.x + this.CCos[i] * radius,     y: center.y + this.CSin[i] * radius };
      const p2 = { x: center.x + this.CCos[i + 1] * radius, y: center.y + this.CSin[i + 1] * radius };
      const dx = p2.x - p1.x;
      const dy = p2.y - p1.y;
      const length = Math.hypot(dx, dy);
      if (length > 0) {
        const mid = { x: (p1.x + p2.x) / 2, y: (p1.y + p2.y) / 2 };
        const rotation = Math.atan2(dy, dx);
        this.Bx(frame, mid.x, mid.y, length + thickness, thickness, c, rotation);
      }
    }
  },

  FormatRange(meters) {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)}km` : `${meters.toFixed(0)}m`;
  },

  ProjectToScreen(localDirection, center, surfaceSize) {
    const scale = surfaceSize.y / 0.31; // COCKPIT_FOV_SCALE_Y
    const screenX = center.x + (localDirection.x / -localDirection.z) * scale;
    const screenY = center.y + (-localDirection.y / -localDirection.z) * scale;
    return { x: screenX, y: screenY };
  },

  RotatePoint(point, pivot, angle) {
    const c = Math.cos(angle);
    const s = Math.sin(angle);
    const tx = point.x - pivot.x;
    const ty = point.y - pivot.y;
    return {
      x: tx * c - ty * s + pivot.x,
      y: tx * s + ty * c + pivot.y,
    };
  },
};

// HUDModule constants (HUDModule.cs:131-164)
const HUD = {
  TEXTURE_SQUARE: 'SquareSimple',
  TEXTURE_CIRCLE: 'CircleHollow',
  TEXTURE_TRIANGLE: 'Triangle',
  TEXTURE_CIRCLE_SOLID: 'Circle',
  RADAR_BOX_SIZE_PX: 100,
  RADAR_BORDER_MARGIN: 10,
  STALL_AOA: 28.0,
  STALL_CAUTION_PERCENT: 0.80,
  STALL_WARNING_PERCENT: 0.90,
  STALL_LEVEL_NORMAL: 0,
  STALL_LEVEL_CAUTION: 1,
  STALL_LEVEL_WARNING: 2,
  STALL_LEVEL_STALL: 3,
  TAPE_HEIGHT_PIXELS: 200,
  ALTITUDE_UNITS_PER_TAPE_HEIGHT: 1000,
  PIXELS_PER_ALTITUDE_UNIT: 200 / 1000,
  TICK_INTERVAL: 100,
  MAJOR_TICK_INTERVAL: 500,
  FONT: 'Monospace',
  FONT_W: 'Monospace',
  SPEED_MAJOR_TICK_INTERVAL: 50,
  SPEED_TICK_INTERVAL: 25,
  SPEED_KPH_UNITS_PER_TAPE_HEIGHT: 600,
  COCKPIT_FOV_SCALE_Y: 0.31,
  THROTTLE_HYDROGEN_THRESHOLD: 0.8,
  MIN_Z_FOR_PROJECTION: 0.1,
};

// Expose everything on the global JOS namespace
global.JOS = {
  MFDTheme, HUD_THEMES, HUD_EMPHASIS, HUD_WARNING, HUD_INFO,
  PI, TWO_PI, ToRad, ToDeg, Mn, Mx, Ab, Cl,
  V2, vAdd, vSub, vMul, vLen, vNorm, vDist,
  V3, VZ, v3Add, v3Sub, v3Mul, v3Dot, v3Cross, v3Len, v3LenSq, v3Norm, v3Dist,
  matFromForwardUp, matTranspose, VTN,
  rgba, colorAlpha,
  Frame, SpriteHelpers, HUD,
};

})(typeof window !== 'undefined' ? window : globalThis);
