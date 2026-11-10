import React from 'react';
import { AbsoluteFill, Easing, OffthreadVideo, interpolate, spring, staticFile, useCurrentFrame, useVideoConfig } from 'remotion';
import assets from './assets.json';
import { useLang, useT } from './lang';

/* ---------- look ---------- */
export const PAPER = '#F5F1EA';
export const INK = '#1E2A32';
export const CORAL = '#F0765C';
export const TEAL = '#2E8C86';
export const MUTED = '#6B777E';
export const CN = '"HarmonyOS Sans SC", "Microsoft YaHei UI", "Microsoft YaHei", sans-serif';
export const LAT = 'Bahnschrift, "HarmonyOS Sans SC", "Microsoft YaHei UI", sans-serif';

/** Left edge and width every text block keeps to: the right 12 % belongs to the phone app's buttons. */
export const SAFE_X = 60;
export const SAFE_W = 880;
/** Captions sit at 67–78 % of the height: below most of the action, above the title and description that Douyin and
 *  Xiaohongshu lay over the bottom fifth of a vertical video. */
export const CAPTION_Y = 1290;

const clamp = { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' } as const;
export const ramp = (f: number, a: number, b: number, ease: (t: number) => number = Easing.out(Easing.cubic)) =>
  interpolate(f, [a, b], [0, 1], { ...clamp, easing: ease });

/** 0 → 1 with a little overshoot: how every label arrives. */
export const usePop = (delay = 0) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  return spring({ frame: frame - delay, fps, config: { damping: 14, stiffness: 190, mass: 0.6 } });
};

/* ---------- footage ---------- */
type ClipProps = {
  file: string;
  /** Source frame (60 fps) shown on this sequence's first frame. */
  from: number;
  rate?: number;
  /** Scale at the sequence's first frame and at `zoomEnd` (frames). */
  zoom?: [number, number];
  zoomStart?: number;
  zoomEnd?: number;
  origin?: string;
  style?: React.CSSProperties;
};

export const Clip: React.FC<ClipProps> = ({ file: name, from, rate = 1, zoom, zoomStart = 0, zoomEnd = 150, origin = '50% 42%', style }) => {
  const frame = useCurrentFrame();
  // The English cut uses footage recorded with the games' English HUD where it exists (name_en.mp4).
  const english = name.replace(/\.mp4$/, '_en.mp4');
  const file = useLang() === 'en' && (assets as string[]).includes(english) ? english : name;
  const scale = zoom ? interpolate(frame, [zoomStart, zoomEnd], zoom, clamp) : 1;
  if (!(assets as string[]).includes(file))
    return (
      <AbsoluteFill style={{ background: 'linear-gradient(#EADFD8, #D9CFE0)', alignItems: 'center', justifyContent: 'center', fontFamily: LAT, fontSize: 44, color: INK }}>
        {file} @ {from + Math.round(frame * rate)}
      </AbsoluteFill>
    );
  return (
    <AbsoluteFill style={{ overflow: 'hidden' }}>
      <OffthreadVideo
        src={staticFile(`footage/${file}`)}
        trimBefore={Math.max(0, from)}
        playbackRate={rate}
        muted
        style={{ width: '100%', height: '100%', objectFit: 'cover', transform: `scale(${scale})`, transformOrigin: origin, ...style }}
      />
    </AbsoluteFill>
  );
};

/* ---------- captions ---------- */
/** Text with the parts between ‹ and › in the accent colour. */
export const Rich: React.FC<{ text: string; accent?: string }> = ({ text, accent = CORAL }) => (
  <>
    {text.split(/(‹[^›]*›)/).map((part, i) =>
      part.startsWith('‹') ? <span key={i} style={{ color: accent }}>{part.slice(1, -1)}</span> : <React.Fragment key={i}>{part}</React.Fragment>)}
  </>
);

type CaptionProps = { line1: string; line2?: string; y?: number; delay?: number; light?: boolean };

/** The two-line caption plate. Dark on footage; `light` on paper cards. */
export const Caption: React.FC<CaptionProps> = ({ line1, line2, y = CAPTION_Y, delay = 0, light = false }) => {
  const pop = usePop(delay);
  const en = useLang() === 'en';
  return (
    <div style={{
      position: 'absolute', left: SAFE_X, top: y, maxWidth: SAFE_W,
      transform: `translateY(${(1 - pop) * 26}px) scale(${0.94 + 0.06 * pop})`, transformOrigin: '0% 100%', opacity: Math.min(1, pop * 1.6),
      background: light ? 'rgba(255,255,255,0.0)' : 'rgba(26,36,43,0.90)', borderRadius: 30, padding: light ? '0' : '22px 38px 26px',
      boxShadow: light ? 'none' : '0 14px 40px rgba(20,28,34,0.28)', fontFamily: en ? LAT : CN, color: light ? INK : '#FFFFFF',
    }}>
      <div style={{ fontSize: en ? 70 : 74, fontWeight: 700, lineHeight: 1.22, letterSpacing: en ? 0 : 1, whiteSpace: 'nowrap' }}><Rich text={line1} /></div>
      {line2 ? <div style={{ fontSize: en ? 50 : 52, fontWeight: 400, lineHeight: 1.36, marginTop: 4, opacity: 0.92, whiteSpace: 'nowrap' }}><Rich text={line2} accent={light ? CORAL : '#FFD0C2'} /></div> : null}
    </div>
  );
};

