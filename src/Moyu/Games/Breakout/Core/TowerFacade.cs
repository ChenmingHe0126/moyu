using System;
using System.Collections.Generic;
using System.Linq;

namespace Moyu.Games.Breakout.Core
{
  /// <summary>What fills the vision zone of one curtain-wall panel.</summary>
  public enum PanelKind { Clear, Vision, Fritted, Spandrel, Fin }

  /// <summary>
  /// The boss-key facade: the wall as it stands, read as the south elevation of an office tower with a unitised
  /// curtain wall. Every typical floor has a dark spandrel band at the slab and a row of panels above it; each
  /// panel samples the brick behind it, so the brickwork becomes the facade's pattern: broken bricks are clear
  /// vision glass, glass bricks tinted vision glass, brick fritted glass, concrete opaque spandrel panels and steel
  /// aluminium fin panels. A double-height lobby and an amenity floor make the podium; a louvred plant screen the
  /// crown. Metres.
  /// </summary>
  public sealed class TowerFacade
  {
    /// <summary>Metres per field unit of the game (the default size of the play field).</summary>
    public const double FieldModule = 2.4;
    public const double Module = 1.5, FloorHeight = 3.9, SpandrelHeight = 1.05, Lobby = 6.6, Amenity = 4.5, Crown = 9.0;
    public const int Columns = 24, Floors = 22, GridEvery = 6;

    public readonly PanelKind[,] Panels = new PanelKind[Floors, Columns];
    public double Width => Columns * Module;
    public double TypicalBase => Lobby + Amenity;
    public double Roof => TypicalBase + Floors * FloorHeight;
    public double Top => Roof + Crown;
    /// <summary>Storeys counted from the lobby to the top typical floor.</summary>
    public int Storeys => Floors + 2;

    public int PanelCount, VisionPanels, FrittedPanels, OpaquePanels, FinPanels;
    public double FacadeArea, VisionArea, Wwr, UValue, GValue, Vlt, CarbonTonnes;

    static double U(PanelKind k) => k == PanelKind.Spandrel ? 0.45 : k == PanelKind.Fin ? 0.55 : 1.3;
    static double Gv(PanelKind k) => k == PanelKind.Clear ? 0.42 : k == PanelKind.Vision ? 0.33 : k == PanelKind.Fritted ? 0.22 : 0;
    static double Tv(PanelKind k) => k == PanelKind.Clear ? 0.7 : k == PanelKind.Vision ? 0.55 : k == PanelKind.Fritted ? 0.35 : 0;
    static double Carbon(PanelKind k) => k == PanelKind.Fin ? 95 : k == PanelKind.Spandrel ? 60 : 48;

    public static PanelKind KindOf(Brick b) =>
      !b.Alive ? PanelKind.Clear : b.Material == BrickMaterial.Glass ? PanelKind.Vision : b.Material == BrickMaterial.Brick ? PanelKind.Fritted :
      b.Material == BrickMaterial.Concrete ? PanelKind.Spandrel : PanelKind.Fin;

    /// <param name="bricks">The whole wall, broken bricks included.</param>
    /// <param name="width">Field width in field units.</param>
    public TowerFacade(IReadOnlyList<Brick> bricks, double width)
    {
      var all = bricks.ToList();
      double y0 = all.Count > 0 ? all.Min(b => b.Y0) : 0, y1 = all.Count > 0 ? all.Max(b => b.Y1) : 1;
      for (int f = 0; f < Floors; f++)
        for (int c = 0; c < Columns; c++)
        {
          double u = (c + 0.5) / Columns * width, v = y0 + (f + 0.5) / Floors * (y1 - y0);
          Panels[f, c] = Sample(all, u, v);
        }
      Measure();
    }

    /// <summary>The brick under (u, v); in a joint or an empty bay, the nearest brick of that course, else plain vision glass.</summary>
    static PanelKind Sample(List<Brick> bricks, double u, double v)
    {
      var hit = bricks.FirstOrDefault(b => u >= b.X0 && u <= b.X1 && v >= b.Y0 && v <= b.Y1);
      if (hit != null) return KindOf(hit);
      var near = bricks.Where(b => v >= b.Y0 - 0.1 && v <= b.Y1 + 0.1).OrderBy(b => Math.Max(0, Math.Max(b.X0 - u, u - b.X1))).FirstOrDefault();
      return near != null && Math.Max(near.X0 - u, u - near.X1) < 1.0 ? KindOf(near) : PanelKind.Vision;
    }

    void Measure()
    {
      double panelVision = Module * (FloorHeight - SpandrelHeight), band = Module * SpandrelHeight;
      double uSum = 0, gSum = 0, tSum = 0, carbon = 0;
      foreach (var k in Panels)
      {
        PanelCount++;
        if (k == PanelKind.Clear || k == PanelKind.Vision) VisionPanels++;
        else if (k == PanelKind.Fritted) FrittedPanels++;
        else if (k == PanelKind.Spandrel) OpaquePanels++;
        else FinPanels++;
        bool glazed = k == PanelKind.Clear || k == PanelKind.Vision || k == PanelKind.Fritted;
        if (glazed) VisionArea += panelVision;
        uSum += U(k) * panelVision + 0.45 * band;
        gSum += Gv(k) * panelVision;
        tSum += Tv(k) * panelVision;
        carbon += Carbon(k) * panelVision + 60 * band;
      }
      // The podium is fully glazed at the lobby, clad at the amenity floor; the crown is louvred.
      double lobby = Width * Lobby, amenity = Width * Amenity, crown = Width * Crown;
      VisionArea += lobby * 0.9;
      FacadeArea = Width * (Top);
      uSum += lobby * 1.4 + amenity * 0.35 + crown * 0.5;
      gSum += lobby * 0.9 * 0.4;
      tSum += lobby * 0.9 * 0.7;
      carbon += lobby * 50 + amenity * 70 + crown * 40;
      Wwr = VisionArea / FacadeArea;
      UValue = uSum / FacadeArea;
      GValue = VisionArea > 0 ? gSum / VisionArea : 0;
      Vlt = VisionArea > 0 ? tSum / VisionArea : 0;
      CarbonTonnes = carbon / 1000;
    }

    /// <summary>Bottom of typical floor <paramref name="f"/> (0 = the first floor above the podium).</summary>
    public double FloorLevel(int f) => TypicalBase + f * FloorHeight;
  }
}
