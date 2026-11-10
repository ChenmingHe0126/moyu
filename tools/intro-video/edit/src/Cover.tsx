import React from 'react';
import { AbsoluteFill, Img, staticFile } from 'remotion';
import { CN, LAT } from './ui';

/** Post cover, blue: the plugin is the point. Fish icon, the title, the four games in a grid, the boss key as a footnote. */
export const Cover: React.FC<{ tall: boolean }> = ({ tall }) => {
  const W = 1080, H = tall ? 1920 : 1440;
  // Mostly type, the games as a row of small crops (no HUD text): Douyin marks covers that are mostly screenshots.
  const gap = 18, tw = (W - 120 - 3 * gap) / 4, th = tall ? 380 : 300;
  const rowTop = tall ? 1250 : 940;
  const games: [string, string][] = [['c_rebound.png', '回弹'], ['c_vox.png', '叠楼'], ['c_torus.png', '蜿蜒'], ['c_swift.png', '雨燕']];
  const YELLOW = '#FFD84D';
  return (
    <AbsoluteFill style={{ background: 'linear-gradient(160deg, #0F3D91 0%, #1F63D1 55%, #4FA0F0 100%)', fontFamily: CN, color: '#fff' }}>
      <div style={{ position: 'absolute', right: -180, top: -160, width: 620, height: 620, borderRadius: '50%', background: 'radial-gradient(circle, rgba(255,255,255,0.22), rgba(255,255,255,0) 70%)' }} />
      <div style={{ position: 'absolute', left: -140, bottom: -200, width: 560, height: 560, borderRadius: '50%', background: 'radial-gradient(circle, rgba(255,255,255,0.14), rgba(255,255,255,0) 70%)' }} />
      <div style={{ position: 'absolute', left: 60, top: tall ? 170 : 90, width: W - 120 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 22 }}>
          <div style={{ width: 130, height: 130, borderRadius: 32, background: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: '0 12px 30px rgba(0,20,60,0.35)' }}>
            <Img src={staticFile('stills/icon-512.png')} style={{ width: 108, height: 108 }} />
          </div>
          <div>
            <div style={{ fontFamily: LAT, fontSize: 68, fontWeight: 700, lineHeight: 1 }}>Moyu 摸鱼</div>
            <div style={{ fontSize: 34, opacity: 0.85, marginTop: 12 }}>Rhino 8 · Grasshopper 插件 · 免费开源</div>
          </div>
        </div>
        <div style={{ fontSize: tall ? 120 : 108, fontWeight: 800, lineHeight: 1.2, marginTop: tall ? 90 : 56, whiteSpace: 'nowrap', textShadow: '0 6px 24px rgba(0,20,60,0.35)' }}>
          我用 AI 做了一款<br />在 Rhino 里<br /><span style={{ color: YELLOW }}>摸鱼</span>的<span style={{ color: YELLOW }}>插件</span>
        </div>
      </div>
      {games.map(([file, name], i) => (
        <div key={file} style={{ position: 'absolute', left: 60 + i * (tw + gap), top: rowTop, width: tw, height: th, borderRadius: 22, overflow: 'hidden', border: '4px solid rgba(255,255,255,0.9)', boxShadow: '0 14px 30px rgba(0,20,60,0.35)' }}>
          <Img src={staticFile(`stills/${file}`)} style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
          <div style={{ position: 'absolute', left: 10, bottom: 10, background: 'rgba(15,61,145,0.88)', borderRadius: 999, padding: '4px 16px', fontSize: 30, fontWeight: 700 }}>{name}</div>
        </div>
      ))}
      <div style={{ position: 'absolute', left: 60, top: rowTop + th + (tall ? 60 : 40), fontSize: 40, display: 'flex', alignItems: 'center', gap: 14, opacity: 0.95 }}>
        老板路过？按
        <span style={{ fontFamily: '"Cascadia Mono", Consolas, monospace', fontWeight: 700, color: '#0F3D91', background: '#fff', borderRadius: 10, padding: '0 16px', fontSize: 42 }}>`</span>
        一帧变回正经图纸
      </div>
    </AbsoluteFill>
  );
};

/** Landscape 4:3 cover (Douyin wants a horizontal one too): title on the left, the four games on the right. */
export const CoverWide: React.FC = () => {
  const YELLOW = '#FFD84D';
  const games: [string, string, string][] = [
    ['g_rebound.png', '回弹', '50% 40%'], ['g_vox.png', '叠楼', '50% 45%'],
    ['g_torus.png', '蜿蜒', '50% 50%'], ['g_swift.png', '雨燕', '30% 45%'],
  ];
  const gx = 800, gy = 90, gap = 20, tw = 290, th = 430;
  return (
    <AbsoluteFill style={{ background: 'linear-gradient(150deg, #0F3D91 0%, #1F63D1 55%, #4FA0F0 100%)', fontFamily: CN, color: '#fff' }}>
      <div style={{ position: 'absolute', left: -200, top: -220, width: 700, height: 700, borderRadius: '50%', background: 'radial-gradient(circle, rgba(255,255,255,0.18), rgba(255,255,255,0) 70%)' }} />
      <div style={{ position: 'absolute', left: 80, top: 150, width: 680 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 22 }}>
          <div style={{ width: 120, height: 120, borderRadius: 30, background: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: '0 12px 30px rgba(0,20,60,0.35)' }}>
            <Img src={staticFile('stills/icon-512.png')} style={{ width: 100, height: 100 }} />
          </div>
          <div>
            <div style={{ fontFamily: LAT, fontSize: 64, fontWeight: 700, lineHeight: 1 }}>Moyu 摸鱼</div>
            <div style={{ fontSize: 32, opacity: 0.85, marginTop: 10, whiteSpace: 'nowrap' }}>Grasshopper 插件 · 免费开源</div>
          </div>
        </div>
        <div style={{ fontSize: 80, fontWeight: 800, lineHeight: 1.25, marginTop: 70, whiteSpace: 'nowrap', textShadow: '0 6px 24px rgba(0,20,60,0.35)' }}>
          我用 AI 做了一款<br />在 Rhino 里<br /><span style={{ color: YELLOW }}>摸鱼</span>的<span style={{ color: YELLOW }}>插件</span>
        </div>
        <div style={{ fontSize: 36, marginTop: 60, display: 'flex', alignItems: 'center', gap: 14, opacity: 0.95 }}>
          老板路过？按
          <span style={{ fontFamily: '"Cascadia Mono", Consolas, monospace', fontWeight: 700, color: '#0F3D91', background: '#fff', borderRadius: 10, padding: '0 16px', fontSize: 38 }}>`</span>
          一帧变回图纸
        </div>
      </div>
      {games.map(([file, name, pos], i) => (
        <div key={file} style={{ position: 'absolute', left: gx + (i % 2) * (tw + gap), top: gy + Math.floor(i / 2) * (th + gap), width: tw, height: th, borderRadius: 24, overflow: 'hidden', border: '4px solid rgba(255,255,255,0.9)', boxShadow: '0 18px 40px rgba(0,20,60,0.35)' }}>
          <Img src={staticFile(`stills/${file}`)} style={{ width: '100%', height: '100%', objectFit: 'cover', objectPosition: pos }} />
          <div style={{ position: 'absolute', left: 12, bottom: 12, background: 'rgba(15,61,145,0.88)', borderRadius: 999, padding: '5px 20px', fontSize: 30, fontWeight: 700 }}>{name}</div>
        </div>
      ))}
    </AbsoluteFill>
  );
};

