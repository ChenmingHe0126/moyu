using System;
using System.Collections.Generic;

namespace Moyu.Games.Stack.Core
{
  /// <summary>
  /// The pit pieces fall into: Nx × Ny × Nz cells, z up. A cell holds 0 when empty, else the ARGB colour of the piece
  /// that filled it. Cells above the rim (z ≥ Nz) count as free so a piece may poke out while it falls.
  /// </summary>
  public sealed class Well
  {
    public readonly int Nx, Ny, Nz;
    readonly int[] _cells;

    /// <summary>Bumped on every change, so renderers know when to rebuild their meshes.</summary>
    public int Version { get; private set; }

    public Well(int nx, int ny, int nz)
    {
      if (nx < 2 || ny < 2 || nz < 4) throw new ArgumentOutOfRangeException(nameof(nx), "A well needs at least 2 × 2 × 4 cells.");
      Nx = nx;
      Ny = ny;
      Nz = nz;
      _cells = new int[nx * ny * nz];
    }

    public int LayerSize => Nx * Ny;
    int Index(int x, int y, int z) => (z * Ny + y) * Nx + x;
    public bool InColumns(Int3 c) => c.X >= 0 && c.X < Nx && c.Y >= 0 && c.Y < Ny;

    /// <summary>0 when empty or outside the well.</summary>
    public int Get(int x, int y, int z) => x < 0 || x >= Nx || y < 0 || y >= Ny || z < 0 || z >= Nz ? 0 : _cells[Index(x, y, z)];
    public int Get(Int3 c) => Get(c.X, c.Y, c.Z);
    public bool Filled(Int3 c) => Get(c) != 0;

    /// <summary>Inside the walls, above the floor and not occupied.</summary>
    public bool IsFree(Int3 c) => InColumns(c) && c.Z >= 0 && (c.Z >= Nz || _cells[Index(c.X, c.Y, c.Z)] == 0);

    public void Set(Int3 c, int value)
    {
      if (!InColumns(c) || c.Z < 0 || c.Z >= Nz) throw new ArgumentOutOfRangeException(nameof(c), $"{c} lies outside the well.");
      _cells[Index(c.X, c.Y, c.Z)] = value;
      Version++;
    }

    public int Count
    {
      get
      {
        int n = 0;
        foreach (var v in _cells) if (v != 0) n++;
        return n;
      }
    }

    public int LayerCount(int z)
    {
      int n = 0;
      for (int i = 0; i < LayerSize; i++) if (_cells[z * LayerSize + i] != 0) n++;
      return n;
    }

    /// <summary>Highest filled layer + 1 (0 when empty).</summary>
    public int Height
    {
      get
      {
        for (int z = Nz - 1; z >= 0; z--) if (LayerCount(z) > 0) return z + 1;
        return 0;
      }
    }

    public List<int> FullLayers()
    {
      var full = new List<int>();
      for (int z = 0; z < Nz; z++) if (LayerCount(z) == LayerSize) full.Add(z);
      return full;
    }

    /// <summary>Deletes the given layers and drops everything above them; empty layers come in at the top.</summary>
    public void RemoveLayers(IEnumerable<int> layers)
    {
      var remove = new HashSet<int>(layers);
      if (remove.Count == 0) return;
      int write = 0;
      for (int z = 0; z < Nz; z++)
      {
        if (remove.Contains(z)) continue;
        if (write != z) Array.Copy(_cells, z * LayerSize, _cells, write * LayerSize, LayerSize);
        write++;
      }
      Array.Clear(_cells, write * LayerSize, (Nz - write) * LayerSize);
      Version++;
    }

    public void Clear()
    {
      Array.Clear(_cells, 0, _cells.Length);
      Version++;
    }

    /// <summary>Filled cells, z up.</summary>
    public IEnumerable<(Int3 cell, int colour)> Cells()
    {
      for (int z = 0; z < Nz; z++)
        for (int y = 0; y < Ny; y++)
          for (int x = 0; x < Nx; x++)
          {
            int v = _cells[Index(x, y, z)];
            if (v != 0) yield return (new Int3(x, y, z), v);
          }
    }
  }
}
