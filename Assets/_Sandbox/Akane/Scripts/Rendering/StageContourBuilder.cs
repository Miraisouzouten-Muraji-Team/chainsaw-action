
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MeshFilterの形状をX/Y平面に投影し、
/// 複数の地形を結合した外周線を生成する。
/// </summary>
public static class StageContourBuilder
{
    private const float EPSILON = 0.00001f;
    private const float PROBE_DISTANCE = 0.001f;
    private const double KEY_PRECISION = 10000.0;
    private const int MAX_EXACT_TRIANGLES = 600;

    private sealed class Polygon
    {
        public readonly List<Vector2> Points;
        public readonly Rect Bounds;

        public Polygon(List<Vector2> points)
        {
            Points = points;
            Bounds = CreateBounds(points);
        }
    }

    private sealed class Edge
    {
        public readonly Vector2 A;
        public readonly Vector2 B;
        public readonly Rect Bounds;
        public readonly List<float> Cuts = new() { 0f, 1f };

        public Edge(Vector2 a, Vector2 b)
        {
            A = a;
            B = b;
            Bounds = Rect.MinMaxRect(
                Mathf.Min(a.x, b.x),
                Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x),
                Mathf.Max(a.y, b.y));
        }
    }

    /// <summary>
    /// メッシュ群の外周を作成する。
    /// exactTrianglesがfalseなら各メッシュの凸包を使用する。
    /// trueなら実際の三角形群を投影して結合する。
    /// </summary>
    public static List<Vector4> BuildSegments(
        IReadOnlyList<MeshFilter> filters,
        bool exactTriangles)
    {
        List<Polygon> polygons = new();
        int triangleCount = 0;

        foreach (MeshFilter filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;

            Mesh mesh = filter.sharedMesh;

            // 再構築はEditorで実行するため、頂点を取得可能にする。
            if (!mesh.isReadable)
            {
                throw new InvalidOperationException(
                    $"{filter.name}: MeshのRead/Writeを有効にしてください。");
            }

            Vector3[] vertices = mesh.vertices;

            if (exactTriangles)
            {
                int[] indices = mesh.triangles;
                triangleCount += indices.Length / 3;

                if (triangleCount > MAX_EXACT_TRIANGLES)
                {
                    throw new InvalidOperationException(
                        $"詳細検出の三角形数が上限の" +
                        $"{MAX_EXACT_TRIANGLES}を超えました。" +
                        "凸形状モードを使用してください。");
                }

                // Meshの各三角形をX/Yに投影する。
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector2 a = ProjectVertex(
                        filter.transform, vertices[indices[i]]);
                    Vector2 b = ProjectVertex(
                        filter.transform, vertices[indices[i + 1]]);
                    Vector2 c = ProjectVertex(
                        filter.transform, vertices[indices[i + 2]]);

                    float cross = Cross(b - a, c - a);

                    if (Mathf.Abs(cross) <= EPSILON)
                        continue;

                    List<Vector2> points = cross > 0f
                        ? new List<Vector2> { a, b, c }
                        : new List<Vector2> { a, c, b };

                    polygons.Add(new Polygon(points));
                }
            }
            else
            {
                // 三角柱や斜面などの凸形状を軽量に処理する。
                List<Vector2> points = new(vertices.Length);

                foreach (Vector3 vertex in vertices)
                {
                    points.Add(ProjectVertex(
                        filter.transform, vertex));
                }

                List<Vector2> hull = BuildConvexHull(points);

                if (hull.Count >= 3)
                    polygons.Add(new Polygon(hull));
            }
        }

        if (polygons.Count == 0)
            return new List<Vector4>();

        List<Edge> edges = new();

        foreach (Polygon polygon in polygons)
        {
            for (int i = 0; i < polygon.Points.Count; i++)
            {
                Vector2 a = polygon.Points[i];
                Vector2 b = polygon.Points[
                    (i + 1) % polygon.Points.Count];

                if ((b - a).sqrMagnitude <= EPSILON * EPSILON)
                    continue;

                edges.Add(new Edge(a, b));
            }
        }

        // 交差していない辺同士の精密判定を省略する。
        // この処理は外周更新時にのみ実施する。
        for (int i = 0; i < edges.Count; i++)
        {
            for (int j = i + 1; j < edges.Count; j++)
            {
                if (!BoundsOverlap(
                    edges[i].Bounds, edges[j].Bounds))
                    continue;

                AddIntersections(edges[i], edges[j]);
            }
        }

        List<Vector4> result = new();
        HashSet<(long, long, long, long)> unique = new();

        foreach (Edge edge in edges)
        {
            edge.Cuts.Sort();
            Vector2 direction = edge.B - edge.A;

            for (int i = 0; i < edge.Cuts.Count - 1; i++)
            {
                float start = edge.Cuts[i];
                float end = edge.Cuts[i + 1];

                if (end - start <= EPSILON)
                    continue;

                Vector2 a = edge.A + direction * start;
                Vector2 b = edge.A + direction * end;
                Vector2 difference = b - a;

                float length = difference.magnitude;
                if (length <= EPSILON)
                    continue;

                // 線分の両側を検査する。
                // 片側だけ地形内部なら外周線として残す。
                Vector2 normal = new(
                    -difference.y / length,
                    difference.x / length);

                Vector2 midpoint = (a + b) * 0.5f;
                float probe = Mathf.Min(
                    PROBE_DISTANCE, length * 0.25f);

                bool leftInside = IsInsideUnion(
                    midpoint + normal * probe, polygons);

                bool rightInside = IsInsideUnion(
                    midpoint - normal * probe, polygons);

                if (leftInside == rightInside)
                    continue;

                var aKey = Quantize(a);
                var bKey = Quantize(b);

                if (aKey.Item1 > bKey.Item1 ||
                    (aKey.Item1 == bKey.Item1 &&
                     aKey.Item2 > bKey.Item2))
                {
                    (aKey, bKey) = (bKey, aKey);
                }

                var key = (
                    aKey.Item1, aKey.Item2,
                    bKey.Item1, bKey.Item2);

                if (!unique.Add(key))
                    continue;

                result.Add(new Vector4(
                    a.x, a.y, b.x, b.y));
            }
        }

        return result;
    }

    private static Vector2 ProjectVertex(
        Transform transform, Vector3 vertex)
    {
        Vector3 world = transform.TransformPoint(vertex);
        return new Vector2(world.x, world.y);
    }

    private static List<Vector2> BuildConvexHull(
        List<Vector2> points)
    {
        if (points.Count < 3)
            return new List<Vector2>();

        points.Sort((a, b) =>
        {
            int result = a.x.CompareTo(b.x);
            return result != 0 ? result : a.y.CompareTo(b.y);
        });

        List<Vector2> hull = new();

        foreach (Vector2 point in points)
        {
            while (hull.Count >= 2 &&
                   Cross(
                       hull[hull.Count - 1] - hull[hull.Count - 2],
                       point - hull[hull.Count - 1]) <= EPSILON)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        int lowerCount = hull.Count;

        for (int i = points.Count - 2; i >= 0; i--)
        {
            Vector2 point = points[i];

            while (hull.Count > lowerCount &&
                   Cross(
                       hull[hull.Count - 1] - hull[hull.Count - 2],
                       point - hull[hull.Count - 1]) <= EPSILON)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        if (hull.Count > 1)
            hull.RemoveAt(hull.Count - 1);

        if (hull.Count < 3)
            return new List<Vector2>();

        return hull;
    }

    private static void AddIntersections(Edge first, Edge second)
    {
        Vector2 r = first.B - first.A;
        Vector2 s = second.B - second.A;
        Vector2 delta = second.A - first.A;

        float denominator = Cross(r, s);

        if (Mathf.Abs(denominator) <= EPSILON)
        {
            if (Mathf.Abs(Cross(delta, r)) >
                EPSILON * r.magnitude)
                return;

            AddPointCut(first.A, second);
            AddPointCut(first.B, second);
            AddPointCut(second.A, first);
            AddPointCut(second.B, first);
            return;
        }

        float t = Cross(delta, s) / denominator;
        float u = Cross(delta, r) / denominator;

        if (t < -EPSILON || t > 1f + EPSILON ||
            u < -EPSILON || u > 1f + EPSILON)
            return;

        first.Cuts.Add(Mathf.Clamp01(t));
        second.Cuts.Add(Mathf.Clamp01(u));
    }

    private static void AddPointCut(Vector2 point, Edge edge)
    {
        Vector2 direction = edge.B - edge.A;
        float lengthSquared = direction.sqrMagnitude;

        if (lengthSquared <= EPSILON * EPSILON)
            return;

        float distance = Mathf.Abs(
            Cross(direction, point - edge.A)) /
            Mathf.Sqrt(lengthSquared);

        if (distance > EPSILON)
            return;

        float t = Vector2.Dot(
            point - edge.A, direction) / lengthSquared;

        if (t < -EPSILON || t > 1f + EPSILON)
            return;

        edge.Cuts.Add(Mathf.Clamp01(t));
    }

    private static bool IsInsideUnion(
        Vector2 point, List<Polygon> polygons)
    {
        foreach (Polygon polygon in polygons)
        {
            Rect bounds = polygon.Bounds;

            if (point.x < bounds.xMin - EPSILON ||
                point.x > bounds.xMax + EPSILON ||
                point.y < bounds.yMin - EPSILON ||
                point.y > bounds.yMax + EPSILON)
                continue;

            if (IsInsidePolygon(point, polygon.Points))
                return true;
        }

        return false;
    }

    private static bool IsInsidePolygon(
        Vector2 point, List<Vector2> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];

            if (Cross(b - a, point - a) < -EPSILON)
                return false;
        }

        return true;
    }

    private static Rect CreateBounds(
        IReadOnlyList<Vector2> points)
    {
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        foreach (Vector2 point in points)
        {
            minX = Mathf.Min(minX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxX = Mathf.Max(maxX, point.x);
            maxY = Mathf.Max(maxY, point.y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static bool BoundsOverlap(Rect a, Rect b)
    {
        return a.xMin <= b.xMax + EPSILON &&
               a.xMax >= b.xMin - EPSILON &&
               a.yMin <= b.yMax + EPSILON &&
               a.yMax >= b.yMin - EPSILON;
    }

    private static (long, long) Quantize(Vector2 point)
    {
        return (
            (long)Math.Round(point.x * KEY_PRECISION),
            (long)Math.Round(point.y * KEY_PRECISION));
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }
}