/** Hero covers: one game frame full-bleed (Serpentine on the torus) under a big title on a blue fade. */
const HeroTitle: React.FC<{ size: number; lines: 2 | 3 }> = ({ size, lines }) => {
  const YELLOW = '#FFD84D';
  return (
    <div style={{ fontSize: size, fontWeight: 800, lineHeight: 1.18, whiteSpace: 'nowrap', textShadow: '0 4px 0 rgba(10,40,100,0.55), 0 10px 30px rgba(0,20,60,0.45)' }}>
      我用 AI 做了一款<br />
      {lines === 2 ? <>在 Rhino 里<span style={{ color: YELLOW }}>摸鱼</span>的<span style={{ color: YELLOW }}>插件</span></>
        : <>在 Rhino 里<br /><span style={{ color: YELLOW }}>摸鱼</span>的<span style={{ color: YELLOW }}>插件</span></>}
    </div>
  );
};

const Brand: React.FC<{ size?: number }> = ({ size = 40 }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
    <div style={{ width: size * 2.2, height: size * 2.2, borderRadius: size * 0.55, background: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: '0 8px 20px rgba(0,20,60,0.35)' }}>
      <Img src={staticFile('stills/icon-512.png')} style={{ width: size * 1.8, height: size * 1.8 }} />
    </div>
    <div style={{ fontSize: size, fontWeight: 700, lineHeight: 1.25 }}>
      <span style={{ fontFamily: LAT }}>Moyu 摸鱼</span><br /><span style={{ fontSize: size * 0.8, fontWeight: 500, opacity: 0.9 }}>Rhino 8 · Grasshopper 插件 · 免费开源</span>
    </div>
  </div>
);

