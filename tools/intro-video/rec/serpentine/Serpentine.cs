using System;
using System.Collections.Generic;
using System.Linq;
using Moyu.Components.Landscape;
using Moyu.Games.Common.Core;
using Moyu.Games.Snake;
using Moyu.Games.Snake.Core;
using Rhino.Geometry;

namespace MoyuRec
{
  /// <summary>
  /// Serpentine on a torus, staged: first person riding over the top of the tube eating fruit, the camera key at `cam`
  /// s turns to the whole torus, a run of fruit takes the combo to ×5 and the level up, then the boss key at `boss` s.
  /// The snake follows a scripted route (long runs round the ring, short swerves over the tube) with fruit placed on it.
  /// args: seed, site (1 torus, 0 terrain), len, cam, boss, back, lens, ozoom / oel (overview), blens.
  /// </summary>
  public sealed class Serpentine : IShot
  {
    SnakeSession _s;
    SnakeGame G => _s.Game;
    SurfaceGrid Grid => G.Grid;
    // The route: one heading per step from frame 0 on, built in Setup; index = steps taken since the start.
    readonly List<(int du, int dv)> _route = new List<(int, int)>();
    // Fruit by route step (eaten when the head reaches that step).
    readonly SortedDictionary<int, ItemKind> _fruit = new SortedDictionary<int, ItemKind>();
    readonly HashSet<int> _placed = new HashSet<int>();
    int _steps;
    Cell _last;
    double _cam, _boss, _back;
    bool _camOn, _bossOn, _backOn;
    int _lockLevel;

    public int Frames => 60 * 13;

    public void Setup(Rec rec)
    {
      int site = (int)rec.Arg("site", 1);
      var brep = SiteSurfaces.Create(site, SiteSurfaceComponent.DefaultSize(), (int)rec.Arg("seed", 3));
      var field = SurfaceField.Build(brep.Faces[0], (int)rec.Arg("cells", 28), out string error);
      if (field == null) throw new InvalidOperationException("field: " + error);
      var settings = new SnakeSettings
      {
        Seed = (int)rec.Arg("seed", 3), StartLevel = (int)rec.Arg("level", 4), SlopeAffectsSpeed = false,
        StartLength = (int)rec.Arg("len", 12), SpawnItems = false,
      };
      _s = SnakeSession.Start(field, settings, null, null, out error);
      if (_s == null) throw new InvalidOperationException("Serpentine did not start: " + error);
      rec.Adopt(_s);
      _s.Language = rec.Arg("lang", "zh") == "en" ? UiLanguage.English : UiLanguage.Chinese;
      rec.SetCountdown(0);
      rec.SSet("PlayTime", 9.0);
      rec.View.ActiveViewport.Camera35mmLensLength = rec.Arg("lens", 30);
      _lockLevel = (int)rec.Arg("lock", settings.StartLevel);
      _cam = rec.Arg("cam", 3.0); _boss = rec.Arg("boss", 6.5); _back = rec.Arg("back", -1);

      // Route. Torus: over the tube to its top row, then long runs round the ring with short swerves, so the
      // horizon keeps rolling. Terrain (open, walled): a wide square circuit round the middle of the site.
      var head = G.Head;
      int preroll;
      void Run(int du, int dvv, int n) { for (int i = 0; i < n; i++) _route.Add((du, dvv)); }
      int top = -1;
      if (Grid.WrapU && Grid.WrapV)
      {
        top = Enumerable.Range(0, Grid.Nv).OrderByDescending(v => field.Centre(new Cell(head.U, v)).Z).First();
        int dv = ((top - head.V) % Grid.Nv + Grid.Nv) % Grid.Nv;
        int sign = dv <= Grid.Nv / 2 ? 1 : -1;
        Run(1, 0, 2);
        Run(0, sign, sign > 0 ? dv : Grid.Nv - dv);
        preroll = _route.Count + 6;
        for (int k = 0; k < 6; k++)
        {
          Run(1, 0, 7);
          Run(0, k % 2 == 0 ? 1 : -1, 2);
        }
      }
      else
      {
        // A square round the middle of the site, well clear of its edges.
        int side = (int)rec.Arg("side", 8);
        Run(1, 0, side / 2 - 1);
        preroll = _route.Count + side / 2;
        Run(0, 1, side / 2);
        for (int k = 0; k < 3; k++) { Run(-1, 0, side); Run(0, -1, side); Run(1, 0, side); Run(0, 1, side); }
      }
      // Fruit: two during the pre-roll, then a steady run (apple, grape, apple, grape, apple, melon).
      int s0 = preroll;
      _fruit[s0 - 6] = ItemKind.Apple;
      _fruit[s0 - 2] = ItemKind.Apple;
      var kinds = new[] { ItemKind.Apple, ItemKind.Grape, ItemKind.Apple, ItemKind.Grape, ItemKind.Apple, ItemKind.Melon, ItemKind.Apple };
      var at = new[] { 3, 8, 15, 19, 23, 27, 32 };
      for (int i = 0; i < at.Length; i++) _fruit[s0 + at[i]] = kinds[i];

      // Pre-roll until the head has taken `preroll` steps.
      _last = G.Head;
      int guard = 0;
      while (_steps < preroll && guard++ < 6000) { Drive(); rec.Step(); Track(); }
      rec.Note($"grid {Grid.Nu}x{Grid.Nv} wrap {Grid.WrapU}/{Grid.WrapV}; top row {top}; pre-roll {preroll} steps, head {G.Head}, length {G.Length}, combo {G.Combo}, fruit {G.FruitsEaten}, speed {G.Speed:F2}");
    }

