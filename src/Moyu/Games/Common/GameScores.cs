using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Moyu.Games.Common
{
  /// <summary>
  /// Local best scores, one file per game in %APPDATA%\Moyu\&lt;game&gt;-scores.txt, one "key|score|date" line per
  /// finished game. The best per key is read from the file once and then kept in memory, since the study components
  /// ask for it on every solve.
  /// </summary>
  public static class GameScores
  {
    static readonly Dictionary<(string game, string key), long> _best = new Dictionary<(string, string), long>();
    static readonly object _lock = new object();

    static string FilePath(string game) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moyu", $"{game}-scores.txt");

    public static long Best(string game, string key)
    {
      lock (_lock)
      {
        if (_best.TryGetValue((game, key), out long cached)) return cached;
        // A read that failed (another Rhino appending, a locked file) is not cached, so the next call tries again.
        if (TryReadBest(game, key, out long best)) _best[(game, key)] = best;
        return best;
      }
    }

    /// <summary>False when the file could not be read; no file or no line for the key is a best of 0.</summary>
    static bool TryReadBest(string game, string key, out long best)
    {
      best = 0;
      try
      {
        string path = FilePath(game);
        if (!File.Exists(path)) return true;
        best = File.ReadAllLines(path)
          .Select(l => l.Split('|'))
          .Where(p => p.Length >= 2 && p[0] == key)
          .Select(p => long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0)
          .DefaultIfEmpty(0)
          .Max();
        return true;
      }
      catch (Exception)
      {
        best = 0;
        return false;
      }
    }

    /// <summary>Appends the result; true when it beats the previous best.</summary>
    public static bool Record(string game, string key, long score)
    {
      long best = Best(game, key);
      // Only on top of a best that was read: after a failed read the next Best reads the file again, this line included.
      lock (_lock)
      {
        if (_best.TryGetValue((game, key), out long cached)) _best[(game, key)] = Math.Max(cached, score);
      }
      try
      {
        string path = FilePath(game);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        File.AppendAllText(path, $"{key}|{score.ToString(CultureInfo.InvariantCulture)}|{date}{Environment.NewLine}");
      }
      catch (Exception) { /* a read-only profile only costs the high score */ }
      return score > best && score > 0;
    }
  }
}
