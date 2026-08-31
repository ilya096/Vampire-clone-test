using System;
using System.Linq;
using P = BossWallSelectionMath.Point;

internal static class BossWallSelectionChecks
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    private static void MustReject(Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, label);
    }

    public static void Main()
    {
        P[] square = { new(-2, -2), new(2, -2), new(2, 2), new(-2, 2) };
        Check(BossWallSelectionMath.IsSimplePolygon(square), "Square valid");
        Check(BossWallSelectionMath.Contains(square, new P(0, 0)), "Interior");
        Check(!BossWallSelectionMath.Contains(square, new P(3, 0)), "Exterior");
        Check(BossWallSelectionMath.Contains(square, new P(2, 1)), "Boundary edge inclusive");
        Check(BossWallSelectionMath.Contains(square, new P(-2, -2)), "Boundary vertex inclusive");
        Check(BossWallSelectionMath.IsSimplePolygon(square.Reverse().ToArray()), "Reverse winding valid");
        Check(BossWallSelectionMath.Contains(square.Reverse().ToArray(), new P(1, 0)), "Winding independent");
        P[] concave = { new(0, 0), new(4, 0), new(4, 1), new(1, 1), new(1, 4), new(0, 4) };
        Check(BossWallSelectionMath.IsSimplePolygon(concave), "Concave valid");
        Check(BossWallSelectionMath.Contains(concave, new P(0.5, 3)), "Concave arm");
        Check(!BossWallSelectionMath.Contains(concave, new P(3, 3)), "Concave notch excluded");
        Check(!BossWallSelectionMath.IsSimplePolygon(new[] { new P(0, 0), new P(2, 2), new P(0, 2), new P(2, 0) }), "Bow tie rejected");
        Check(!BossWallSelectionMath.IsSimplePolygon(new[] { new P(0, 0), new P(2, 0), new P(1, 0) }), "Zero area rejected");
        Check(!BossWallSelectionMath.IsSimplePolygon(new[] { new P(0, 0), new P(2, 0), new P(1, 0), new P(1, 2) }), "Adjacent overlap rejected");
        Check(!BossWallSelectionMath.IsSimplePolygon(new[] { new P(0, 0), new P(2, 0), new P(2, 0), new P(0, 2) }), "Duplicate rejected");
        Check(!BossWallSelectionMath.IsSimplePolygon(new[] { new P(double.NaN, 0), new P(2, 0), new P(0, 2) }), "NaN rejected");
        Check(!BossWallSelectionMath.IsSimplePolygon(Array.Empty<P>()), "Empty contour rejected");
        // Simulates the inner ring and nearby outer wall in a translated/scaled top view.
        foreach (double scale in new[] { 0.05, 1.0, 60.0 })
        {
            P[] moved = square.Select(p => new P(p.X * scale + 123, p.Y * scale - 45)).ToArray();
            Check(BossWallSelectionMath.IsSimplePolygon(moved), "Transformed polygon valid");
            for (int i = 0; i < 24; i++)
            {
                double angle = i * Math.PI / 12;
                Check(BossWallSelectionMath.Contains(moved, new P(123 + Math.Cos(angle) * scale, -45 + Math.Sin(angle) * scale)), "Inner wall selected");
                Check(!BossWallSelectionMath.Contains(moved, new P(123 + Math.Cos(angle) * scale * 3, -45 + Math.Sin(angle) * scale * 3)), "Outer wall retained");
            }
        }
        int[] triangles = { 5, 2, 9, 9, 2, 6, 6, 2, 8, 8, 3, 7, 7, 4, 1, 1, 4, 5 };
        for (int bits = 1; bits < 63; bits++)
        {
            bool[] mask = Enumerable.Range(0, 6).Select(i => (bits & (1 << i)) != 0).ToArray();
            BossWallSelectionMath.Partition(triangles, mask, out int[] kept, out int[] cut);
            Check(kept.Length + cut.Length == triangles.Length, "No triangle loss");
            int k = 0, c = 0;
            for (int i = 0; i < mask.Length; i++)
                for (int corner = 0; corner < 3; corner++)
                    Check((mask[i] ? cut[c++] : kept[k++]) == triangles[i * 3 + corner], "Exact index order/winding preserved");
        }
        MustReject(() => BossWallSelectionMath.Partition(triangles, new bool[6], out _, out _), "Empty selection");
        MustReject(() => BossWallSelectionMath.Partition(triangles, Enumerable.Repeat(true, 6).ToArray(), out _, out _), "Whole wall");
        MustReject(() => BossWallSelectionMath.Partition(triangles, new bool[5], out _, out _), "Stale mask");
        MustReject(() => BossWallSelectionMath.Partition(new[] { 0, 1 }, new bool[1], out _, out _), "Malformed triangles");
        Console.WriteLine($"PASS: {_checks} boss wall selection checks. No Unity scene/assets modified.");
    }
}
