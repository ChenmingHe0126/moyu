// The cut, in music beats. "Pamgaea" (Kevin MacLeod) is exactly 94 BPM, so one beat is 60/94 s ≈ 38.3 frames at
// 60 fps; every cut and every boss-key switch sits on a beat. 104 beats = 26 bars ≈ 66.4 s. Every caption stays at
// least 4 beats (2.6 s), most 5–8, so it can be read.
export const FPS = 60;
export const BPM = 94;
export const BEAT = (FPS * 60) / BPM;
export const F = (beat: number) => Math.round(beat * BEAT);
export const TOTAL_BEATS = 104;
export const DURATION = F(TOTAL_BEATS);

/** Scene boundaries in beats. */
export const B = {
  hook: 0, hookBoss: 5, title: 10, ribbon: 15,
  rhino: 21, rhinoPlay: 24, rhinoBoss: 29,
  vox: 34,
  serp: 41, serpTerrain: 46, serpBoss: 50,
  swift: 55, swiftBoss: 60, swiftBack: 64, swiftKey: 65,
  topo2d: 72, topo3d: 78, ai: 86, install: 91, examples: 95, end: 99,
};

/**
 * Recorded clips (dist/intro-video/footage, made by tools/intro-video/rec): file and the source frame (60 fps) of
 * each event. See tools/intro-video/rec/SHOTS.md.
 */
export const CLIPS = {
  rebound: { file: 'rebound_hook.mp4', boss: 300 },
  // The real Rhino and Grasshopper windows: Rhino 1920x1080 above Grasshopper 1920x1400; Run at 1.0 s, boss at 6.5 s.
  rhino: { file: 'rhino_ui.mp4', click: 60, boss: 390 },
  vox: { file: 'voxstack_play_boss.mp4', boss: 330 },
  torus: { file: 'serpentine_torus.mp4', from: 138 },
  terrain: { file: 'serpentine_terrain_boss.mp4', boss: 300 },
  swift: { file: 'swift_play_boss_return.mp4', boss: 300, back: 540 },
  // `speed`: how much faster than the real solve the iterations play (solve time / time on screen).
  topo2d: { file: 'topo2d.mp4', from: 0, rate: 0.8, speed: 26 },
  topo3d: { file: 'topo3d.mp4', from: 0, rate: 1, speed: 8 },
  pm: { file: '36_package_manager.mp4' },
  examples: { file: '37_examples.mp4' },
};
