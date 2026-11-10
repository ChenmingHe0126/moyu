using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Moyu.Games.Common.Core;
using Moyu.Games.Stack;
using Moyu.Games.Stack.Core;
using Rhino;
using Rhino.Geometry;

namespace MoyuRec
{
  /// <summary>
  /// VoxStack, staged: a custom "Gate 门楼" unit is falling at frame 0; the bot turns it with the gumball's vertical ring,
  /// moves it and hard-drops it to clear one floor; an O4 is stood up and dropped into a 2-cell shaft to clear two floors
  /// at once; then the boss key turns the well into the white massing study, held to the end.
  /// Times (s, args): hot, grab, drop1, rot2, drop2, boss, back (negative = never). Camera: lens, tz, blens, bzoom.
  /// </summary>
  public sealed class VoxStack : IShot
  {
    const BindingFlags NP = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    StackSession _s;
    StackGame G => _s.Game;
    PieceDef _gate;
    // The planned placements: pivot cell of the gate after its turn, and of the stood-up O4.
    Int3 _gateAt, _o4At;
    readonly HashSet<(int, int)> _shaft = new HashSet<(int, int)> { (1, 0), (2, 0) };
    double _hot, _grab, _drop1, _rot2, _drop2, _boss, _back;
    bool _turned, _dropped1, _o4, _rotated2, _dropped2, _bossOn, _backOn, _released;
    double _nextMove;

    public int Frames => 60 * 12;

    public void Setup(Rec rec)
    {
      var settings = new StackSettings { Seed = (int)rec.Arg("seed", 5), StartLevel = (int)rec.Arg("level", 4) };
      double size = 7.2 * RhinoMath.UnitScale(UnitSystem.Meters, rec.Doc.ModelUnitSystem);
      _gate = new PieceDef("Gate 门楼", Polycube.ParsePattern("#.#/#.#|###"), Color.FromArgb(76, 201, 240).ToArgb(), 1.0, true);
      settings.Pieces = PieceSets.Get(PieceSets.Standard).Concat(new[] { _gate }).ToList();
      _s = StackSession.Start(settings, Plane.WorldXY, size, null, null, out string error);
      if (_s == null) throw new InvalidOperationException("VoxStack did not start: " + error);
      rec.Adopt(_s);
      _s.Language = rec.Arg("lang", "zh") == "en" ? UiLanguage.English : UiLanguage.Chinese;
      rec.SetCountdown(0);
      rec.SSet("PlayTime", 9.0);
      _hot = rec.Arg("hot", 1.7); _grab = rec.Arg("grab", 1.95); _drop1 = rec.Arg("drop1", 3.2);
      _rot2 = rec.Arg("rot2", 3.85); _drop2 = rec.Arg("drop2", 4.85); _boss = rec.Arg("boss", 5.5); _back = rec.Arg("back", -1);

      Plan(rec);
      Camera(rec);
      // A moment of fall before frame 0, so the gate is already on its way down.
      for (int i = 0; i < (int)(rec.Arg("preroll", 0.8) * rec.Fps); i++) rec.Step();
      rec.Note($"gate at {G.Position} offsets {string.Join(" ", G.Offsets)}; plan gate pivot {_gateAt}, O4 pivot {_o4At}; well {G.Well.Count} cells");
    }

    void Spawn(PieceDef def) => typeof(StackGame).GetMethod("Spawn", NP).Invoke(G, new object[] { def });

    /// <summary>
    /// Works out where the turned gate and the stood-up O4 go, and builds a stack around them: floors 0–1 full but for
    /// a 2-cell shaft at the front, floor 2 full but for the gate's four legs, a few loose cells on floors 3–4.
    /// </summary>
    void Plan(Rec rec)
    {
      var well = G.Well;
      well.Clear();
      // Turned gate: try it out on the empty well, find a pivot whose legs and lintel keep clear of the shaft.
      Spawn(_gate);
      var spawnAt = G.Position;
      G.Rotate(Int3.UnitZ, true);
      var gate = G.Offsets.ToArray();
      int low = gate.Min(o => o.Z);
      var best = (score: double.MaxValue, at: spawnAt);
      for (int x = 0; x < well.Nx; x++)
        for (int y = 0; y < well.Ny; y++)
        {
          var cells = gate.Select(o => new Int3(x + o.X, y + o.Y, 0)).ToArray();
          if (cells.Any(c => c.X < 0 || c.Y < 0 || c.X >= well.Nx || c.Y >= well.Ny || _shaft.Contains((c.X, c.Y)))) continue;
          // Sit it at the back, near where it spawns.
          double score = Math.Abs(x - spawnAt.X) + Math.Abs(y - spawnAt.Y) - 0.6 * cells.Min(c => c.Y);
          if (score < best.score) best = (score, new Int3(x, y, 0));
        }
      // The legs land on floor 1, so their pivot sits at z = 2 - lowest offset.
      _gateAt = new Int3(best.at.X, best.at.Y, 2 - low);
      var legs = gate.Where(o => o.Z == low).Select(o => (_gateAt.X + o.X, _gateAt.Y + o.Y)).ToHashSet();
      var lintel = gate.Where(o => o.Z != low).Select(o => (_gateAt.X + o.X, _gateAt.Y + o.Y)).ToHashSet();

      // Stood-up O4, aimed at the shaft.
      Spawn(PieceSets.O4);
      G.Rotate(Int3.UnitX, true);
      var o4 = G.Offsets.ToArray();
      int o4low = o4.Min(o => o.Z);
      var foot = o4.Select(o => (o.X, o.Y)).Distinct().ToList();
      var s0 = _shaft.OrderBy(c => c).First();
      var f0 = foot.OrderBy(c => c).First();
      _o4At = new Int3(s0.Item1 - f0.Item1, s0.Item2 - f0.Item2, -o4low);
      if (foot.Count != 2 || !foot.All(f => _shaft.Contains((f.Item1 + _o4At.X, f.Item2 + _o4At.Y))))
        throw new InvalidOperationException("the stood-up O4 does not fit the shaft: " + string.Join(" ", o4));

      // The stack.
      for (int x = 0; x < well.Nx; x++)
        for (int y = 0; y < well.Ny; y++)
        {
          var c = (x, y);
          if (!_shaft.Contains(c)) { well.Set(new Int3(x, y, 0), 1); well.Set(new Int3(x, y, 1), 1); }
          if (!legs.Contains(c)) well.Set(new Int3(x, y, 2), 1);
          bool open = legs.Contains(c) || lintel.Contains(c) || _shaft.Contains(c);
          if (!open && (x * 7 + y * 3) % 3 != 0) well.Set(new Int3(x, y, 3), 1);
          if (!open && (x * 5 + y * 2) % 4 == 1) well.Set(new Int3(x, y, 4), 1);
        }
      Spawn(_gate);
    }

