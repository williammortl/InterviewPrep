namespace InterviewToolkit.Algorithms;

/*
 * Convex hull (Andrew's monotone chain)
 * -------------------------------------
 * The convex hull of a set of points is the smallest convex polygon that
 * contains every point. Rubber-band analogy: stretch a band around all of the
 * nails; the band touches the hull, and nails inside the band are not corners.
 *
 *        * * * * *
 *        *   o   *      o is inside and is not a corner
 *        * * * * *
 *
 * A polygon is convex when every turn along its boundary goes the same way.
 * This implementation walks the boundary counter-clockwise, so every turn is
 * a left turn.
 *
 * The test for a turn is the cross product of two edges that share a corner:
 *
 *   origin ----> a
 *          \
 *           \--> b
 *
 *   cross(origin, a, b) = (a.x - origin.x) * (b.y - origin.y)
 *                       - (a.y - origin.y) * (b.x - origin.x)
 *
 *   cross > 0    b is left of the directed line origin -> a   (left turn)
 *   cross < 0    b is right of that line                       (right turn)
 *   cross = 0    the three points are collinear
 *
 * Monotone chain:
 *   1. Sort the points by increasing x, and by increasing y when x ties.
 *      The leftmost point is first. The rightmost point is last.
 *   2. Walk left to right and build the lower hull. Before accepting a point,
 *      pop the previous corner while the new point would make a right turn or
 *      a flat turn. Flat turns are popped so a point in the middle of an edge
 *      is not reported as a corner.
 *   3. Walk right to left and build the upper hull with the same left-turn rule.
 *   4. Join the chains. Each chain includes both endpoints, so drop one copy of
 *      each endpoint or they would be listed twice.
 *
 *        upper hull, right to left
 *        * ---------------- *
 *        |                  |
 *        * ---------------- *   lower hull, left to right
 *
 * Sorting is O(n log n). Each point is pushed and popped at most once per chain,
 * so the two walks are O(n). Total time is O(n log n). Extra memory is O(n).
 *
 * The cross product is computed with doubles. Coordinates that are not exactly
 * representable can flip the sign of a near-zero cross product, so this is the
 * standard textbook predicate, not a robust geometric one.
 */

/// <summary>
/// Convex hull of a set of points in the plane, as a counter-clockwise polygon.
/// </summary>
public static class ConvexHull
{
    /// <summary>A point in the plane.</summary>
    public readonly record struct Point(double X, double Y)
    {
        /// <summary>Short form used when a hull is printed.</summary>
        public override string ToString() => $"({X}, {Y})";
    }

    /// <summary>
    /// Returns the corners of the convex hull in counter-clockwise order, starting
    /// at the leftmost point (the lowest one, if several share that x).
    /// Collinear edge points and duplicate points are omitted. Fewer than two
    /// distinct points returns those points unchanged.
    /// </summary>
    public static IReadOnlyList<Point> Compute(IReadOnlyList<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
            return Array.Empty<Point>();

        var sorted = new Point[points.Count];
        for (int index = 0; index < points.Count; index++)
        {
            Point point = points[index];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new ArgumentException("Every coordinate must be finite.", nameof(points));

            sorted[index] = point;
        }

        Array.Sort(sorted, static (left, right) =>
        {
            int byX = left.X.CompareTo(right.X);
            return byX != 0 ? byX : left.Y.CompareTo(right.Y);
        });

        // Duplicates sit next to each other after the sort. A repeated point is not
        // a new corner, and leaving it in would make a zero-length edge.
        int uniqueCount = 1;
        for (int index = 1; index < sorted.Length; index++)
        {
            if (sorted[index] != sorted[uniqueCount - 1])
                sorted[uniqueCount++] = sorted[index];
        }

        if (uniqueCount == 1)
            return new[] { sorted[0] };

        // Lower hull: left to right. Upper hull: right to left.
        var lower = new List<Point>(uniqueCount);
        for (int index = 0; index < uniqueCount; index++)
            AppendLeftTurn(lower, sorted[index]);

        var upper = new List<Point>(uniqueCount);
        for (int index = uniqueCount - 1; index >= 0; index--)
            AppendLeftTurn(upper, sorted[index]);

        // lower[^1] is the rightmost point, which is also upper[0].
        // upper[^1] is the leftmost point, which is also lower[0].
        // Drop those two copies when the chains are joined.
        var hull = new Point[lower.Count + upper.Count - 2];
        lower.CopyTo(0, hull, 0, lower.Count - 1);
        upper.CopyTo(0, hull, lower.Count - 1, upper.Count - 1);
        return hull;
    }

    /// <summary>
    /// Pushes <paramref name="next"/> onto the chain, first removing any tail that
    /// would no longer be a strict left turn.
    /// </summary>
    private static void AppendLeftTurn(List<Point> chain, Point next)
    {
        while (chain.Count >= 2 && Cross(chain[^2], chain[^1], next) <= 0)
            chain.RemoveAt(chain.Count - 1);

        chain.Add(next);
    }

    /// <summary>
    /// Twice the signed area of the triangle (origin, a, b). Only the sign matters:
    /// positive is a left turn, negative is a right turn, zero is collinear.
    /// </summary>
    private static double Cross(Point origin, Point a, Point b)
    {
        return ((a.X - origin.X) * (b.Y - origin.Y)) - ((a.Y - origin.Y) * (b.X - origin.X));
    }
}
