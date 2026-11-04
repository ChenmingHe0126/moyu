using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.Core;
using Moyu.ThreeD;

namespace Moyu.Components.ThreeD
{
  public class Solver3DComponent : SolverComponentBase<VoxelDomain3D, Problem3D>
  {
    public Solver3DComponent()
      : base("SIMP Solver 3D", "Solver",
        "Minimum-compliance topology optimisation (SIMP + optimality criteria) on the voxel domain, solved with multigrid CG. " +
        "Drive it with a Timer (Params › Util › Trigger): every tick with Run = true performs Steps iterations.",
        "3D")
    {
    }

    protected override string Dimension => "3D";
    protected override string Cells => "voxels";
    protected override double DefaultVolumeFraction => 0.2;
    protected override double DefaultFilterRadius => 1.5;
    protected override int DefaultMaxIterations => 150;
    protected override int DefaultSteps => 2;
    // 3D layouts keep refining for a long time at a fraction of a percent per iteration; stop earlier.
    protected override double ObjectiveTolerance => 5e-4;

    protected override Built Build(VoxelDomain3D domain, IGH_DataAccess DA)
    {
      var errors = new List<string>();
      var loads = Collect<Load3D>(DA, 1, "Loads", errors);
      var supports = Collect<Support3D>(DA, 2, "Supports", errors);
      var areas = Collect<Area3D>(DA, 3, "Areas", errors);
      IGH_Goo materialGoo = null;
      var material = DA.GetData(4, ref materialGoo) && MoyuGoo<Material3D>.TryGet(materialGoo, out var m) ? m : Material3D.Default;

      foreach (var e in errors) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e);
      if (errors.Count > 0) return null;
      if (loads.Count == 0 || supports.Count == 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect at least one load and one support.");
        return null;
      }

      var output = ProblemBuilder3D.Build(domain, loads, supports, areas, material, out string error);
      if (output == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
        return null;
      }
      var built = new Built { Problem = output.Problem, Marks = output };
      built.Warnings.AddRange(output.Warnings);
      return built;
    }

    protected override SimpOptimizer CreateOptimizer(Problem3D problem, SimpSettings settings) =>
      new SimpOptimizer3D(problem, settings);

    protected override bool Fits(SimpOptimizer optimizer, Problem3D problem) =>
      optimizer is SimpOptimizer3D o && o.Grid.Nx == problem.Grid.Nx && o.Grid.Ny == problem.Grid.Ny && o.Grid.Nz == problem.Grid.Nz;

    protected override IGH_Goo CreateResult(VoxelDomain3D domain, Built built, SimpOptimizer optimizer)
    {
      var output = (ProblemBuilder3D.Output)built.Marks;
      return new MoyuGoo<Result3D>(new Result3D
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

    public override Guid ComponentGuid => new Guid("ebbb8167-2279-4eaf-a62d-75e2226fb21b");
  }
}