const BossSticker: React.FC<{ size?: number }> = ({ size = 40 }) => (
  <div style={{ display: 'inline-flex', alignItems: 'center', gap: 12, background: '#FFD84D', color: '#0F3D91', borderRadius: 999, padding: `${size * 0.3}px ${size * 0.8}px`, fontSize: size, fontWeight: 800, boxShadow: '0 10px 24px rgba(0,20,60,0.35)', transform: 'rotate(-3deg)' }}>
    老板来了？按
    <span style={{ fontFamily: '"Cascadia Mono", Consolas, monospace', background: '#fff', borderRadius: 10, padding: '0 14px', fontSize: size * 1.05 }}>`</span>
    秒变图纸
  </div>
);

export const CoverHero34: React.FC = () => {
  const s = 1.25;
  return (
    <AbsoluteFill style={{ background: '#DCEBD7', fontFamily: CN, color: '#fff', overflow: 'hidden' }}>
      <Img src={staticFile('stills/hero_torus.png')} style={{ position: 'absolute', left: 540 - 540 * s, top: 920 - 1000 * s, width: 1080 * s, height: 1920 * s, maxWidth: 'none' }} />
      <div style={{ position: 'absolute', left: 0, top: 0, width: 1080, height: 760, background: 'linear-gradient(180deg, #0F3D91 0%, #164AA6 30%, rgba(20,80,180,0.9) 46%, rgba(31,99,209,0.5) 66%, rgba(31,99,209,0) 100%)' }} />
      <div style={{ position: 'absolute', left: 0, bottom: 0, width: 1080, height: 260, background: 'linear-gradient(0deg, rgba(15,61,145,0.85) 0%, rgba(15,61,145,0) 100%)' }} />
      <div style={{ position: 'absolute', left: 60, top: 64 }}><Brand size={38} /></div>
      <div style={{ position: 'absolute', left: 60, top: 230 }}><HeroTitle size={90} lines={2} /></div>
      <div style={{ position: 'absolute', left: 60, bottom: 70 }}><BossSticker size={44} /></div>
    </AbsoluteFill>
  );
};

export const CoverHero43: React.FC = () => {
  const s = 1.6;
  return (
    <AbsoluteFill style={{ background: '#DCEBD7', fontFamily: CN, color: '#fff', overflow: 'hidden' }}>
      <Img src={staticFile('stills/hero_torus.png')} style={{ position: 'absolute', left: 1010 - 540 * s, top: 560 - 1000 * s, width: 1080 * s, height: 1920 * s, maxWidth: 'none' }} />
      <div style={{ position: 'absolute', left: 0, top: 0, width: 1000, height: 1080, background: 'linear-gradient(90deg, #0F3D91 0%, rgba(20,80,180,0.92) 52%, rgba(31,99,209,0) 100%)' }} />
      <div style={{ position: 'absolute', left: 70, top: 90 }}><Brand size={38} /></div>
      <div style={{ position: 'absolute', left: 70, top: 300 }}><HeroTitle size={96} lines={3} /></div>
      <div style={{ position: 'absolute', left: 70, bottom: 90 }}><BossSticker size={42} /></div>
    </AbsoluteFill>
  );
};

/* ------------------------------------------------------------------ the "花字" covers: the game's own colours */
const MINT = '#E9F2E7', DEEP = '#1F4E46', CORAL2 = '#F26B4F';

/** Big display text with a thick outline and a hard drop shadow (two stacked layers: outline behind, fill on top). */
const Fancy: React.FC<{ text: string; size: number; fill: string; stroke?: string; strokeW?: number; shadow?: string; rotate?: number; style?: React.CSSProperties }> =
  ({ text, size, fill, stroke = DEEP, strokeW = 16, shadow = 'rgba(31,78,70,0.35)', rotate = 0, style }) => {
    const base: React.CSSProperties = { fontFamily: CN, fontSize: size, fontWeight: 900, lineHeight: 1.08, whiteSpace: 'nowrap', letterSpacing: size * 0.02, paddingRight: strokeW };
    return (
      <div style={{ position: 'relative', display: 'inline-block', transform: `rotate(${rotate}deg)`, ...style }}>
        <div style={{ ...base, color: stroke, WebkitTextStroke: `${strokeW}px ${stroke}`, textShadow: `0 ${size * 0.08}px 0 ${shadow}` }}>{text}</div>
        <div style={{ ...base, position: 'absolute', left: 0, top: 0, background: fill, WebkitBackgroundClip: 'text', backgroundClip: 'text', color: 'transparent' }}>{text}</div>
      </div>
    );
  };

const Pill: React.FC<{ size?: number }> = ({ size = 32 }) => (
  <div style={{ display: 'inline-flex', alignItems: 'center', gap: 12, background: 'rgba(255,255,255,0.92)', borderRadius: 999, padding: `${size * 0.25}px ${size * 0.7}px ${size * 0.25}px ${size * 0.3}px`, boxShadow: '0 8px 20px rgba(31,78,70,0.18)', color: DEEP, fontFamily: CN, fontSize: size, fontWeight: 700 }}>
    <Img src={staticFile('stills/icon-512.png')} style={{ width: size * 1.5, height: size * 1.5 }} />
    <span><span style={{ fontFamily: LAT }}>Moyu 摸鱼</span><span style={{ fontWeight: 500, opacity: 0.75 }}> · Grasshopper 插件 · 免费开源</span></span>
  </div>
);

const Sticker: React.FC<{ size?: number; rotate?: number }> = ({ size = 44, rotate = -3 }) => (
  <div style={{ display: 'inline-flex', alignItems: 'center', gap: 12, background: CORAL2, color: '#fff', borderRadius: 18, padding: `${size * 0.32}px ${size * 0.75}px`, fontFamily: CN, fontSize: size, fontWeight: 900, border: `6px solid #fff`, boxShadow: `0 10px 0 ${DEEP}`, transform: `rotate(${rotate}deg)` }}>
    老板来了？按
    <span style={{ fontFamily: '"Cascadia Mono", Consolas, monospace', background: '#fff', color: DEEP, borderRadius: 10, padding: '0 14px', fontSize: size * 1.05 }}>`</span>
    秒变图纸
  </div>
);

const GOLD = 'linear-gradient(180deg, #FFF27A 0%, #FFC93C 55%, #FF9F2E 100%)';

export const CoverFun34: React.FC = () => {
  const s = 1.05;
  return (
    <AbsoluteFill style={{ background: MINT, overflow: 'hidden' }}>
      <Img src={staticFile('stills/hero_torus.png')} style={{ position: 'absolute', left: 540 - 540 * s, top: 610 - 640 * s, width: 1080 * s, height: 1920 * s, maxWidth: 'none' }} />
      {/* a light mint wash over the game's HUD at the top, so the title sits on the game's own colour */}
      <div style={{ position: 'absolute', left: 0, top: 0, width: 1080, height: 700, background: `linear-gradient(180deg, ${MINT} 0%, ${MINT} 40%, rgba(233,242,231,0.9) 62%, rgba(233,242,231,0) 100%)` }} />
      <div style={{ position: 'absolute', left: 60, top: 54 }}><Pill size={30} /></div>
      <div style={{ position: 'absolute', left: 50, top: 150 }}>
        <Fancy text="我用 AI 做了一款" size={92} fill="#FFFFFF" strokeW={14} />
      </div>
      <div style={{ position: 'absolute', left: 50, top: 270 }}>
        <Fancy text="在 Rhino 里" size={92} fill="#FFFFFF" strokeW={14} />
      </div>
      <div style={{ position: 'absolute', left: 36, top: 370 }}>
        <Fancy text="摸鱼" size={210} fill={GOLD} strokeW={22} rotate={-4} />
      </div>
      <div style={{ position: 'absolute', left: 500, top: 440 }}>
        <Fancy text="的插件" size={120} fill="#FFFFFF" strokeW={16} rotate={-4} />
      </div>
      <div style={{ position: 'absolute', left: 60, bottom: 64 }}><Sticker size={46} /></div>
    </AbsoluteFill>
  );
};

const PillEn: React.FC<{ size?: number }> = ({ size = 32 }) => (
  <div style={{ display: 'inline-flex', alignItems: 'center', gap: 12, background: 'rgba(255,255,255,0.92)', borderRadius: 999, padding: `${size * 0.25}px ${size * 0.7}px ${size * 0.25}px ${size * 0.3}px`, boxShadow: '0 8px 20px rgba(31,78,70,0.18)', color: DEEP, fontFamily: CN, fontSize: size, fontWeight: 700 }}>
    <Img src={staticFile('stills/icon-512.png')} style={{ width: size * 1.5, height: size * 1.5 }} />
    <span>Moyu<span style={{ fontWeight: 500, opacity: 0.75 }}> · a Grasshopper plugin · free</span></span>
  </div>
);

/** Instagram Reel cover, English, 9:16. The profile grid shows the middle 3:4 (y 240–1680), so everything sits inside it. */
export const CoverFunEn916: React.FC = () => {
  const s = 1.0;
  return (
    <AbsoluteFill style={{ background: MINT, overflow: 'hidden' }}>
      <Img src={staticFile('stills/hero_torus.png')} style={{ position: 'absolute', left: 540 - 540 * s, top: 980 - 760 * s, width: 1080 * s, height: 1920 * s, maxWidth: 'none' }} />
      <div style={{ position: 'absolute', left: 0, top: 0, width: 1080, height: 1080, background: `linear-gradient(180deg, ${MINT} 0%, ${MINT} 76%, rgba(233,242,231,0.85) 84%, rgba(233,242,231,0) 100%)` }} />
      <div style={{ position: 'absolute', left: 60, top: 280 }}><PillEn size={30} /></div>
      <div style={{ position: 'absolute', left: 52, top: 380 }}><Fancy text="I used AI to build" size={88} fill="#FFFFFF" strokeW={14} /></div>
      <div style={{ position: 'absolute', left: 52, top: 492 }}><Fancy text="a Rhino plugin for" size={88} fill="#FFFFFF" strokeW={14} /></div>
      <div style={{ position: 'absolute', left: 36, top: 600 }}><Fancy text="SLACKING" size={190} fill={GOLD} strokeW={22} rotate={-4} /></div>
      <div style={{ position: 'absolute', left: 560, top: 790 }}><Fancy text="OFF" size={150} fill={GOLD} strokeW={20} rotate={-4} /></div>
      <div style={{ position: 'absolute', left: 60, top: 1500 }}>
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 12, background: CORAL2, color: '#fff', borderRadius: 18, padding: '14px 34px', fontFamily: CN, fontSize: 44, fontWeight: 900, border: '6px solid #fff', boxShadow: `0 10px 0 ${DEEP}`, transform: 'rotate(-3deg)' }}>
          Boss walking by? Press
          <span style={{ fontFamily: '"Cascadia Mono", Consolas, monospace', background: '#fff', color: DEEP, borderRadius: 10, padding: '0 14px', fontSize: 46 }}>`</span>
        </div>
      </div>
    </AbsoluteFill>
  );
};