/** A small pill above the caption: "Massing 面板 → VoxStack 叠楼". The second half arrives a moment later. */
export const PanelTag: React.FC<{ panel: string; game: string; delay?: number }> = ({ panel, game, delay = 0 }) => {
  const a = usePop(delay), b = usePop(delay + 16);
  return (
    <div style={{ position: 'absolute', left: SAFE_X, top: CAPTION_Y - 96, display: 'flex', alignItems: 'center', gap: 14, fontFamily: LAT, fontSize: 38 }}>
      <div style={{ transform: `scale(${a})`, transformOrigin: '0% 50%', background: '#1b1b1b', color: '#fff', borderRadius: 10, padding: '8px 22px', boxShadow: '0 6px 18px rgba(0,0,0,0.25)' }}>{panel}</div>
      <div style={{ opacity: b, color: INK, fontWeight: 700, textShadow: '0 0 12px #fff, 0 0 4px #fff' }}>→</div>
      <div style={{ transform: `scale(${b})`, transformOrigin: '0% 50%', background: '#FFFFFF', color: INK, borderRadius: 10, padding: '8px 22px', fontFamily: CN, fontWeight: 700, boxShadow: '0 6px 18px rgba(0,0,0,0.18)' }}>{game}</div>
    </div>
  );
};

/** A row of small chips (facts that need no sentence). */
export const Chips: React.FC<{ items: string[]; y: number; delay?: number; step?: number; dark?: boolean; size?: number }> = ({ items, y, delay = 0, step = 8, dark = false, size = 36 }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  return (
    <div style={{ position: 'absolute', left: SAFE_X, top: y, width: SAFE_W, display: 'flex', flexWrap: 'wrap', gap: 14, fontFamily: CN, fontSize: size }}>
      {items.map((item, i) => {
        const p = spring({ frame: frame - delay - i * step, fps, config: { damping: 14, stiffness: 190, mass: 0.6 } });
        return (
          <div key={item} style={{
            transform: `scale(${p})`, opacity: Math.min(1, p * 1.5), borderRadius: 999, padding: '9px 26px', fontWeight: 500,
            background: dark ? 'rgba(26,36,43,0.86)' : 'rgba(255,255,255,0.94)', color: dark ? '#fff' : INK, boxShadow: '0 6px 18px rgba(20,28,34,0.16)',
          }}>{item}</div>
        );
      })}
    </div>
  );
};

/** The boss key: a keycap that appears, is pressed on frame `press`, and leaves. */
export const Keycap: React.FC<{ press: number; label?: string; hint?: string; x?: number; y?: number }> = ({ press, label = '`', hint: given, x = 700, y = 1030 }) => {
  const frame = useCurrentFrame();
  const t = useT();
  const hint = given ?? t('老板键', 'Boss key');
  const { fps } = useVideoConfig();
  const arrive = spring({ frame: frame - (press - 22), fps, config: { damping: 13, stiffness: 200, mass: 0.6 } });
  const leave = ramp(frame, press + 34, press + 46, Easing.in(Easing.cubic));
  const down = frame >= press - 2 && frame < press + 9 ? 1 : 0;
  const ring = ramp(frame, press, press + 22);
  if (frame < press - 22 || frame > press + 46) return null;
  const size = 168;
  return (
    <div style={{ position: 'absolute', left: x, top: y, width: size, opacity: 1 - leave, transform: `scale(${arrive})`, transformOrigin: '50% 60%' }}>
      {frame >= press ? (
        <div style={{
          position: 'absolute', left: size / 2, top: size / 2, width: 0, height: 0, borderRadius: '50%',
          boxShadow: `0 0 0 ${ring * 150}px rgba(240,118,92,${0.5 * (1 - ring)})`,
        }} />
      ) : null}
      <div style={{
        width: size, height: size, borderRadius: 30, background: down ? '#ECE7E0' : '#FFFFFF', border: `4px solid ${down ? CORAL : '#D9D3CB'}`,
        boxShadow: down ? '0 3px 0 #BDB6AD, 0 6px 14px rgba(0,0,0,0.25)' : '0 14px 0 #C9C2B9, 0 22px 30px rgba(0,0,0,0.28)',
        transform: `translateY(${down ? 11 : 0}px)`, display: 'flex', alignItems: 'center', justifyContent: 'center',
        fontFamily: '"Cascadia Mono", Consolas, monospace', fontSize: 104, fontWeight: 700, color: INK, paddingTop: 26, boxSizing: 'border-box',
      }}>{label}</div>
      <div style={{
        marginTop: 26, textAlign: 'center', fontFamily: CN, fontSize: 34, fontWeight: 700, color: '#fff', whiteSpace: 'nowrap',
        textShadow: '0 2px 10px rgba(20,28,34,0.75), 0 0 3px rgba(20,28,34,0.9)',
      }}>{hint}</div>
    </div>
  );
};

/** "×12 倍速" in the top right of a fast-forwarded shot. */
export const SpeedBadge: React.FC<{ text: string }> = ({ text }) => {
  const pop = usePop(6);
  return (
    <div style={{
      position: 'absolute', left: SAFE_X, top: 96, transform: `scale(${pop})`, transformOrigin: '0% 50%', fontFamily: LAT, fontSize: 36,
      background: 'rgba(26,36,43,0.82)', color: '#fff', borderRadius: 999, padding: '8px 24px',
    }}>{text}</div>
  );
};

/** One beat of white at a cut, for the hard switches the edit adds itself (never on the game's own boss switch). */
export const Paper: React.FC<{ children?: React.ReactNode; style?: React.CSSProperties }> = ({ children, style }) => (
  <AbsoluteFill style={{ background: `radial-gradient(120% 80% at 50% 30%, #FBF8F3 0%, ${PAPER} 55%, #EAE3DA 100%)`, ...style }}>{children}</AbsoluteFill>
);
