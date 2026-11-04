using System;
using System.Drawing;
using System.Reflection;
using Grasshopper;
using Grasshopper.Kernel;

namespace Moyu
{
  public class MoyuInfo : GH_AssemblyInfo
  {
    public override string Name => "Moyu";

    //Return a 24x24 pixel bitmap to represent this GHA library.
    public override Bitmap Icon => Components.Icons.Get("Moyu");

    //Return a short string describing the purpose of this GHA library (the csproj Description, so the package manifest and this stay in step).
    public override string Description =>
      GetType().Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ??
      "Topology optimisation for Grasshopper (2D pixel and 3D voxel SIMP), plus four small viewport games disguised as massing, landscape, facade and section studies.";

    public override Guid Id => new Guid("b27a1bf2-7386-436b-846f-b4f9002b5395");

    //Return a string identifying you or your company.
    public override string AuthorName => "Ming";

    //Return a string representing your preferred contact details.
    public override string AuthorContact => "https://github.com/ChenmingHe0126/moyu";

    //Return a string representing the version: the csproj Version as the informational version without its "+commit" build metadata,
    //so a pre-release suffix (1.0.1-beta) is kept and this matches the package version in manifest.yml exactly.
    public override string AssemblyVersion =>
      GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ??
      GetType().Assembly.GetName().Version.ToString(3);

    //MIT licence (LICENSE in the repository root and in the package).
    public override GH_LibraryLicense License => GH_LibraryLicense.opensource;
  }
}
