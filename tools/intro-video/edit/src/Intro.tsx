import React from 'react';
import { AbsoluteFill, Audio, Easing, Img, Sequence, interpolate, spring, staticFile, useCurrentFrame, useVideoConfig } from 'remotion';
import { B, CLIPS, F, TOTAL_BEATS } from './timeline';
import { Lang, LangContext, useLang, useT } from './lang';
import { CAPTION_Y, CN, CORAL, Caption, Chips, Clip, INK, Keycap, LAT, MUTED, PanelTag, Paper, Rich, SAFE_W, SAFE_X, SpeedBadge, TEAL, ramp, usePop } from './ui';

const clamp = { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' } as const;

/** A scene between two beats. */
const Scene: React.FC<{ from: number; to: number; children: React.ReactNode }> = ({ from, to, children }) => (
  <Sequence from={F(from)} durationInFrames={F(to) - F(from)}>{children}</Sequence>
);

/* ---------------------------------------------------------------- 1. hook: Rebound → tower elevation */
const Hook: React.FC = () => {
  const t = useT();
  const boss = F(B.hookBoss) - F(B.hook);
  return (
    <AbsoluteFill>
      <Clip file={CLIPS.rebound.file} from={CLIPS.rebound.boss - boss} zoom={[1, 1.06]} zoomStart={boss} zoomEnd={boss + 170} origin="46% 34%" />
      <Sequence durationInFrames={boss}>
        <Caption line1={t('上班时间', 'Work hours.')} line2={t('我在 Rhino 里‹打砖块›', 'Playing brick breaker in ‹Rhino›')} delay={22} />
      </Sequence>
      <Sequence from={boss}>
        <Caption line1={t('老板来了？', 'Boss walking by?')} line2={t('‹一帧›变成幕墙立面', '‹One frame›: a façade elevation')} />
      </Sequence>
      <Keycap press={boss} hint={t('老板键 · 1 左边', 'Boss key · left of 1')} />
    </AbsoluteFill>
  );
};

/* ---------------------------------------------------------------- 2. title */
const Title: React.FC = () => {
  const t = useT();
  const en = useLang() === 'en';
  const a = usePop(2), b = usePop(12), c = usePop(22);
  const boss = F(B.hookBoss) - F(B.hook), len = F(B.title) - F(B.hook);
  return (
    <AbsoluteFill>
      <Clip file={CLIPS.rebound.file} from={CLIPS.rebound.boss - boss + len} style={{ filter: 'blur(18px)' }} zoom={[1.12, 1.16]} />
      <AbsoluteFill style={{ background: 'rgba(250,247,242,0.80)' }} />
      <div style={{ position: 'absolute', left: 0, top: 500, width: 1080, textAlign: 'center', fontFamily: CN, color: INK }}>
        <Img src={staticFile('stills/icon-512.png')} style={{ width: 230, height: 230, transform: `scale(${a})`, filter: 'drop-shadow(0 18px 30px rgba(30,42,50,0.22))' }} />
        <div style={{ fontSize: 168, fontWeight: 700, letterSpacing: 4, marginTop: 34, transform: `scale(${0.9 + 0.1 * a})`, opacity: a }}>
          <span style={{ fontFamily: LAT }}>Moyu</span> 摸鱼
        </div>
        {en ? (
          <div style={{ fontSize: 58, fontWeight: 500, lineHeight: 1.3, marginTop: 26, opacity: b, transform: `translateY(${(1 - b) * 24}px)`, fontFamily: LAT }}>
            half <span style={{ color: TEAL }}>topology optimisation</span><br />half <span style={{ color: CORAL }}>slacking off</span>
            <div style={{ fontSize: 36, color: MUTED, marginTop: 14 }}>(摸鱼 mōyú: slacking off at work)</div>
          </div>
        ) : (
          <div style={{ fontSize: 62, fontWeight: 500, marginTop: 26, opacity: b, transform: `translateY(${(1 - b) * 24}px)` }}>
            一半<span style={{ color: TEAL }}>拓扑优化</span> · 一半<span style={{ color: CORAL }}>摸鱼</span>
          </div>
        )}
        <div style={{ fontSize: 40, color: MUTED, marginTop: 44, opacity: c, fontFamily: LAT }}>
          {t('Grasshopper 插件 · Rhino 8 Windows · 免费开源', 'Grasshopper plugin · Rhino 8 · Windows · free')}
        </div>
      </div>
    </AbsoluteFill>
  );
};

/* ---------------------------------------------------------------- 3. the ribbon: four respectable panels */
const PANELS = [
  { panel: '3 | Massing', game: '叠楼', latin: 'VoxStack' },
  { panel: '4 | Landscape', game: '蜿蜒', latin: 'Serpentine' },
  { panel: '5 | Facade', game: '回弹', latin: 'Rebound' },
  { panel: '6 | Section', game: '雨燕', latin: 'Swift' },
];

const Ribbon: React.FC = () => {
  const t = useT();
  const en = useLang() === 'en';
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  // The ribbon capture is 2576 x 421; the Moyu panels take x 0–990, y 0–320. Start on all seven, then push in on the
  // four game panels (x 460–990).
  const push = ramp(frame, 26, 58, Easing.inOut(Easing.cubic));
  const scale = interpolate(push, [0, 1], [0.99, 1.84]);
  const x = interpolate(push, [0, 1], [40, 52 - 462 * 1.84]);
  const card = usePop(0);
  return (
    <Paper>
      <div style={{ position: 'absolute', left: SAFE_X, top: 250, fontFamily: LAT, fontSize: 40, color: MUTED, opacity: card }}>Grasshopper › Moyu</div>
      <div style={{
        position: 'absolute', left: 40, top: 330, width: 1000, height: 330, borderRadius: 26, overflow: 'hidden', background: '#e9e9e9',
        boxShadow: '0 24px 60px rgba(30,42,50,0.20)', transform: `scale(${0.96 + 0.04 * card})`, opacity: card,
      }}>
        <Img src={staticFile('stills/gh_ribbon_moyu.png')} style={{ position: 'absolute', left: x - 40, top: interpolate(push, [0, 1], [10, -18]), width: 2576 * scale, height: 421 * scale, maxWidth: 'none' }} />
      </div>
      <div style={{ position: 'absolute', left: SAFE_X, top: 760, width: SAFE_W, fontFamily: CN }}>
        {PANELS.map((p, i) => {
          const pop = spring({ frame: frame - 40 - i * 13, fps, config: { damping: 14, stiffness: 190, mass: 0.6 } });
          return (
            <div key={p.panel} style={{ display: 'flex', alignItems: 'center', gap: 22, marginBottom: 30, opacity: Math.min(1, pop * 1.5), transform: `translateX(${(1 - pop) * -40}px)` }}>
              <div style={{ width: 330, background: '#1b1b1b', color: '#fff', borderRadius: 12, padding: '14px 0', textAlign: 'center', fontFamily: LAT, fontSize: 44 }}>{p.panel}</div>
              <div style={{ fontSize: 48, color: MUTED, fontWeight: 700 }}>→</div>
              {en ? <div style={{ fontSize: 58, fontWeight: 700, color: INK, fontFamily: LAT }}>{p.latin}</div>
                : <div style={{ fontSize: 60, fontWeight: 700, color: INK }}>{p.game}<span style={{ fontFamily: LAT, fontSize: 40, fontWeight: 400, color: MUTED, marginLeft: 18 }}>{p.latin}</span></div>}
            </div>
          );
        })}
      </div>
      <Caption line1={t('四个面板', 'Four panels')} line2={t('名字都很‹正经›', 'with very ‹serious› names')} delay={6} />
    </Paper>
  );
};

/* ---------------------------------------------------------------- 4. the games */
/** VoxStack close up, play only (its boss key was shown in the real Rhino window just before). */
const Vox: React.FC = () => {
  const t = useT();
  const len = F(B.serp) - F(B.vox);
  return (
    <AbsoluteFill>
      <Clip file={CLIPS.vox.file} from={CLIPS.vox.boss - len} />
      <PanelTag panel={t('Massing 面板', 'Massing panel')} game={t('VoxStack 叠楼', 'VoxStack')} delay={4} />
      <Caption line1={t('方块能‹自己建模›', 'Model ‹your own› blocks')} line2={t('Gumball 转一下，一次消两层', 'a gumball turn, two floors gone')} delay={2} />
    </AbsoluteFill>
  );
};

/* ---------------------------------------------------------------- the real thing: Rhino and Grasshopper windows */
const S = 1080 / 1920;

/** A band of the recorded window frame (Rhino above Grasshopper, 1920x2480), as a card across the phone frame. */
const WindowCard: React.FC<{ from: number; srcY: number; top: number; height: number }> = ({ from, srcY, top, height }) => (
  <div style={{ position: 'absolute', left: 0, top, width: 1080, height, overflow: 'hidden', background: '#202020', boxShadow: '0 18px 44px rgba(20,28,34,0.28)' }}>
    <div style={{ position: 'absolute', left: 0, top: -srcY * S, width: 1080, height: 2480 * S }}>
      <Clip file={CLIPS.rhino.file} from={from} />
    </div>
  </div>
);

const Cursor: React.FC<{ x: number; y: number; down: boolean }> = ({ x, y, down }) => (
  <svg width="44" height="56" viewBox="0 0 22 28" style={{ position: 'absolute', left: x - 3, top: y - 2, transform: `scale(${down ? 0.88 : 1})`, transformOrigin: '3px 2px', filter: 'drop-shadow(0 3px 4px rgba(0,0,0,0.45))' }}>
    <path d="M2 2 L2 22 L7 17.5 L10.5 25.5 L14 24 L10.6 16.2 L17.5 16.2 Z" fill="#fff" stroke="#111" strokeWidth="1.4" strokeLinejoin="round" />
  </svg>
);

/** A coral frame round a detail of the window cards, with a small label above it. */
const Callout: React.FC<{ x: number; y: number; w: number; h: number; label: string; delay?: number; labelLeft?: boolean; labelRight?: boolean }> = ({ x, y, w, h, label, delay = 0, labelLeft, labelRight }) => {
  const pop = usePop(delay);
  return (
    <>
      <div style={{ position: 'absolute', left: x, top: y, width: w, height: h, border: `5px solid ${CORAL}`, borderRadius: 10, opacity: pop, transform: `scale(${1.15 - 0.15 * pop})`, boxShadow: '0 0 0 4px rgba(255,255,255,0.55)' }} />
      <div style={{ position: 'absolute', left: labelRight ? x + w + 14 : labelLeft ? x + w : x, top: labelRight ? y + h / 2 - 26 : y - 52, transform: `translateX(${labelLeft ? '-100%' : '0'}) scale(${pop})`, transformOrigin: labelLeft ? '100% 100%' : '0% 50%', background: CORAL, color: '#fff', borderRadius: 10, padding: '4px 16px', fontFamily: CN, fontSize: 32, fontWeight: 700, whiteSpace: 'nowrap' }}>{label}</div>
    </>
  );
};

const RhinoScene: React.FC = () => {
  const t = useT();
  const frame = useCurrentFrame();
  const partB = F(B.rhinoPlay) - F(B.rhino), at = F(B.rhinoBoss) - F(B.rhino);
  const fromB = CLIPS.rhino.boss - (at - partB);
  // Rhino (title bar cropped) and the Grasshopper canvas, stacked.
  const A = { srcY: 48, top: 168, height: 580 }, G = { srcY: 1678, top: 770, height: 378 };
  const cards = (from: number) => (
    <>
      <WindowCard from={from} {...A} />
      <WindowCard from={from} {...G} />
    </>
  );
  // The pointer glides to the component's Run button and clicks it on the frame the recording pressed it.
  const run = { x: 1188 * S, y: G.top + (2191 - G.srcY) * S };
  const glide = ramp(frame, 8, 50, Easing.inOut(Easing.cubic));
  const cx = interpolate(glide, [0, 1], [930, run.x]), cy = interpolate(glide, [0, 1], [1230, run.y]);
  const click = CLIPS.rhino.click, ring = ramp(frame, click, click + 24);
  const away = ramp(frame, click + 20, click + 46);
  return (
    <Paper>
      <Chips items={[t('实机画面 · Rhino 8 + Grasshopper', 'Real footage · Rhino 8 + Grasshopper')]} y={92} size={34} dark />
      <Sequence durationInFrames={partB}>{cards(0)}</Sequence>
      <Sequence from={partB}>{cards(fromB)}</Sequence>
      {frame < click + 46 ? (
        <>
          {frame >= click ? <div style={{ position: 'absolute', left: run.x, top: run.y, width: 0, height: 0, borderRadius: '50%', boxShadow: `0 0 0 ${ring * 70}px rgba(240,118,92,${0.6 * (1 - ring)})` }} /> : null}
          <div style={{ opacity: 1 - away }}><Cursor x={cx} y={cy} down={frame >= click - 2 && frame < click + 8} /></div>
        </>
      ) : null}
      <Sequence durationInFrames={at}>
        <Caption line1={t('真的在 ‹Rhino› 里', "Yes, it's really ‹Rhino›")} line2={t('Grasshopper 里点 ▶ Run 就开玩', 'hit ▶ Run in Grasshopper and play')} delay={4} />
      </Sequence>
      <Sequence from={at}>
        <Callout x={8} y={A.top + (86 - A.srcY) * S - 8} w={400} h={40} label={t('命令行', 'Command line')} delay={4} labelRight />
        <Callout x={1500 * S - 8} y={G.top + (1934 - G.srcY) * S - 6} w={204} h={156} label={t('电池输出：Option B', 'GH output: Option B')} delay={12} labelLeft />
        <Caption line1={t('老板键一按', 'Boss key: the viewport,')} line2={t('视口、命令行、电池‹一起›伪装', 'command line and GH ‹play along›')} />
      </Sequence>
      <Keycap press={at} x={760} y={1110} />
    </Paper>
  );
};

/** Serpentine: the whole torus (any surface), then first person on the rolling terrain into the boss key. */
const Serp: React.FC = () => {
  const t = useT();
  const cut = F(B.serpTerrain) - F(B.serp), at = F(B.serpBoss) - F(B.serp);
  return (
    <AbsoluteFill>
      <Sequence durationInFrames={cut}>
        <Clip file={CLIPS.torus.file} from={CLIPS.torus.from} />
      </Sequence>
      <Sequence from={cut}>
        <Clip file={CLIPS.terrain.file} from={CLIPS.terrain.boss - (at - cut)} zoom={[1, 1.05]} zoomStart={at - cut} zoomEnd={at - cut + 150} />
      </Sequence>
      <Sequence durationInFrames={at}>
        <PanelTag panel={t('Landscape 面板', 'Landscape panel')} game={t('Serpentine 蜿蜒', 'Serpentine')} delay={4} />
      </Sequence>
      <Sequence durationInFrames={cut}>
        <Caption line1={t('什么曲面‹都能爬›', 'Crawls on ‹any› surface')} line2={t('圆环、管子、地形、球面', 'torus, tube, terrain, sphere')} delay={2} />
      </Sequence>
      <Sequence from={cut} durationInFrames={at - cut}>
        <Caption line1={t('第一人称视角', 'First-person mode')} line2={t('连吃有连击，最高 ×5', 'fruit combos up to ×5')} />
      </Sequence>
      <Sequence from={at}>
        <Caption line1={t('‹一帧›变成 景观流线图', '‹One frame› later')} line2={t('', "it's a landscape plan") || undefined} />
      </Sequence>
      <Keycap press={at} />
    </AbsoluteFill>
  );
};

/** Swift: play → section drawing → (a cut inside the hold) → the key again → the real 3-2-1 → flying. */
const Swift: React.FC = () => {
  const t = useT();
  const at = F(B.swiftBoss) - F(B.swift), back = F(B.swiftBack) - F(B.swift), key = F(B.swiftKey) - F(B.swift);
  return (
    <AbsoluteFill>
      <Sequence durationInFrames={back}>
        <Clip file={CLIPS.swift.file} from={CLIPS.swift.boss - at} zoom={[1, 1.05]} zoomStart={at} zoomEnd={at + 150} />
      </Sequence>
      <Sequence from={back}>
        <Clip file={CLIPS.swift.file} from={CLIPS.swift.back - (key - back)} />
      </Sequence>
      <Sequence durationInFrames={at}>
        <PanelTag panel={t('Section 面板', 'Section panel')} game={t('Swift 雨燕', 'Swift')} delay={4} />
        <Caption line1={t('只用一个键：‹空格›', 'One key: ‹Space›')} line2={t('扇翅膀，穿过柱廊', 'flap through the colonnade')} delay={2} />
      </Sequence>
      <Sequence from={at} durationInFrames={back - at}>
        <Caption line1={t('‹一帧›变成 采光通风剖面', '‹One frame› later')} line2={t('', "it's a daylight section") || undefined} />
      </Sequence>
      <Sequence from={back}>
        <Chips items={[t('Alt+Tab 切走，也会自动伪装', 'Alt+Tab hides it too'), t('默认静音', 'Muted by default')]} y={CAPTION_Y - 92} delay={key - back + 70} step={26} />
        <Caption line1={t('老板走了？‹再按一次›', 'Boss gone? ‹Press again›')} line2={t('3-2-1，回到游戏', '3-2-1, back to the game')} />
      </Sequence>
      <Keycap press={at} />
      <Keycap press={key} hint={t('再按一次', 'Again')} />
    </AbsoluteFill>
  );
};

/* ---------------------------------------------------------------- 5. the serious half */
const Topo: React.FC<{ clip: { file: string; from: number; rate: number; speed: number }; line1: string; line2: string; chips?: string[] }> = ({ clip, line1, line2, chips }) => {
  const t = useT();
  return (
  <AbsoluteFill style={{ background: 'linear-gradient(#F8F9FC, #F1F2F7 50%, #E2E7ED)' }}>
    <Clip file={clip.file} from={clip.from} rate={clip.rate} style={{ transform: 'scale(0.9)', transformOrigin: '50% 4%' }} />
    <SpeedBadge text={`×${Math.round(clip.rate * clip.speed)} ${t('倍速', 'speed')}`} />
    {chips ? <Chips items={chips} y={160} delay={50} step={18} dark /> : null}
    <Caption line1={line1} line2={line2} delay={2} />
  </AbsoluteFill>
  );
};

/* ---------------------------------------------------------------- 6. written by an agent */
const Ai: React.FC = () => {
  const t = useT();
  const en = useLang() === 'en';
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const head = usePop(2), time = usePop(64);
  const minutes = Math.round(interpolate(frame, [64, 104], [0, 588], { ...clamp, easing: Easing.out(Easing.cubic) }));
  return (
    <Paper>
      <div style={{ position: 'absolute', left: SAFE_X, top: 520, width: SAFE_W + 60, fontFamily: CN, color: INK }}>
        <div style={{ fontSize: 46, color: MUTED, fontFamily: LAT, opacity: head }}>{t('说出来你可能不信', 'You may not believe this')}</div>
        <div style={{ fontSize: 116, fontWeight: 700, lineHeight: 1.2, marginTop: 20, opacity: head, transform: `translateY(${(1 - head) * 30}px)` }}>
          {en ? <span style={{ fontFamily: LAT }}>The code?<br />Mostly <span style={{ color: CORAL }}>AI</span>.</span> : <>代码几乎全是<br /><span style={{ color: CORAL }}>AI 写的</span></>}
        </div>
        <div style={{ display: 'flex', gap: 22, marginTop: 56 }}>
          {[t('代码', 'Code'), t('测试', 'Tests'), t('文档', 'Docs')].map((label, i) => {
            const p = spring({ frame: frame - 26 - i * 11, fps, config: { damping: 13, stiffness: 200, mass: 0.6 } });
            return (
              <div key={label} style={{ transform: `scale(${p})`, display: 'flex', alignItems: 'center', gap: 14, background: '#fff', borderRadius: 20, padding: '16px 30px', fontSize: 50, fontWeight: 500, boxShadow: '0 10px 26px rgba(30,42,50,0.12)' }}>
                <svg width="46" height="46" viewBox="0 0 46 46"><circle cx="23" cy="23" r="23" fill={TEAL} /><path d="M12 24 l8 8 l15 -17" stroke="#fff" strokeWidth="5" fill="none" strokeLinecap="round" strokeLinejoin="round" /></svg>
                {label}
              </div>
            );
          })}
        </div>
        <div style={{ marginTop: 90, opacity: time, transform: `translateY(${(1 - time) * 24}px)` }}>
          <div style={{ fontSize: 44, color: MUTED }}>{t('1.0.0 版 · Claude Code 开发用时', 'v1.0.0 · time Claude Code spent on it')}</div>
          <div style={{ fontFamily: LAT, fontSize: 150, fontWeight: 700, marginTop: 6, letterSpacing: 2 }}>
            {Math.floor(minutes / 60)}<span style={{ fontSize: 64, fontFamily: CN, fontWeight: 500 }}>{t(' 小时 ', ' h ')}</span>{String(minutes % 60).padStart(2, '0')}<span style={{ fontSize: 64, fontFamily: CN, fontWeight: 500 }}>{t(' 分', ' min')}</span>
          </div>
          <div style={{ fontSize: 40, color: MUTED, marginTop: 10, opacity: ramp(frame, 110, 130) }}>{t('这期间我主要负责提需求，和摸鱼', 'I wrote the requests. And slacked off.')}</div>
        </div>
      </div>
    </Paper>
  );
};

/* ---------------------------------------------------------------- 7. install */
const Card: React.FC<{ file: string; w: number; h: number; top: number }> = ({ file, w, h, top }) => {
  const pop = usePop(0);
  return (
    <div style={{
      position: 'absolute', left: (1080 - w) / 2, top, width: w, height: h, borderRadius: 30, overflow: 'hidden', background: '#222',
      boxShadow: '0 30px 70px rgba(30,42,50,0.28)', transform: `translateY(${(1 - pop) * 60}px) scale(${0.95 + 0.05 * pop})`, opacity: Math.min(1, pop * 1.6),
    }}>
      <Clip file={file} from={20} />
    </div>
  );
};

const Install: React.FC = () => {
  const t = useT();
  const swap = F(B.examples) - F(B.install);
  return (
    <Paper>
      <Sequence durationInFrames={swap}>
        <Chips items={['Rhino 8', 'Windows', t('免费', 'Free')]} y={300} delay={8} size={44} dark />
        <Card file={CLIPS.pm.file} w={1000} h={764} top={450} />
        <Caption line1={t('PackageManager', 'Package Manager')} line2={t('搜 ‹moyu›，点 Install', 'search ‹moyu›, hit Install')} delay={4} />
      </Sequence>
      <Sequence from={swap}>
        <Chips items={[t('装好之后', 'Then')]} y={300} delay={4} size={44} dark />
        <Card file={CLIPS.examples.file} w={960} h={837} top={420} />
        <Caption line1={t('Examples 电池', 'Examples component')} line2={t('一键打开‹现成示例›', 'opens a ‹ready-made› example')} delay={4} />
      </Sequence>
    </Paper>
  );
};

/* ---------------------------------------------------------------- 8. end card */
const End: React.FC = () => {
  const t = useT();
  const en = useLang() === 'en';
  const a = usePop(2), b = usePop(14), c = usePop(30);
  const key = <span style={{ fontFamily: '"Cascadia Mono", Consolas, monospace', fontWeight: 700, color: INK, background: '#fff', border: '2px solid #D9D3CB', borderRadius: 10, padding: '0 14px' }}>`</span>;
  return (
    <Paper>
      <div style={{ position: 'absolute', left: 0, top: 470, width: 1080, textAlign: 'center', fontFamily: CN, color: INK }}>
        <Img src={staticFile('stills/icon-512.png')} style={{ width: 200, height: 200, transform: `scale(${a})`, filter: 'drop-shadow(0 18px 30px rgba(30,42,50,0.22))' }} />
        <div style={{ fontSize: 120, fontWeight: 700, marginTop: 30, opacity: a }}><span style={{ fontFamily: LAT }}>Moyu</span> 摸鱼</div>
        <div style={{ fontSize: 76, fontWeight: 700, marginTop: 60, opacity: b, transform: `translateY(${(1 - b) * 24}px)`, fontFamily: en ? LAT : CN }}>
          {en ? <>Happy <span style={{ color: CORAL }}>slacking</span></> : <>祝<span style={{ color: CORAL }}>摸鱼</span>愉快</>}
        </div>
        <div style={{ fontSize: 44, color: MUTED, marginTop: 18, opacity: b, fontFamily: en ? LAT : CN }}>
          {en ? <>Boss walking by? Press {key}</> : <>老板路过？按 {key} 就好</>}
        </div>
        <div style={{ display: 'inline-block', marginTop: 56, opacity: c, transform: `scale(${0.94 + 0.06 * c})`, background: INK, color: '#fff', borderRadius: 999, padding: '20px 54px', fontSize: 50, fontWeight: 500 }}>
          {t('PackageManager 搜 ', 'Package Manager: search ')}<span style={{ fontFamily: LAT, color: '#FFD0C2', fontWeight: 700 }}>moyu</span>
        </div>
        <div style={{ fontSize: 36, color: MUTED, marginTop: 44, opacity: c, fontFamily: LAT }}>{t('免费开源 · food4rhino · GitHub', 'Free & open source · food4rhino · GitHub')}</div>
        <div style={{ fontSize: 24, color: '#9AA3A8', marginTop: 150, opacity: c, fontFamily: LAT }}>Music: “Pamgaea” Kevin MacLeod (incompetech.com) · CC BY 4.0</div>
      </div>
    </Paper>
  );
};

/** The first second: the picture comes up out of the paper colour instead of starting cold. */
const FadeIn: React.FC = () => {
  const frame = useCurrentFrame();
  const o = interpolate(frame, [0, 42], [1, 0], { ...clamp, easing: Easing.inOut(Easing.quad) });
  return o > 0 ? <AbsoluteFill style={{ background: PAPER_FADE, opacity: o }} /> : null;
};
const PAPER_FADE = '#F5F1EA';

/* ---------------------------------------------------------------- the cut */
export const Intro: React.FC<{ lang: Lang }> = ({ lang }) => (
  <LangContext.Provider value={lang}>
    <Cut />
  </LangContext.Provider>
);

const Cut: React.FC = () => {
  const t = useT();
  return (
  <AbsoluteFill style={{ background: '#000' }}>
    <Audio src={staticFile('audio/bgm.wav')} />
    <Scene from={B.hook} to={B.title}><Hook /></Scene>
    <Scene from={B.title} to={B.ribbon}><Title /></Scene>
    <Scene from={B.ribbon} to={B.rhino}><Ribbon /></Scene>
    <Scene from={B.rhino} to={B.vox}><RhinoScene /></Scene>
    <Scene from={B.vox} to={B.serp}><Vox /></Scene>
    <Scene from={B.serp} to={B.swift}><Serp /></Scene>
    <Scene from={B.swift} to={B.topo2d}><Swift /></Scene>
    <Scene from={B.topo2d} to={B.topo3d}>
      <Topo clip={CLIPS.topo2d} line1={t('老板真来检查？', 'Boss checking for real?')} line2={t('那就真算个‹拓扑优化›', 'Fine. ‹Topology optimisation›.')} chips={[t('画边界', 'Draw a boundary'), t('加荷载', 'Add loads'), t('加支座', 'Add supports')]} />
    </Scene>
    <Scene from={B.topo3d} to={B.ai}>
      <Topo clip={CLIPS.topo3d} line1={t('‹3D› 体素也行', '‹3D› voxels too')} line2={t('收敛后直接出光滑网格', 'converges into a smooth mesh')} />
    </Scene>
    <Scene from={B.ai} to={B.install}><Ai /></Scene>
    <Scene from={B.install} to={B.end}><Install /></Scene>
    <Scene from={B.end} to={TOTAL_BEATS}><End /></Scene>
    <FadeIn />
  </AbsoluteFill>
  );
};
