using System;
using System.Collections.Generic;

// No Unity dependency: the same selection/partition code is checked offline.
internal static class BossWallSelectionMath
{
    internal readonly struct Point
    {
        public readonly double X;
        public readonly double Y;
        public Point(double x, double y) { X = x; Y = y; }
    }

    private const double Epsilon = 1e-8;

    internal static bool Contains(IReadOnlyList<Point> polygon, Point point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Point a = polygon[j], b = polygon[i];
            if (OnSegment(a, b, point)) return true;
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    internal static bool IsSimplePolygon(IReadOnlyList<Point> points)
    {
        if (points == null || points.Count < 3) return false;
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Point a = points[i], b = points[(i + 1) % points.Count];
            if (double.IsNaN(a.X) || double.IsNaN(a.Y)
                || double.IsInfinity(a.X) || double.IsInfinity(a.Y)) return false;
            if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) <= Epsilon) return false;
            // Adjacent edges may share their endpoint, but may not double back.
            Point previous = points[(i + points.Count - 1) % points.Count];
            if (Math.Abs(Cross(previous, a, b)) <= Epsilon
                && (previous.X - a.X) * (b.X - a.X)
                    + (previous.Y - a.Y) * (b.Y - a.Y) > Epsilon) return false;
            area += a.X * b.Y - b.X * a.Y;
            for (int j = i + 1; j < points.Count; j++)
            {
                if (j == i + 1 || (i == 0 && j == points.Count - 1)) continue;
                if (Intersects(a, b, points[j], points[(j + 1) % points.Count])) return false;
            }
        }
        return Math.Abs(area) > Epsilon;
    }

    internal static void Partition(int[] triangles, bool[] selected,
        out int[] remaining, out int[] extracted)
    {
        if (triangles == null || selected == null
            || triangles.Length % 3 != 0 || triangles.Length / 3 != selected.Length)
            throw new ArgumentException("Triangle selection does not match the source mesh.");
        var keep = new List<int>(triangles.Length);
        var cut = new List<int>();
        for (int triangle = 0; triangle < selected.Length; triangle++)
        {
            List<int> output = selected[triangle] ? cut : keep;
            int offset = triangle * 3;
            output.Add(triangles[offset]);
            output.Add(triangles[offset + 1]);
            output.Add(triangles[offset + 2]);
        }
        if (keep.Count == 0 || cut.Count == 0)
            throw new ArgumentException("Select a non-empty part, not the entire wall.");
        remaining = keep.ToArray();
        extracted = cut.ToArray();
    }

    private static double Cross(Point a, Point b, Point c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool OnSegment(Point a, Point b, Point p) =>
        Math.Abs(Cross(a, b, p)) <= Epsilon
        && p.X >= Math.Min(a.X, b.X) - Epsilon && p.X <= Math.Max(a.X, b.X) + Epsilon
        && p.Y >= Math.Min(a.Y, b.Y) - Epsilon && p.Y <= Math.Max(a.Y, b.Y) + Epsilon;

    private static bool Intersects(Point a, Point b, Point c, Point d)
    {
        double abC = Cross(a, b, c), abD = Cross(a, b, d);
        double cdA = Cross(c, d, a), cdB = Cross(c, d, b);
        return ((abC > Epsilon && abD < -Epsilon || abC < -Epsilon && abD > Epsilon)
                && (cdA > Epsilon && cdB < -Epsilon || cdA < -Epsilon && cdB > Epsilon))
            || OnSegment(a, b, c) || OnSegment(a, b, d)
            || OnSegment(c, d, a) || OnSegment(c, d, b);
    }
}
