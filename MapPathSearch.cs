// Constraint-satisfying DFS to recover the historical path through
// the new map. Map gen is seeded so the new map's graph and node
// types are identical to the original run's. Each historical floor
// records a MapPointType; if we line them up against the map, only
// one (or a few) coord chains satisfy the type sequence from
// StartingMapPoint to target.
//
// The greedy "pick leftmost matching child at each row" approach
// fails whenever a row has multiple matching candidates and the
// downstream path only routes through a specific one. Example
// observed in TBESFXYS6G act 0 retry-to-floor-12: row 5 has
// Monster at cols 2 and 6; greedy picks col 2 but only col 6's
// subtree reaches (12,5,Unknown) matching history. Backtracking
// finds the right pick.
//
// Search behavior:
//   • If history[0] is Ancient, start from StartingMapPoint as
//     row 0 and recurse on its children for row 1.
//   • Otherwise (act > 0 or act 0 without Neow), the first wanted
//     type is a row-1 type and we recurse on each StartingMapPoint
//     child individually.
//   • At each step the candidate's PointType must equal
//     wanted[idx]. First (column-sorted) match wins; backtrack on
//     dead end. Stops when idx == wanted.Count - 1.
//
// If no path exactly satisfies the type sequence (e.g. modded
// content swapped a type), the caller falls back to the older
// greedy walk.
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Map;

namespace Retry;

public static class MapPathSearch
{
    public static List<MapPoint>? FindPath(ActMap map, IReadOnlyList<MapPointType> wanted)
    {
        if (wanted.Count == 0) return null;
        var startingMapPoint = map.StartingMapPoint;
        var path = new List<MapPoint>();

        if (startingMapPoint.PointType == wanted[0])
        {
            path.Add(startingMapPoint);
            if (wanted.Count == 1) return path;
            if (Recurse(startingMapPoint, wanted, 1, path)) return path;
            return null;
        }

        // history doesn't start with Ancient → first wanted is a
        // row-1 type. Try each StartingMapPoint child as the entry
        // point for the search.
        foreach (var child in startingMapPoint.Children.OrderBy(c => c.coord.col).ThenBy(c => c.coord.row))
        {
            path.Clear();
            if (child.PointType != wanted[0]) continue;
            path.Add(child);
            if (wanted.Count == 1) return path;
            if (Recurse(child, wanted, 1, path)) return path;
        }
        return null;
    }

    private static bool Recurse(MapPoint cur, IReadOnlyList<MapPointType> wanted, int idx, List<MapPoint> path)
    {
        var want = wanted[idx];
        foreach (var child in cur.Children.OrderBy(c => c.coord.col).ThenBy(c => c.coord.row))
        {
            if (child.PointType != want) continue;
            path.Add(child);
            if (idx == wanted.Count - 1) return true;
            if (Recurse(child, wanted, idx + 1, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }
}