    /// <summary>Counts the steps the head has taken.</summary>
    void Track()
    {
      if (!G.Head.Equals(_last)) { _steps++; _last = G.Head; }
    }

    /// <summary>Queues the route's next heading and puts fruit on the route a few steps ahead of the head.</summary>
    void Drive()
    {
      if (_lockLevel > 0) G.Settings.StartLevel = _lockLevel - (G.FruitsEaten + 1) / 5;
      if (_steps < _route.Count)
      {
        var want = _route[_steps];
        if (G.PlannedHeading != want) G.Turn(want.du, want.dv);
      }
      foreach (var f in _fruit)
      {
        if (_placed.Contains(f.Key) || f.Key - _steps > 7 || f.Key <= _steps) continue;
        var c = G.Head;
        bool ok = true;
        for (int i = _steps; i < f.Key && ok; i++) ok = i < _route.Count && Grid.TryStep(c, _route[i].du, _route[i].dv, out c);
        if (ok && !G.Occupies(c)) G.PlaceItem(f.Value, c);
        _placed.Add(f.Key);
      }
    }

    void Overview(Rec rec)
    {
      var vp = rec.View.ActiveViewport;
      var box = _s.Field.Bounds;
      double el = rec.Arg("oel", 52) * Math.PI / 180, az = rec.Arg("oaz", -70) * Math.PI / 180;
      var dir = new Vector3d(Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az), Math.Sin(el));
      double size = box.Diagonal.Length;
      var target = box.Center + Vector3d.ZAxis * size * rec.Arg("oty", 0.0);
      vp.SetCameraLocations(target, target + dir * size * rec.Arg("ozoom", 1.55));
      vp.CameraUp = Vector3d.ZAxis;
      vp.Camera35mmLensLength = rec.Arg("olens", 35);
    }

    void BossCamera(Rec rec)
    {
      var vp = rec.View.ActiveViewport;
      vp.Camera35mmLensLength = rec.Arg("blens", 40);
      var dir = vp.CameraLocation - vp.CameraTarget;
      var box = _s.Field.Bounds;
      // Centre the site: the disguise shifts it sideways to clear its card in a landscape view.
      var target = box.Center + Vector3d.ZAxis * box.Diagonal.Length * rec.Arg("bty", 0.0);
      vp.SetCameraLocations(target, target + dir * rec.Arg("bzoom", 1.0));
    }

    public void Frame(Rec rec, int i)
    {
      double t = i * rec.Dt;
      if (!_camOn && _cam >= 0 && t >= _cam - 1e-9) { _camOn = true; rec.SCall("ToggleCamera"); Overview(rec); }
      if (!_bossOn && _boss >= 0 && t >= _boss - 1e-9) { _bossOn = true; _s.ToggleBoss(); BossCamera(rec); }
      if (_bossOn && !_backOn && _back >= 0 && t >= _back - 1e-9) { _backOn = true; _s.ToggleBoss(); }
      if (!_s.Boss && _s.Countdown <= 0) Drive();
      rec.Step();
      Track();
      if (i % 60 == 0) rec.Note($"t {t:F1} step {_steps} head {G.Head} len {G.Length} combo {G.Combo} fruit {G.FruitsEaten} level {G.Level} state {G.State}");
    }

    public void Teardown(Rec rec) => _s?.Stop();
  }
}
