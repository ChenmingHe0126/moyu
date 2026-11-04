using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Moyu.Core;

namespace Moyu.Components
{
  /// <summary>
  /// Trigger-driven SIMP solver shared by the 2D and 3D components. Each solution with Run = true advances the
  /// optimisation by Steps iterations; a Grasshopper Timer (Trigger) attached to the component keeps it going
  /// until it converges. Loads, supports, areas and settings may change mid-run: the densities carry over.
  /// Inputs 0–4 are Domain, Loads, Supports, Areas, Material; subclasses read 1–4 in <see cref="Build"/>.
  /// </summary>
  public abstract class SolverComponentBase<TDomain, TProblem> : MoyuComponent
    where TDomain : class
    where TProblem : ProblemBase
  {
    protected sealed class Built
    {
      public TProblem Problem;
      public readonly List<string> Warnings = new List<string>();
      /// <summary>Whatever the subclass needs to build its result (e.g. load and support marks).</summary>
      public object Marks;
    }

    SimpOptimizer _optimizer;
    IGH_Goo _result;
    TDomain _resultDomain;
    SimpOptimizer _resultSource;
    int _resultRevision = -1;

    protected SolverComponentBase(string name, string nickname, string description, string subCategory)
      : base(name, nickname, description, subCategory) { }

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    /// <summary>"2D" or "3D", used in parameter descriptions.</summary>
    protected abstract string Dimension { get; }
    /// <summary>"pixels" or "voxels".</summary>
    protected abstract string Cells { get; }
    protected abstract double DefaultVolumeFraction { get; }
    protected abstract double DefaultFilterRadius { get; }
    protected abstract int DefaultMaxIterations { get; }
    protected abstract int DefaultSteps { get; }
    /// <summary>Converged once compliance changes less than this (relative) per iteration for 10 iterations.</summary>
    protected virtual double ObjectiveTolerance => 1e-4;
    /// <summary>Upper bound of the Filter Radius input: the neighbour tables grow with R² (2D) or R³ (3D).</summary>
    protected virtual double MaxFilterRadius => Dimension == "3D" ? 6.0 : 12.0;
    /// <summary>Upper bound of the Steps input.</summary>
    const int MaxSteps = 50;
    /// <summary>Wall time after which a solution starts no further iteration; the Timer's next tick carries on.</summary>
    const long StepBudgetMs = 1500;

    /// <summary>Reads inputs 1–4 and maps them onto the domain. Returns null after reporting why.</summary>
    protected abstract Built Build(TDomain domain, IGH_DataAccess DA);
    protected abstract SimpOptimizer CreateOptimizer(TProblem problem, SimpSettings settings);
    protected abstract bool Fits(SimpOptimizer optimizer, TProblem problem);
    protected abstract IGH_Goo CreateResult(TDomain domain, Built built, SimpOptimizer optimizer);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      string d = Dimension;
      pManager.AddGenericParameter("Domain", "D", $"Domain from Boundary {d}.", GH_ParamAccess.item);
      pManager.AddGenericParameter("Loads", "L", $"Loads from Load {d}.", GH_ParamAccess.list);
      pManager.AddGenericParameter("Supports", "S", $"Supports from Support {d}.", GH_ParamAccess.list);
      pManager.AddGenericParameter("Areas", "A", $"Optional solid/void areas from Area {d}.", GH_ParamAccess.list);
      pManager.AddGenericParameter("Material", "M", $"Optional material from Material {d} (default E = 1, ν = 0.3).", GH_ParamAccess.item);
      pManager.AddNumberParameter("Volume Fraction", "V", "Share of the available region (boundary minus void areas) filled with material.", GH_ParamAccess.item, DefaultVolumeFraction);
      pManager.AddNumberParameter("Penalty", "P", "SIMP penalty; 3 is standard. Higher pushes harder towards solid and empty.", GH_ParamAccess.item, 3.0);
      pManager.AddNumberParameter("Filter Radius", "R", $"Density filter radius in {Cells}; sets the thinnest member (≈ 2R {Cells}). Minimum 1 = no filter, maximum {MaxFilterRadius:0}.", GH_ParamAccess.item, DefaultFilterRadius);
      pManager.AddIntegerParameter("Max Iterations", "N", "Stop after this many iterations even if not converged. Counted again from zero whenever an input changes.", GH_ParamAccess.item, DefaultMaxIterations);
      pManager.AddIntegerParameter("Steps", "St",
        $"Iterations per trigger tick, at most {MaxSteps}. A tick stops starting new iterations after {StepBudgetMs / 1000.0:0.#} s (or on Esc) so Rhino stays responsive; it always runs at least one.",
        GH_ParamAccess.item, DefaultSteps);
      pManager.AddBooleanParameter("Run", "Run", "Iterate while true. Use a Toggle plus a Timer for continuous solving.", GH_ParamAccess.item, false);
      pManager.AddBooleanParameter("Reset", "Rst", "Restart from a uniform density. Use a Button.", GH_ParamAccess.item, false);
      pManager[3].Optional = true;
      pManager[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Result", "R", $"Optimisation state; connect to Visualize {Dimension}.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Iteration", "I", "Iterations done.", GH_ParamAccess.item);
      pManager.AddNumberParameter("Compliance", "C", "Compliance (work done by the loads) of the last analysed layout; lower is stiffer.", GH_ParamAccess.item);
      pManager.AddNumberParameter("Volume", "V", "Current material share of the available region.", GH_ParamAccess.item);
      pManager.AddBooleanParameter("Converged", "Done",
        $"True once densities change by less than 1% per iteration, or compliance has changed by less than {100 * ObjectiveTolerance:0.##}% per iteration for 10 iterations.", GH_ParamAccess.item);
      pManager.AddNumberParameter("History", "H", "Compliance per iteration.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      if (DA.Iteration > 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "One solver handles one domain; use a separate solver per extra domain.");
        return;
      }
      IGH_Goo domainGoo = null;
      DA.GetData(0, ref domainGoo);
      if (!MoyuGoo<TDomain>.TryGet(domainGoo, out var domain))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Domain input needs the output of Boundary {Dimension}.");
        return;
      }

      double volFrac = DefaultVolumeFraction, penal = 3.0, radius = DefaultFilterRadius;
      int maxIter = DefaultMaxIterations, steps = DefaultSteps;
      bool run = false, reset = false;
      DA.GetData(5, ref volFrac);
      DA.GetData(6, ref penal);
      DA.GetData(7, ref radius);
      DA.GetData(8, ref maxIter);
      DA.GetData(9, ref steps);
      DA.GetData(10, ref run);
      DA.GetData(11, ref reset);

      var built = Build(domain, DA);
      if (built == null)
      {
        // Outputs are empty now, so the last run's "Converged @ N" must not linger.
        Message = RuntimeMessageLevel == GH_RuntimeMessageLevel.Error ? "Error" : "Waiting";
        return;
      }
      foreach (var w in built.Warnings.Distinct()) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);