    void Camera(Rec rec)
    {
      var vp = rec.View.ActiveViewport;
      var st = _s.Settings;
      var target = _s.WellPoint(st.Nx / 2.0, st.Ny / 2.0, st.Nz * rec.Arg("tz", 0.42));
      double k = st.Nz / 12.0 * _s.VoxelSize;
      var location = target + (Vector3d.XAxis * 14 - Vector3d.YAxis * 17 + Vector3d.ZAxis * 17.5) * k;
      vp.SetCameraLocations(target, location);
      vp.CameraUp = Vector3d.ZAxis;
      vp.Camera35mmLensLength = rec.Arg("lens", 42);
    }

    void BossCamera(Rec rec)
    {
      var vp = rec.View.ActiveViewport;
      vp.Camera35mmLensLength = rec.Arg("blens", 50) * rec.Arg("bzoom", 1.0);
    }

    public void Frame(Rec rec, int i)
    {
      double t = i * rec.Dt;
      var gb = _s.Gumball;
      if (!_bossOn && _boss >= 0 && t >= _boss - 1e-9) { _bossOn = true; _s.ToggleBoss(); BossCamera(rec); }
      if (_bossOn && !_backOn && _back >= 0 && t >= _back - 1e-9) { _backOn = true; _s.ToggleBoss(); }
      if (!_s.Boss && _s.Countdown <= 0)
      {
        // Gumball: hover the vertical (blue) ring, drag along it, the gate turns as the drag passes the threshold.
        const int ring = 2;
        if (t >= _hot && !_released)
        {
          SetGb(gb, "Hot", ring);
          if (t >= _grab)
          {
            if (gb.Dragging < 0) { SetGb(gb, "Dragging", ring); SetGb(gb, "GrabAngle", 0.55); }
            double u = Math.Min(1, (t - _grab) / 0.5);
            double angle = 0.78 * (1 - Math.Pow(1 - u, 3));
            SetGb(gb, "DragAngle", angle);
            if (!_turned && angle >= 0.55)
            {
              _turned = true;
              G.Rotate(Int3.UnitZ, true);
              SetGb(gb, "LastAxis", ring);
              SetGb(gb, "LastTurnTime", _s.Time);
            }
            if (u >= 1 && t >= _grab + 0.62) { gb.End(); SetGb(gb, "Hot", -1); _released = true; _nextMove = t + 0.08; }
          }
        }
        if (_released && !_dropped1) StepTowards(t, _gateAt);
        if (!_dropped1 && t >= _drop1) { _dropped1 = true; G.HardDrop(); }
        // After the clear the game deals the next piece: make it an O4, stand it up, slide it over the shaft.
        if (_dropped1 && !_o4 && G.State == StackState.Playing && G.Current != null) { _o4 = true; Spawn(PieceSets.O4); }
        if (_o4 && !_rotated2 && t >= _rot2) { _rotated2 = true; G.Rotate(Int3.UnitX, true); _nextMove = t + 0.12; }
        if (_rotated2 && !_dropped2) StepTowards(t, _o4At);
        if (_rotated2 && !_dropped2 && t >= _drop2) { _dropped2 = true; G.HardDrop(); }
      }
      rec.Step();
      if (i == 0 || (_dropped1 && !_o4) || i % 60 == 0)
        rec.Note($"t {t:F2} state {G.State} cur {G.Current?.Name} pos {G.Position} layers {G.Layers} score {G.Score} clear {G.LastClear?.Layers}");
    }

    /// <summary>One cell every few frames towards the planned pivot (x first, then y), the way a player taps the arrows.</summary>
    void StepTowards(double t, Int3 at)
    {
      if (t < _nextMove || G.Current == null) return;
      var p = G.Position;
      Int3 d = p.X != at.X ? new Int3(Math.Sign(at.X - p.X), 0, 0) : p.Y != at.Y ? new Int3(0, Math.Sign(at.Y - p.Y), 0) : Int3.Zero;
      if (d == Int3.Zero) return;
      G.Move(d);
      _nextMove = t + 0.1;
    }

    static void SetGb(Gumball gb, string name, object value) =>
      typeof(Gumball).GetProperty(name, NP).SetValue(gb, value);

    public void Teardown(Rec rec) => _s?.Stop();
  }
}