/** Bilibili 16:9: the 4:3 layout with the torus further right; the sticker sits higher, clear of the feed's
 *  play-count / duration strip along the bottom edge. */
export const CoverFun169: React.FC = () => {
  const s = 1.3;
  return (
    <AbsoluteFill style={{ background: MINT, overflow: 'hidden' }}>
      <Img src={staticFile('stills/hero_torus.png')} style={{ position: 'absolute', left: 1440 - 540 * s, top: 560 - 1000 * s, width: 1080 * s, height: 1920 * s, maxWidth: 'none' }} />
      <div style={{ position: 'absolute', left: 0, top: 0, width: 1300, height: 1080, background: `linear-gradient(90deg, ${MINT} 0%, ${MINT} 62%, rgba(233,242,231,0.85) 74%, rgba(233,242,231,0) 100%)` }} />
      <div style={{ position: 'absolute', left: 80, top: 70 }}><Pill size={32} /></div>
      <div style={{ position: 'absolute', left: 76, top: 190 }}><Fancy text="我用 AI 做了一款" size={96} fill="#FFFFFF" strokeW={15} /></div>
      <div style={{ position: 'absolute', left: 76, top: 315 }}><Fancy text="在 Rhino 里" size={96} fill="#FFFFFF" strokeW={15} /></div>
      <div style={{ position: 'absolute', left: 60, top: 420 }}><Fancy text="摸鱼" size={220} fill={GOLD} strokeW={24} rotate={-4} /></div>
      <div style={{ position: 'absolute', left: 530, top: 492 }}><Fancy text="的插件" size={124} fill="#FFFFFF" strokeW={17} rotate={-4} /></div>
      <div style={{ position: 'absolute', left: 80, bottom: 190 }}><Sticker size={44} /></div>
    </AbsoluteFill>
  );
};

