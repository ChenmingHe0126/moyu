using System;
using System.Collections.Generic;

namespace Moyu.Core
{
  /// <summary>
  /// Minimum-compliance SIMP optimiser with an optimality-criteria update (after Andreassen et al. 2011, "top88").
  /// State persists between <see cref="Step"/> calls so the optimisation can be advanced incrementally.
  /// The problem can be swapped (loads, supports, passive elements, settings) while the densities carry over.
  /// Subclasses supply the finite-element analysis for their element type.
  /// </summary>
  public abstract class SimpOptimizer
  {
    /// <summary>Floor of a design density: a cell at exactly zero could never regrow under the OC update.</summary>
    const double MinDensity = 1e-9;

    ProblemBase _problem;
    SimpSettings _settings;
    NeighborFilter _filter;
    int _budgetStart;

    readonly double[] _ce;
    readonly double[] _dc;
    readonly double[] _dv;
    readonly double[] _tmp;
    readonly double[] _xNew;

    /// <summary>Design variables.</summary>
    public readonly double[] X;
    /// <summary>Physical (filtered, passive-enforced) densities.</summary>
    public readonly double[] XPhys;
    public readonly List<double> History = new List<double>();

    public int Iteration { get; private set; }
    public double Compliance { get; private set; } = double.NaN;
    public double Change { get; private set; } = 1.0;
    public double Volume { get; private set; }
    public bool Converged { get; private set; }
    /// <summary>Increments whenever densities or the problem change.</summary>
    public int Revision { get; private set; }
    public bool ReachedMaxIterations => Iteration - _budgetStart >= _settings.MaxIterations;
    public bool IsFinished => Converged || ReachedMaxIterations;
    public SimpSettings Settings => _settings;
    protected ProblemBase CurrentProblem => _problem;

    protected SimpOptimizer(ProblemBase problem, SimpSettings settings, NeighborFilter filter)
    {
      int nel = problem.ElementCount;
      X = new double[nel];
      XPhys = new double[nel];
      _ce = new double[nel];
      _dc = new double[nel];
      _dv = new double[nel];
      _tmp = new double[nel];
      _xNew = new double[nel];

      _problem = problem;
      _settings = settings.Clone();
      _filter = filter;

      for (int e = 0; e < nel; e++)
        X[e] = problem.Kinds[e] == ElementKind.Design ? _settings.VolumeFraction : 0.0;
      UpdatePhysical(X);
      Volume = MeasureVolume();
    }

    protected abstract bool IsSameGrid(ProblemBase problem);
    protected abstract NeighborFilter CreateFilter(ProblemBase problem, double radius);
    /// <summary>Called before a new problem replaces the current one (e.g. to rebuild the element stiffness).</summary>
    protected virtual void OnProblemReplaced(ProblemBase previous, ProblemBase next) { }

    /// <summary>
    /// Solves K(xPhys) u = f and writes the unscaled element energies uₑᵀ k₀ uₑ into <paramref name="energies"/>.
    /// Throws <see cref="InvalidOperationException"/> when the structure is a mechanism.
    /// </summary>
    protected abstract void Analyse(double[] xPhys, double penalty, double emin, double e0, double[] energies);

    /// <summary>
    /// Replaces the boundary conditions/settings on the same grid. Densities are kept,
    /// so the optimisation continues from the current layout; convergence is cleared on any change.
    /// </summary>
    public void Update(ProblemBase problem, SimpSettings settings)
    {
      if (!IsSameGrid(problem))
        throw new ArgumentException("Grid size changed; create a new optimiser instead.");

      bool kindsChanged = false;
      for (int e = 0; e < X.Length && !kindsChanged; e++) kindsChanged = problem.Kinds[e] != _problem.Kinds[e];
      bool loadsChanged = kindsChanged || problem.PoissonRatio != _problem.PoissonRatio ||
        problem.YoungsModulus != _problem.YoungsModulus || problem.MinStiffnessRatio != _problem.MinStiffnessRatio ||
        !SameArray(problem.Force, _problem.Force) || !SameArray(problem.Fixed, _problem.Fixed);
      bool settingsChanged = !settings.SameAs(_settings);

      // The filter can refuse an oversized radius; build it before anything else changes so a throw leaves the state intact.
      if (kindsChanged || settings.FilterRadius != _settings.FilterRadius)
        _filter = CreateFilter(problem, settings.FilterRadius);
      if (kindsChanged)
      {
        for (int e = 0; e < X.Length; e++)
        {
          if (problem.Kinds[e] != ElementKind.Design) X[e] = 0.0;
          else if (_problem.Kinds[e] != ElementKind.Design) X[e] = settings.VolumeFraction;
        }
      }
      // Densities seeded from a non-finite volume fraction never recover through the OC update; start those cells afresh.
      bool reseeded = false;
      if (IsFinite(settings.VolumeFraction))
      {
        for (int e = 0; e < X.Length; e++)
        {
          if (IsFinite(X[e])) continue;
          X[e] = problem.Kinds[e] == ElementKind.Design ? settings.VolumeFraction : 0.0;
          reseeded = true;
        }
      }
      OnProblemReplaced(_problem, problem);

      _problem = problem;
      _settings = settings.Clone();
      if (kindsChanged || loadsChanged || settingsChanged || reseeded)
      {
        Revision++;
        _budgetStart = Iteration;
        Converged = false;
        Change = 1.0;
        UpdatePhysical(X);
        Volume = MeasureVolume();
      }
    }