      var settings = new SimpSettings
      {
        VolumeFraction = Clamp(volFrac, 0.01, 1.0, DefaultVolumeFraction, "Volume fraction"),
        Penalty = Clamp(penal, 1.0, 8.0, 3.0, "Penalty"),
        FilterRadius = Clamp(radius, 1.0, MaxFilterRadius, DefaultFilterRadius, "Filter radius"),
        MaxIterations = Math.Max(1, maxIter),
        ObjectiveTolerance = ObjectiveTolerance,
      };
      if (steps > MaxSteps) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Steps clamped to [1, {MaxSteps}].");
      steps = Math.Max(1, Math.Min(MaxSteps, steps));

      // Solid areas count against the material budget; if they use it all, every design cell empties out.
      int solid = built.Problem.Kinds.Count(k => k == ElementKind.Solid);
      int available = built.Problem.Kinds.Count(k => k == ElementKind.Design || k == ElementKind.Solid);
      if (available > 0 && solid >= 0.9 * settings.VolumeFraction * available)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
          $"Solid areas already use {100.0 * solid / available:F1}% of the region, close to or above the volume fraction " +
          $"({100 * settings.VolumeFraction:F0}%). Little or no material is left for the structure; raise Volume Fraction.");

      // The filter, the stiffness factor and the multigrid hierarchy are built lazily and refuse sizes that would
      // not fit in memory; anything that still slips through surfaces as an allocation failure, reported the same way.
      SimpOptimizer opt;
      int done = 0;
      bool timedOut = false, escaped = false;
      try
      {
        if (reset || _optimizer == null || !Fits(_optimizer, built.Problem))
          _optimizer = CreateOptimizer(built.Problem, settings);
        else
          _optimizer.Update(built.Problem, settings);
        opt = _optimizer;
        // The loop runs on the UI thread, so a solution always makes progress but never holds Rhino much past the budget.
        var budget = Stopwatch.StartNew();
        while (run && !reset && done < steps && !opt.IsFinished)
        {
          if (done > 0 && budget.ElapsedMilliseconds >= StepBudgetMs) { timedOut = true; break; }
          if (done > 0 && GH_Document.IsEscapeKeyDown()) { escaped = true; break; }
          opt.Step();
          done++;
        }
      }
      catch (InvalidOperationException ex)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        Message = ex.Message.StartsWith("The structure is not stable") ? "Unstable" : "Error";
        return;
      }
      catch (Exception ex) when (ex is OutOfMemoryException || ex is ArgumentOutOfRangeException || ex is OverflowException)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Out of memory: lower the resolution or the filter radius.");
        Message = "Error";
        return;
      }
      if (escaped) OnPingDocument()?.RequestAbortSolution();
      else if (timedOut)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
          $"Stopped after {done} of {steps} iteration(s): a solution runs for at most {StepBudgetMs / 1000.0:0.#} s so Rhino stays responsive.");
      if (run && !reset && !opt.IsFinished && !HasTrigger())
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
          $"No Timer drives this solver, so each solution only runs up to {steps} iteration(s). Attach a Timer (Params › Util › Trigger) to keep it going.");

      if (_result == null || _resultRevision != opt.Revision || !ReferenceEquals(_resultDomain, domain) || !ReferenceEquals(_resultSource, opt))
      {
        _result = CreateResult(domain, built, opt);
        _resultSource = opt;
        _resultRevision = opt.Revision;
        _resultDomain = domain;
      }

      Message = opt.Converged ? $"Converged @ {opt.Iteration}"
        : opt.ReachedMaxIterations ? $"Max iter {opt.Iteration}"
        : opt.Iteration == 0 ? "Ready"
        : run ? $"Running @ {opt.Iteration}"
        : $"Paused @ {opt.Iteration}";

      DA.SetData(0, _result);
      DA.SetData(1, opt.Iteration);
      if (opt.Iteration > 0) DA.SetData(2, opt.Compliance);
      DA.SetData(3, opt.Volume);
      DA.SetData(4, opt.Converged);
      DA.SetDataList(5, opt.History);
    }

    /// <summary>Limits a number input to [lo, hi]; NaN and infinities (e.g. 0/0 from an Expression) fall back to the default.</summary>
    double Clamp(double v, double lo, double hi, double dflt, string what)
    {
      if (double.IsNaN(v) || double.IsInfinity(v))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{what} is not a number; using {dflt}.");
        return dflt;
      }
      if (v >= lo && v <= hi) return v;
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{what} clamped to [{lo}, {hi}].");
      return Math.Min(hi, Math.Max(lo, v));
    }

    /// <summary>Any Timer counts: one aimed at the Run toggle or an upstream component expires this solver just the same.</summary>
    bool HasTrigger()
    {
      var doc = OnPingDocument();
      return doc != null && doc.Objects.OfType<GH_Timer>().Any();
    }

    public override void RemovedFromDocument(GH_Document document)
    {
      _optimizer = null;
      _result = null;
      base.RemovedFromDocument(document);
    }
  }
}