export const CoverFun43: React.FC = () => {
  const s = 1.3;
  return (
    <AbsoluteFill style={{ background: MINT, overflow: 'hidden' }}>
      <Img src={staticFile('stills/hero_torus.png')} style={{ position: 'absolute', left: 1030 - 540 * s, top: 560 - 1000 * s, width: 1080 * s, height: 1920 * s, maxWidth: 'none' }} />
      <div style={{ position: 'absolute', left: 0, top: 0, width: 900, height: 1080, background: `linear-gradient(90deg, ${MINT} 0%, rgba(233,242,231,0.95) 50%, rgba(233,242,231,0) 100%)` }} />
      <div style={{ position: 'absolute', left: 60, top: 60 }}><Pill size={30} /></div>
      <div style={{ position: 'absolute', left: 56, top: 190 }}><Fancy text="我用 AI 做了一款" size={88} fill="#FFFFFF" strokeW={14} /></div>
      <div style={{ position: 'absolute', left: 56, top: 305 }}><Fancy text="在 Rhino 里" size={88} fill="#FFFFFF" strokeW={14} /></div>
      <div style={{ position: 'absolute', left: 40, top: 405 }}><Fancy text="摸鱼" size={200} fill={GOLD} strokeW={22} rotate={-4} /></div>
      <div style={{ position: 'absolute', left: 475, top: 470 }}><Fancy text="的插件" size={112} fill="#FFFFFF" strokeW={16} rotate={-4} /></div>
      <div style={{ position: 'absolute', left: 60, bottom: 70 }}><Sticker size={42} /></div>
    </AbsoluteFill>
  );
};