    /// <summary>One finite-element analysis plus one OC design update.</summary>
    public void Step()
    {
      var p = _problem;
      var s = _settings;
      int nel = X.Length;
      double e0 = p.YoungsModulus;
      double emin = e0 * p.MinStiffnessRatio;

      Analyse(XPhys, s.Penalty, emin, e0, _ce);

      // Compliance and its sensitivity w.r.t. the physical densities.
      double c = 0.0;
      for (int e = 0; e < nel; e++)
      {
        double xp = XPhys[e];
        c += (emin + Math.Pow(xp, s.Penalty) * (e0 - emin)) * _ce[e];
        _dc[e] = -s.Penalty * (e0 - emin) * Math.Pow(xp, s.Penalty - 1.0) * _ce[e];
        _dv[e] = 1.0;
      }
      Compliance = c;
      History.Add(c);

      if (s.Filter == FilterKind.Sensitivity)
      {
        _filter.FilterSensitivity(X, _dc, _tmp);
        Array.Copy(_tmp, _dc, nel);
      }
      else
      {
        _filter.BackPropagate(_dc, _tmp);
        Array.Copy(_tmp, _dc, nel);
        _filter.BackPropagate(_dv, _tmp);
        Array.Copy(_tmp, _dv, nel);
      }

      UpdateDesign();
      Iteration++;
      Revision++;
      Converged = Change < s.Tolerance || ObjectiveStalled();
    }

    /// <summary>
    /// Density-change convergence can take hundreds of iterations while a few grey elements flicker;
    /// by then the layout and compliance have long settled.
    /// </summary>
    bool ObjectiveStalled()
    {
      const int window = 10;
      int n = History.Count;
      if (_settings.ObjectiveTolerance <= 0.0 || Iteration - _budgetStart < 2 * window || n <= window) return false;
      for (int k = n - window; k < n; k++)
        if (Math.Abs(History[k] - History[k - 1]) > _settings.ObjectiveTolerance * Math.Abs(History[k])) return false;
      return true;
    }

    void UpdateDesign()
    {
      var kinds = _problem.Kinds;
      var s = _settings;
      int nel = X.Length;

      double target = s.VolumeFraction * AvailableCount();

      // Scale sensitivities so the multiplier bracket below always fits the problem's units.
      double scale = 0.0;
      int nd = 0;
      for (int e = 0; e < nel; e++)
        if (kinds[e] == ElementKind.Design) { scale += Math.Abs(_dc[e]); nd++; }
      if (nd == 0) { Change = 0.0; return; }
      scale = scale > 0.0 ? nd / scale : 1.0;

      // Bounded bisection: when a raised volume fraction cannot be met within the move limit, l1 stays 0 and the
      // bracket never closes, so the pass count is capped and the multiplier floored to keep every density finite.
      // Densities stay strictly positive because the multiplicative update can never regrow an exact zero.
      double l1 = 0.0, l2 = 1e9;
      for (int pass = 0; pass < 120 && (l2 - l1) / (l1 + l2) > 1e-4; pass++)
      {
        double lmid = Math.Max(0.5 * (l1 + l2), 1e-30);
        for (int e = 0; e < nel; e++)
        {
          if (kinds[e] != ElementKind.Design) { _xNew[e] = 0.0; continue; }
          double x = X[e];
          double b = Math.Max(0.0, -_dc[e] * scale) / (Math.Max(1e-12, _dv[e]) * lmid);
          double xn = x * Math.Sqrt(b);
          xn = Math.Min(xn, Math.Min(1.0, x + s.MoveLimit));
          xn = Math.Max(xn, Math.Max(MinDensity, x - s.MoveLimit));
          _xNew[e] = xn;
        }
        UpdatePhysical(_xNew);
        if (MeasureMaterial() > target) l1 = lmid; else l2 = lmid;
      }

      double change = 0.0;
      for (int e = 0; e < nel; e++)
      {
        if (kinds[e] != ElementKind.Design) continue;
        change = Math.Max(change, Math.Abs(_xNew[e] - X[e]));
        X[e] = _xNew[e];
      }
      Change = change;
      UpdatePhysical(X);
      Volume = MeasureVolume();
    }

    void UpdatePhysical(double[] x)
    {
      var kinds = _problem.Kinds;
      if (_settings.Filter == FilterKind.Density) _filter.FilterDensity(x, XPhys);
      for (int e = 0; e < XPhys.Length; e++)
      {
        switch (kinds[e])
        {
          case ElementKind.Design:
            if (_settings.Filter != FilterKind.Density) XPhys[e] = x[e];
            break;
          case ElementKind.Solid: XPhys[e] = 1.0; break;
          default: XPhys[e] = 0.0; break;
        }
      }
    }

    int AvailableCount()
    {
      int n = 0;
      foreach (var k in _problem.Kinds)
        if (k == ElementKind.Design || k == ElementKind.Solid) n++;
      return n;
    }

    double MeasureMaterial()
    {
      double v = 0.0;
      var kinds = _problem.Kinds;
      for (int e = 0; e < XPhys.Length; e++)
        if (kinds[e] == ElementKind.Design || kinds[e] == ElementKind.Solid) v += XPhys[e];
      return v;
    }

    double MeasureVolume()
    {
      int n = AvailableCount();
      return n > 0 ? MeasureMaterial() / n : 0.0;
    }

    static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    static bool SameArray<T>(T[] a, T[] b) where T : IEquatable<T>
    {
      if (a.Length != b.Length) return false;
      for (int i = 0; i < a.Length; i++)
        if (!a[i].Equals(b[i])) return false;
      return true;
    }
  }
}
