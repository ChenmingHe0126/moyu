using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.Core;
using Moyu.TwoD;

namespace Moyu.Components.TwoD
{
  public class SolverComponent : SolverComponentBase<PixelDomain2D, Problem2D>
  {
    public SolverComponent()
      : base("SIMP Solver 2D", "Solver",
        "Minimum-compliance topology optimisation (SIMP + optimality criteria) on the pixel domain. " +
        "Drive it with a Timer (Params › Util › Trigger): every tick with Run = true performs Steps iterations.",
        "2D")
    {
    }

    protected override string Dimension => "2D";
    protected override string Cells => "pixels";
    protected override double DefaultVolumeFraction => 0.4;
    protected override double DefaultFilterRadius => 2.0;
    protected override int DefaultMaxIterations => 200;
    protected override int DefaultSteps => 5;

    protected override Built Build(PixelDomain2D domain, IGH_DataAccess DA)
    {
      var errors = new List<string>();
      var loads = Collect<Load2D>(DA, 1, "Loads", errors);
      var supports = Collect<Support2D>(DA, 2, "Supports", errors);
      var areas = Collect<Area2D>(DA, 3, "Areas", errors);
      IGH_Goo materialGoo = null;
      var material = DA.GetData(4, ref materialGoo) && MoyuGoo<Material2D>.TryGet(materialGoo, out var m) ? m : Material2D.Default;

      foreach (var e in errors) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e);
      if (errors.Count > 0) return null;
      if (loads.Count == 0 || supports.Count == 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect at least one load and one support.");
        return null;
      }

      var output = ProblemBuilder2D.Build(domain, loads, supports, areas, material, out string error);
      if (output == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
        return null;
      }
      var built = new Built { Problem = output.Problem, Marks = output };
      built.Warnings.AddRange(output.Warnings);
      return built;
    }

    protected override SimpOptimizer CreateOptimizer(Problem2D problem, SimpSettings settings) =>
      new SimpOptimizer2D(problem, settings);

    protected override bool Fits(SimpOptimizer optimizer, Problem2D problem) =>
      optimizer is SimpOptimizer2D o && o.Grid.Nx == problem.Grid.Nx && o.Grid.Ny == problem.Grid.Ny;

    protected override IGH_Goo CreateResult(PixelDomain2D domain, Built built, SimpOptimizer optimizer)
    {
      var output = (ProblemBuilder2D.Output)built.Marks;
      return new MoyuGoo<Result2D>(new Result2D
      {
        Domain = domain,
        Kinds = built.Problem.Kinds,
        Density = (double[])optimizer.XPhys.Clone(),
        Iteration = optimizer.Iteration,
        Compliance = optimizer.Compliance,
        Volume = optimizer.Volume,
        Change = optimizer.Change,
        Converged = optimizer.Converged,
        History = optimizer.History.ToArray(),
        Loads = output.Loads,
        Supports = output.Supports,
        Revision = optimizer.Revision,
      });
    }

    public override Guid ComponentGuid => new Guid("0ccc0783-dd2c-45aa-ade8-96778cbd4815");
  }
}
