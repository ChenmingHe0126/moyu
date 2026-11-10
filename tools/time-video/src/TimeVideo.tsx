import React from 'react';
import { AbsoluteFill, Easing, interpolate, interpolateColors, useCurrentFrame } from 'remotion';
import { ACTIVITIES, ACTIVITY_ORDER, ActivityId, PARTS, STAGES_101, buildTiles } from './data';

/* ---------- look ---------- */
const PAPER = '#F7F6F2';
const INK = '#223936';
const MUTED = '#66716D';
const TEAL = '#287F7D';
const TEAL_LIGHT = '#A9CFC9';
const SANS = 'Bahnschrift, "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", sans-serif';
const MONO = '"Cascadia Mono", Consolas, "Microsoft YaHei UI", monospace';

/* ---------- timeline (frames @30fps) ---------- */
const F_GRID = 120;
const F_FILL0 = 135;
const FILL_LEN = 180;
const F_EXCL = 330;
const F_EXCL_OUT = 432;
const F_MORPH = 465;
const MORPH_STAGGER = 45;
const MORPH_LEN = 40;
const F_BARLAB = 515;
const F_INS = 610;
const INS_LEN = 105;
const F_101 = 1040;
const F_END = 1275;
export const DURATION = 1425;

/* ---------- geometry ---------- */
const GX = 120, GY = 250, GP = 30, GS = 26, GCOLS = 28;
const BX = 520, BY = 236, BP = 17, BS = 14, BROWS = 4, BAR_PITCH = 96;

const { tiles, counts, partStart, partTotal } = buildTiles();
const TOTAL_MIN = tiles.length; // 588
const EXCLUDED = PARTS.reduce((s, p) => s + p.excluded, 0); // 34

