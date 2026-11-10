// Source: docs/dev-time-by-activity.md (derived from docs/dev-time-stats.md and docs/dev-time-1.0.1.json).
// Every 1.0.0 minute is one tile. Parts list their activities in the order the work happened.

export type ActivityId = 'A' | 'B' | 'C' | 'D' | 'E' | 'F' | 'G';

export const ACTIVITIES: Record<ActivityId, { name: string; color: string }> = {
  B: { name: '写代码与自动测试', color: '#287F7D' },
  F: { name: '文档、报告与计时', color: '#647F9F' },
  G: { name: '审查与发布', color: '#AA744D' },
  C: { name: 'Rhino 实机测试与修复', color: '#C0564A' },
  A: { name: '方案与原型', color: '#7D6AA8' },
  D: { name: '画面设计与打磨', color: '#D99A4E' },
  E: { name: '图标', color: '#7F9A4E' },
};

// Bars in the second half, largest first.
export const ACTIVITY_ORDER: ActivityId[] = ['B', 'F', 'G', 'C', 'A', 'D', 'E'];

export type Part = {
  id: string;
  name: string;
  color: string;
  seq: [ActivityId, number][];
  excluded: number; // Rhino / third-party crash minutes, not counted
};

export const PARTS: Part[] = [
  { id: 'topo', name: '拓扑优化 2D + 3D', color: '#5B7697', seq: [['B', 51], ['E', 3], ['B', 59], ['E', 1]], excluded: 27 },
  { id: 'vox', name: 'VoxStack 叠楼', color: '#1E6B68', seq: [['A', 31], ['B', 22], ['C', 17], ['F', 4], ['E', 6], ['G', 3]], excluded: 5 },
  { id: 'serp', name: 'Serpentine 蜿蜒', color: '#2F8C87', seq: [['A', 8], ['B', 22], ['C', 7], ['D', 19], ['F', 14]], excluded: 0 },
  { id: 'reb', name: 'Rebound + Inkline', color: '#55A7A0', seq: [['A', 9], ['B', 21], ['E', 8], ['F', 2], ['C', 6], ['F', 10]], excluded: 2 },
  { id: 'swift', name: 'Swift 雨燕 + 老板画面重做', color: '#86C3BC', seq: [['A', 4], ['B', 25], ['D', 6], ['C', 2], ['F', 16]], excluded: 0 },
  {
    id: 'rounds', name: '三轮调整', color: '#9C8FB8',
    seq: [['B', 13], ['D', 4], ['C', 1], ['F', 8], ['C', 2], ['A', 2], ['B', 6], ['C', 10], ['F', 7], ['C', 4], ['B', 5], ['C', 18], ['F', 8], ['C', 5]],
    excluded: 0,
  },
  { id: 'release', name: '1.0.0 审查与发布', color: '#AA744D', seq: [['G', 30], ['G', 19], ['E', 11], ['G', 22]], excluded: 0 },
  { id: 'guide', name: '图文手册与上架文案', color: '#D3A57E', seq: [['F', 9], ['G', 6], ['F', 17], ['G', 5]], excluded: 0 },
];

export type Tile = { idx: number; part: number; act: ActivityId; slot: number };

export function buildTiles(): { tiles: Tile[]; counts: Record<ActivityId, number>; partStart: number[]; partTotal: number[] } {
  const tiles: Tile[] = [];
  const counts = { A: 0, B: 0, C: 0, D: 0, E: 0, F: 0, G: 0 } as Record<ActivityId, number>;
  const partStart: number[] = [];
  const partTotal: number[] = [];
  PARTS.forEach((p, pi) => {
    partStart.push(tiles.length);
    let n = 0;
    for (const [act, k] of p.seq) {
      for (let i = 0; i < k; i++) {
        tiles.push({ idx: tiles.length, part: pi, act, slot: counts[act]++ });
        n++;
      }
    }
    partTotal.push(n);
  });
  return { tiles, counts, partStart, partTotal };
}

// 1.0.1: stages from docs/dev-time-1.0.1.json (primary = main-agent turns, rest = background-only time).
export const STAGES_101 = [
  { name: '语言菜单、双语示例与全仓审查修复', primary: 38.4, total: 167.8 },
  { name: 'Examples 组件、图标与发布包', primary: 15.9, total: 20.3 },
  { name: '面板编号、命名与排版', primary: 8.1, total: 8.1 },
  { name: 'Yak 与 food4rhino 发布、合并', primary: 7.9, total: 7.9 },
  { name: '提交、重建与本机安装', primary: 2.9, total: 2.9 },
];