const clamp = { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' } as const;
const ramp = (f: number, a: number, b: number, ease = Easing.out(Easing.cubic)) =>
  interpolate(f, [a, b], [0, 1], { ...clamp, easing: ease });
const win = (f: number, a: number, b: number, fin = 15, fout = 15) => Math.min(ramp(f, a, a + fin), 1 - ramp(f, b - fout, b));
const lerp = (a: number, b: number, t: number) => a + (b - a) * t;
const pct = (n: number) => `${Math.round((n / TOTAL_MIN) * 100)}%`;

/* ---------- insights ---------- */
type Insight = { act: ActivityId; title: string; detail: string; hlPart?: string };
const INSIGHTS: Insight[] = [
  {
    act: 'B', hlPart: 'topo',
    title: `写代码与自动测试 · ${counts.B} 分钟，占 ${pct(counts.B)}`,
    detail: '其中 110 分钟是拓扑优化（亮出来的那段）。每个游戏写代码只要 21 到 25 分钟，Rebound 和 Inkline 两个加起来也才 21 分钟。',
  },
  {
    act: 'F',
    title: `文档、报告与计时 · ${counts.F} 分钟，${pct(counts.F)}`,
    detail: '每次交付都配图文报告、截图和用时统计。排第二，比在 Rhino 里实测还多。',
  },
  {
    act: 'C', hlPart: 'rounds',
    title: `Rhino 实机测试与修复 · ${counts.C} 分钟，${pct(counts.C)}`,
    detail: '越往后越多：三轮调整占了 40 分钟（亮出来的那段），第四轮在 4K 屏上测 gumball 就用了 18 分钟。',
  },
  {
    act: 'G',
    title: `审查与发布 · ${counts.G} 分钟，${pct(counts.G)}`,
    detail: '1.0.0 上架前，179 个审查和核查代理在后台并行；这里只算主线的时间。',
  },
];
const insWin = (f: number, k: number) => win(f, F_INS + k * INS_LEN, F_INS + (k + 1) * INS_LEN, 12, 12);
const dimAll = (f: number) => win(f, F_INS, F_INS + INSIGHTS.length * INS_LEN, 12, 12);
const bright = (f: number, act: ActivityId) =>
  INSIGHTS.reduce((m, ins, k) => (ins.act === act ? Math.max(m, insWin(f, k)) : m), 0);
const groupOpacity = (f: number, act: ActivityId) => 1 - 0.78 * dimAll(f) * (1 - bright(f, act));

/* ---------- pieces ---------- */
const Header: React.FC<{ f: number }> = ({ f }) => {
  const phases = [
    { a: F_GRID, b: F_MORPH + 10, eyebrow: '1.0.0 · 2026.11.01 – 11.04', title: `${TOTAL_MIN} 分钟，按时间先后排，一格一分钟` },
    { a: F_MORPH, b: F_101 + 5, eyebrow: `同样的 ${TOTAL_MIN} 格`, title: '按干的活重新排' },
    { a: F_101, b: F_END + 5, eyebrow: '1.0.1 · 2026.11.07 – 11.08', title: '1.0.1 又花了 207 分钟' },
  ];
  return (
    <>
      {phases.map((p, i) => {
        const o = win(f, p.a, p.b, 18, 14);
        if (o <= 0) return null;
        const y = (1 - ramp(f, p.a, p.a + 22)) * 18;
        return (
          <div key={i} style={{ position: 'absolute', left: GX, top: 84, opacity: o, transform: `translateY(${y}px)` }}>
            <div style={{ font: `500 24px ${MONO}`, color: TEAL, letterSpacing: '0.06em' }}>{p.eyebrow}</div>
            <div style={{ marginTop: 14, font: `600 56px ${SANS}`, color: INK }}>{p.title}</div>
          </div>
        );
      })}
    </>
  );
};

const TitleCard: React.FC<{ f: number }> = ({ f }) => {
  const o = 1 - ramp(f, 100, 122);
  if (o <= 0) return null;
  const rise = (a: number) => ({ opacity: f < 1 ? 1 : Math.max(0.0001, ramp(f, a, a + 20)), transform: `translateY(${(1 - ramp(f, a, a + 24)) * 14}px)` });
  return (
    <AbsoluteFill style={{ opacity: o, alignItems: 'center', justifyContent: 'center', textAlign: 'center' }}>
      <div style={{ ...rise(-30), font: `500 28px ${MONO}`, color: TEAL, letterSpacing: '0.12em' }}>MOYU · 开发用时</div>
      <div style={{ ...rise(-30), marginTop: 26, font: `600 104px ${SANS}`, color: INK, transform: `scale(${lerp(1.03, 1, ramp(f, 0, 60))})` }}>
        {TOTAL_MIN} 分钟，花在了哪？
      </div>
      <div style={{ ...rise(8), marginTop: 30, font: `400 34px ${SANS}`, color: MUTED }}>
        Moyu 1.0.0 · 2026.11.01 – 11.04 · 数据来自 Claude Code 会话时间戳
      </div>
    </AbsoluteFill>
  );
};

const Tiles: React.FC<{ f: number }> = ({ f }) => {
  if (f < F_FILL0 - 2 || f > F_101 + 25) return null;
  const out = 1 - ramp(f, F_101 - 10, F_101 + 20);
  const dim = dimAll(f);
  return (
    <>
      {tiles.map(t => {
        const appear = F_FILL0 + (t.idx * FILL_LEN) / TOTAL_MIN;
        const a = ramp(f, appear, appear + 8);
        if (a <= 0) return null;
        const d = F_MORPH + (t.idx / TOTAL_MIN) * MORPH_STAGGER;
        const m = ramp(f, d, d + MORPH_LEN, Easing.inOut(Easing.cubic));
        const gx = GX + (t.idx % GCOLS) * GP, gy = GY + Math.floor(t.idx / GCOLS) * GP;
        const g = ACTIVITY_ORDER.indexOf(t.act);
        const bx = BX + Math.floor(t.slot / BROWS) * BP, by = BY + g * BAR_PITCH + (t.slot % BROWS) * BP;
        const size = lerp(GS, BS, m) * lerp(0.5, 1, a);
        const cx = lerp(gx + GS / 2, bx + BS / 2, m), cy = lerp(gy + GS / 2, by + BS / 2, m);
        const part = PARTS[t.part];
        let color = interpolateColors(m, [0, 1], [part.color, ACTIVITIES[t.act].color]);
        // during an insight, the highlighted slice of a bar goes back to its part colour
        INSIGHTS.forEach((ins, k) => {
          if (ins.hlPart && ins.act === t.act && part.id === ins.hlPart) {
            const s = F_INS + k * INS_LEN;
            const h = win(f, s + 22, s + INS_LEN, 14, 12);
            if (h > 0) color = interpolateColors(h, [0, 1], [color, part.color]);
          }
        });
        const op = a * out * (1 - 0.78 * dim * (1 - bright(f, t.act)));
        return (
          <div
            key={t.idx}
            style={{
              position: 'absolute', left: cx - size / 2, top: cy - size / 2, width: size, height: size,
              borderRadius: lerp(5, 3, m), background: color, opacity: op,
            }}
          />
        );
      })}
    </>
  );
};

const Legend: React.FC<{ f: number }> = ({ f }) => {
  const o = 1 - ramp(f, F_MORPH - 12, F_MORPH + 8);
  if (o <= 0 || f < F_FILL0) return null;
  return (
    <div style={{ position: 'absolute', left: 1060, top: 252, width: 660, opacity: o }}>
      {PARTS.map((p, i) => {
        const start = F_FILL0 + (partStart[i] * FILL_LEN) / TOTAL_MIN;
        const ro = ramp(f, start - 2, start + 12);
        const shown = Math.max(0, Math.min(partTotal[i], Math.floor(((f - F_FILL0) * TOTAL_MIN) / FILL_LEN) - partStart[i] + 1));
        return (
          <div key={p.id} style={{ height: 58, display: 'flex', alignItems: 'center', gap: 18, opacity: ro, transform: `translateX(${(1 - ro) * 16}px)` }}>
            <div style={{ width: 24, height: 24, borderRadius: 5, background: p.color, flex: 'none' }} />
            <div style={{ flex: 1, font: `400 30px ${SANS}`, color: INK }}>{p.name}</div>
            <div style={{ font: `600 30px ${SANS}`, color: INK, fontVariantNumeric: 'tabular-nums' }}>{shown} 分</div>
          </div>
        );
      })}
    </div>
  );
};

const Excluded: React.FC<{ f: number }> = ({ f }) => {
  if (f < F_EXCL - 2 || f > F_EXCL_OUT + 45) return null;
  const lab = win(f, F_EXCL, F_EXCL_OUT + 30, 15, 20);
  return (
    <>
      <div style={{ position: 'absolute', left: 1060, top: 742, opacity: lab, font: `400 28px ${SANS}`, color: MUTED }}>
        另有 <b style={{ color: INK }}>{EXCLUDED} 分钟</b>：Rhino 自身或第三方插件故障，不计
      </div>
      {Array.from({ length: EXCLUDED }, (_, j) => {
        const a = ramp(f, F_EXCL + j * 1.3, F_EXCL + j * 1.3 + 10);
        const fall = ramp(f, F_EXCL_OUT + j * 0.6, F_EXCL_OUT + j * 0.6 + 24, Easing.in(Easing.quad));
        if (a <= 0) return null;
        return (
          <div
            key={j}
            style={{
              position: 'absolute', left: 1060 + (j % 17) * GP, top: 796 + Math.floor(j / 17) * GP + fall * 120,
              width: GS, height: GS, borderRadius: 5, opacity: a * (1 - fall), boxSizing: 'border-box',
              border: '1.5px solid #B4BCB8', background: 'repeating-linear-gradient(45deg, #C8CFCB 0 4px, #ECEEEA 4px 8px)',
              transform: `rotate(${fall * (j % 2 ? 18 : -14)}deg)`,
            }}
          />
        );
      })}
    </>
  );
};

const GridFooter: React.FC<{ f: number }> = ({ f }) => {
  const o = win(f, F_FILL0 + 10, F_MORPH + 5, 18, 16);
  if (o <= 0) return null;
  return (
    <div style={{ position: 'absolute', left: GX, top: 950, opacity: o, font: `400 26px ${SANS}`, color: MUTED }}>
      每一格 = Claude 实际干活的 1 分钟；不含你看结果、想下一步的时间，也不含 Codex 画图标的时间。
    </div>
  );
};

const BarLabels: React.FC<{ f: number }> = ({ f }) => {
  if (f < F_BARLAB || f > F_101 + 25) return null;
  const out = 1 - ramp(f, F_101 - 10, F_101 + 20);
  return (
    <>
      {ACTIVITY_ORDER.map((act, g) => {
        const n = counts[act];
        const o = ramp(f, F_BARLAB + g * 6, F_BARLAB + g * 6 + 18) * out * groupOpacity(f, act);
        const vo = ramp(f, F_BARLAB + 30 + g * 6, F_BARLAB + 46 + g * 6) * out * groupOpacity(f, act);
        const shown = Math.round(n * ramp(f, F_BARLAB + 30 + g * 6, F_BARLAB + 70 + g * 6));
        const top = BY + g * BAR_PITCH;
        const end = BX + Math.ceil(n / BROWS) * BP;
        return (
          <React.Fragment key={act}>
            <div style={{ position: 'absolute', left: GX, top: top + 15, width: 370, opacity: o, font: `500 30px ${SANS}`, color: INK, whiteSpace: 'nowrap' }}>
              <span style={{ display: 'inline-block', width: 14, height: 14, borderRadius: 3, background: ACTIVITIES[act].color, marginRight: 14, verticalAlign: 3 }} />
              {ACTIVITIES[act].name}
            </div>
            <div style={{ position: 'absolute', left: end + 16, top: top + 15, opacity: vo, font: `600 30px ${SANS}`, color: INK, fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
              {shown} 分 <span style={{ fontWeight: 400, color: MUTED }}>· {pct(n)}</span>
            </div>
          </React.Fragment>
        );
      })}
    </>
  );
};

const Insights: React.FC<{ f: number }> = ({ f }) => (
  <>
    {INSIGHTS.map((ins, k) => {
      const o = insWin(f, k);
      if (o <= 0) return null;
      const y = (1 - ramp(f, F_INS + k * INS_LEN, F_INS + k * INS_LEN + 18)) * 14;
      return (
        <div key={k} style={{ position: 'absolute', left: GX, top: 930, width: 1680, opacity: o, transform: `translateY(${y}px)` }}>
          <div style={{ font: `600 36px ${SANS}`, color: ACTIVITIES[ins.act].color }}>{ins.title}</div>
          <div style={{ marginTop: 10, font: `400 28px ${SANS}`, color: MUTED, lineHeight: 1.45 }}>{ins.detail}</div>
        </div>
      );
    })}
  </>
);

const Scene101: React.FC<{ f: number }> = ({ f }) => {
  const o = win(f, F_101, F_END + 5, 18, 16);
  if (o <= 0) return null;
  const total = 207.0, primary = 73.3, back = total - primary;
  const W = 1680, sx = W / total;
  const gp = ramp(f, F_101 + 18, F_101 + 48), gb = ramp(f, F_101 + 44, F_101 + 80);
  const STAGE_X = 780, STAGE_W = 860, smax = STAGES_101[0].total;
  return (
    <div style={{ position: 'absolute', inset: 0, opacity: o }}>
      <div style={{ position: 'absolute', left: GX, top: 262, display: 'flex', height: 64 }}>
        <div style={{ width: primary * sx * gp, background: TEAL, borderRadius: '6px 0 0 6px' }} />
        <div style={{ width: back * sx * gb, borderRadius: '0 6px 6px 0', background: `repeating-linear-gradient(45deg, ${TEAL_LIGHT} 0 10px, #C3DFDA 10px 20px)` }} />
      </div>
      <div style={{ position: 'absolute', left: GX, top: 344, opacity: ramp(f, F_101 + 36, F_101 + 54), font: `600 30px ${SANS}`, color: TEAL }}>
        主代理轮次 73 分钟
      </div>
      <div style={{ position: 'absolute', left: GX + primary * sx + 18, top: 344, opacity: ramp(f, F_101 + 70, F_101 + 88), font: `400 30px ${SANS}`, color: MUTED }}>
        <b style={{ color: INK, fontWeight: 600 }}>后台审查与修复任务 134 分钟</b> · 并行的只算一次
      </div>
      {STAGES_101.map((s, i) => {
        const a = F_101 + 96 + i * 10;
        const ro = ramp(f, a, a + 16);
        const gw = ramp(f, a + 4, a + 30);
        const top = 460 + i * 78;
        return (
          <React.Fragment key={s.name}>
            <div style={{ position: 'absolute', left: GX, top: top + 4, width: 640, opacity: ro, font: `400 30px ${SANS}`, color: INK, whiteSpace: 'nowrap' }}>{s.name}</div>
            <div style={{ position: 'absolute', left: STAGE_X, top: top + 6, height: 34, display: 'flex', opacity: ro }}>
              <div style={{ width: (s.primary / smax) * STAGE_W * gw, background: TEAL, borderRadius: 4 }} />
              <div style={{ width: ((s.total - s.primary) / smax) * STAGE_W * gw, background: TEAL_LIGHT, borderRadius: '0 4px 4px 0' }} />
            </div>
            <div style={{ position: 'absolute', left: STAGE_X + (s.total / smax) * STAGE_W * gw + 16, top: top + 2, opacity: ramp(f, a + 20, a + 34), font: `600 30px ${SANS}`, color: INK, fontVariantNumeric: 'tabular-nums' }}>
              {Math.round(s.total)}
            </div>
          </React.Fragment>
        );
      })}
      <div style={{ position: 'absolute', left: GX, top: 920, width: 1680, opacity: ramp(f, F_101 + 150, F_101 + 170), font: `400 26px ${SANS}`, color: MUTED, lineHeight: 1.5 }}>
        计法和 1.0.0 不同：这次把后台的审查、修复和图标任务也算进来，重叠的时间只算一次。笔记本上更早的按键修复没有记录，没有计入。
      </div>
    </div>
  );
};

const EndCard: React.FC<{ f: number }> = ({ f }) => {
  if (f < F_END) return null;
  const r = (a: number) => ({ opacity: ramp(f, F_END + a, F_END + a + 20), transform: `translateY(${(1 - ramp(f, F_END + a, F_END + a + 24)) * 16}px)` });
  return (
    <AbsoluteFill style={{ alignItems: 'center', justifyContent: 'center', textAlign: 'center' }}>
      <div style={{ ...r(6), font: `600 112px ${SANS}`, color: INK, fontVariantNumeric: 'tabular-nums' }}>588 + 207 分钟</div>
      <div style={{ ...r(16), marginTop: 18, font: `600 50px ${SANS}`, color: TEAL }}>约 13 小时 15 分</div>
      <div style={{ ...r(26), marginTop: 26, font: `400 30px ${SANS}`, color: MUTED }}>
        1.0.0 只算主线轮次 · 1.0.1 含后台任务 · Rhino 自身故障 34 分钟不计
      </div>
      <div style={{ ...r(44), marginTop: 110, font: `400 32px ${SANS}`, color: INK }}>Moyu · 代码、测试和文档几乎全部由 Claude Code 编写</div>
      <div style={{ ...r(52), marginTop: 16, font: `500 28px ${MONO}`, color: TEAL }}>github.com/ChenmingHe0126/moyu</div>
    </AbsoluteFill>
  );
};

export const TimeVideo: React.FC = () => {
  const f = useCurrentFrame();
  return (
    <AbsoluteFill style={{ background: PAPER, fontFamily: SANS, color: INK }}>
      <TitleCard f={f} />
      <Header f={f} />
      <Legend f={f} />
      <Excluded f={f} />
      <GridFooter f={f} />
      <Tiles f={f} />
      <BarLabels f={f} />
      <Insights f={f} />
      <Scene101 f={f} />
      <EndCard f={f} />
    </AbsoluteFill>
  );
};
