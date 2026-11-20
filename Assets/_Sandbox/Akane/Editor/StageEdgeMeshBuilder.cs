
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 地形群の外周を検出し、角を丸めた縁メッシュを生成する。
/// 編集時専用。生成後は通常のMeshRendererで描画する。
/// </summary>
public static class StageEdgeMeshBuilder
{
    private const float EPSILON = 0.00001f;
    private const float PROBE_DISTANCE = 0.001f;
    private const double KEY_PRECISION = 10000.0;
    private const float MAX_CORNER_LENGTH_RATIO = 0.4f;

    public sealed class BuildResult
    {
        public MeshFilter Source { get; }
        public Mesh GeneratedMesh { get; }

        public BuildResult(MeshFilter source, Mesh mesh)
        {
            Source = source;
            GeneratedMesh = mesh;
        }
    }

    private sealed class Piece
    {
        public MeshFilter Source;
        public Vector3[] WorldVertices;
        public int[] Triangles;
        public List<Vector2> Hull;
        public Rect Bounds;
    }

    private sealed class Edge
    {
        public Vector2 A;
        public Vector2 B;
        public Rect Bounds;
        public List<float> Cuts = new() { 0f, 1f };

        public Edge(Vector2 a, Vector2 b)
        {
            A = a;
            B = b;
            Bounds = CreateBounds(a, b);
        }
    }

    /// <summary>
    /// 外周線。常に地形内部が進行方向の左側になる。
    /// </summary>
    private sealed class Contour
    {
        public Vector2 A;
        public Vector2 B;
        public Vector2 Direction;
        public Vector2 Inward;
        public Vector2 InnerStart;
        public Vector2 InnerEnd;
        public float Length;
        public Rect Bounds;

        public Contour(Vector2 a, Vector2 b)
        {
            A = a;
            B = b;
            Length = (b - a).magnitude;
            Direction = (b - a) / Length;
            Inward = new Vector2(-Direction.y, Direction.x);
            Bounds = CreateBounds(a, b);
        }

        public void InitializeInner(float width)
        {
            InnerStart = A + Inward * width;
            InnerEnd = B + Inward * width;
        }
    }

    /// <summary>
    /// 接続点の曲線情報。
    /// </summary>
    private sealed class Corner
    {
        public Vector2 Pivot;
        public List<Vector2> Arc;

        public Corner(Vector2 pivot, List<Vector2> arc)
        {
            Pivot = pivot;
            Arc = arc;
        }
    }

    /// <summary>
    /// 縁を構成する凸多角形。
    /// 直線部分は四角形、曲線部分は三角形を使用する。
    /// </summary>
    private sealed class Patch
    {
        public Vector2[] Points;
        public Rect Bounds;

        public Patch(params Vector2[] points)
        {
            Points = points;
            Bounds = CreateBounds(points);
        }
    }

    /// <summary>
    /// 地形ごとの縁メッシュを生成する。
    /// </summary>
    public static List<BuildResult> Build(
        StageSurfaceMask mask,
        IReadOnlyList<MeshFilter> filters)
    {
        if (mask == null || mask.ProfileLibrary == null)
        {
            throw new InvalidOperationException(
                "StagePieceProfileLibraryが設定されていません。");
        }

        List<Piece> pieces = CollectPieces(
            mask.ProfileLibrary, filters);

        List<Contour> contours = BuildContours(pieces);

        List<Patch> patches = BuildPatches(
            contours,
            mask.EdgeWidth,
            mask.CornerRadius,
            mask.CornerSegments);

        Vector3 toCamera = mask.CameraAtNegativeZ
            ? Vector3.back
            : Vector3.forward;

        List<BuildResult> results = new();

        foreach (Piece piece in pieces)
        {
            Mesh mesh = CreateEdgeMesh(
                piece, patches,
                mask.DepthOffset, toCamera);

            if (mesh == null)
                continue;

            results.Add(new BuildResult(piece.Source, mesh));
        }

        Debug.Log(
            $"StageEdge生成完了: 地形={pieces.Count}, " +
            $"外周線={contours.Count}, " +
            $"縁領域={patches.Count}, " +
            $"生成メッシュ={results.Count}");

        return results;
    }

    /// <summary>
    /// 保存済み形状をワールド座標へ変換する。
    /// Read/WriteがOFFのMeshでも使用できる。
    /// </summary>
    private static List<Piece> CollectPieces(
        StagePieceProfileLibrary library,
        IReadOnlyList<MeshFilter> filters)
    {
        List<Piece> pieces = new();

        foreach (MeshFilter filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;

            if (!library.TryGetProfile(
                filter.sharedMesh, out StagePieceProfile profile))
            {
                throw new InvalidOperationException(
                    $"{filter.name}: 形状情報がありません。");
            }

            Vector3[] localVertices = profile.Vertices;
            Vector3[] worldVertices =
                new Vector3[localVertices.Length];

            List<Vector2> projected =
                new(localVertices.Length);

            for (int i = 0; i < localVertices.Length; i++)
            {
                Vector3 world = filter.transform.TransformPoint(
                    localVertices[i]);

                worldVertices[i] = world;
                projected.Add(new Vector2(world.x, world.y));
            }

            // 現在の地形パーツは凸形状を前提とする。
            List<Vector2> hull = BuildConvexHull(projected);

            if (hull.Count < 3)
                continue;

            pieces.Add(new Piece
            {
                Source = filter,
                WorldVertices = worldVertices,
                Triangles = profile.Triangles,
                Hull = hull,
                Bounds = CreateBounds(hull)
            });
        }

        return pieces;
    }

    /// <summary>
    /// 複数の地形の内部境界を除外し、外周線を取得する。
    /// </summary>
    private static List<Contour> BuildContours(
        List<Piece> pieces)
    {
        List<Edge> edges = new();

        foreach (Piece piece in pieces)
        {
            for (int i = 0; i < piece.Hull.Count; i++)
            {
                Vector2 a = piece.Hull[i];
                Vector2 b = piece.Hull[
                    (i + 1) % piece.Hull.Count];

                if ((b - a).sqrMagnitude <= EPSILON * EPSILON)
                    continue;

                edges.Add(new Edge(a, b));
            }
        }

        // 交差部分を分割する。
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

        List<Contour> result = new();
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
                Vector2 segment = b - a;

                float length = segment.magnitude;

                if (length <= EPSILON)
                    continue;

                Vector2 leftNormal = new(
                    -segment.y / length,
                    segment.x / length);

                Vector2 midpoint = (a + b) * 0.5f;
                float probe = Mathf.Min(
                    PROBE_DISTANCE, length * 0.25f);

                bool leftInside = IsInsideUnion(
                    midpoint + leftNormal * probe, pieces);

                bool rightInside = IsInsideUnion(
                    midpoint - leftNormal * probe, pieces);

                if (leftInside == rightInside)
                    continue;

                var aKey = Quantize(a);
                var bKey = Quantize(b);

                // 同じ線分を二重登録しない。
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

                // 地形内部が左側になる方向へ統一する。
                result.Add(leftInside
                    ? new Contour(a, b)
                    : new Contour(b, a));
            }
        }

        return result;
    }

    /// <summary>
    /// 直線部分と曲線部分を、描画用の凸多角形に分解する。
    /// </summary>
    private static List<Patch> BuildPatches(
        List<Contour> contours,
        float width,
        float radius,
        int segments)
    {
        List<Patch> patches = new();

        if (width <= EPSILON)
            return patches;

        segments = Mathf.Clamp(segments, 2, 16);

        // 線分の内側を初期化する。
        foreach (Contour contour in contours)
        {
            contour.InitializeInner(width);
        }

        // 接続点を探すため、始点と終点を別々に登録する。
        Dictionary<(long, long), List<int>> starts = new();
        Dictionary<(long, long), List<int>> ends = new();

        for (int i = 0; i < contours.Count; i++)
        {
            AddIndex(starts, Quantize(contours[i].A), i);
            AddIndex(ends, Quantize(contours[i].B), i);
        }

        List<Corner> corners = new();

        // 終点と始点が一致する線分を接続する。
        foreach (var entry in ends)
        {
            if (entry.Value.Count != 1)
                continue;

            if (!starts.TryGetValue(
                entry.Key, out List<int> nextIndices))
                continue;

            // 複数の接続候補がある点は安全のため丸めない。
            if (nextIndices.Count != 1)
                continue;

            Contour previous = contours[entry.Value[0]];
            Contour next = contours[nextIndices[0]];

            if (ReferenceEquals(previous, next))
                continue;

            Corner corner = CreateCorner(
                previous, next,
                width, radius, segments);

            if (corner != null)
                corners.Add(corner);
        }

        // 各線分の直線部分を生成する。
        foreach (Contour contour in contours)
        {
            AddPatch(patches,
                contour.A,
                contour.B,
                contour.InnerEnd,
                contour.InnerStart);
        }

        // 曲線を三角形に分解して追加する。
        foreach (Corner corner in corners)
        {
            for (int i = 0; i < corner.Arc.Count - 1; i++)
            {
                AddPatch(patches,
                    corner.Pivot,
                    corner.Arc[i],
                    corner.Arc[i + 1]);
            }
        }

        return patches;
    }

    /// <summary>
    /// 接続点の角度から内側の円弧を作成する。
    /// 凸角と凹角で異なる処理を行う。
    /// </summary>
    private static Corner CreateCorner(
        Contour previous,
        Contour next,
        float width,
        float radius,
        int segments)
    {
        Vector2 pivot = previous.B;

        if ((pivot - next.A).sqrMagnitude >
            EPSILON * EPSILON * 100f)
        {
            return null;
        }

        float cross = Cross(
            previous.Direction, next.Direction);

        float dot = Vector2.Dot(
            previous.Direction, next.Direction);

        float turn = Mathf.Atan2(cross, dot);

        if (Mathf.Abs(turn) < 0.0001f)
            return null;

        // 180度の折り返しは通常の角として扱わない。
        if (Mathf.Abs(turn) > Mathf.PI - 0.001f)
            return null;

        // 凹角の場合は接点を中心とした円弧で接続する。
        if (turn < 0f)
        {
            previous.InnerEnd =
                pivot + previous.Inward * width;

            next.InnerStart =
                pivot + next.Inward * width;

            List<Vector2> arc = CreateArc(
                pivot,
                previous.InnerEnd,
                turn,
                segments);

            arc[^1] = next.InnerStart;

            return new Corner(pivot, arc);
        }

        // 凸角の内側の交点を丸める。
        float tanHalf = Mathf.Tan(turn * 0.5f);

        if (tanHalf <= EPSILON)
            return null;

        // 短い線分では丸め半径を縮小する。
        // 両端の角を処理しても線分が反転しないように制限する。
        float availableLength = Mathf.Min(
            previous.Length, next.Length) *
            MAX_CORNER_LENGTH_RATIO;

        float maxRadius =
            availableLength / tanHalf - width;

        float actualRadius = Mathf.Min(
            Mathf.Max(0f, radius),
            Mathf.Max(0f, maxRadius));

        if (actualRadius <= EPSILON)
        {
            // 半径を確保できない場合は直線接続にする。
            // 短い段差で無理に曲線を作ると面が交差するため。
            return new Corner(pivot, new List<Vector2>
            {
                previous.InnerEnd,
                next.InnerStart
            });
        }

        // 元の外周接点から、各線分上の接線位置まで戻す。
        float tangentDistance =
            (width + actualRadius) * tanHalf;

        previous.InnerEnd =
            pivot +
            previous.Inward * width -
            previous.Direction * tangentDistance;

        next.InnerStart =
            pivot +
            next.Inward * width +
            next.Direction * tangentDistance;

        // 円弧の中心。前の線分の内側法線方向へ半径分進む。
        Vector2 center =
            previous.InnerEnd +
            previous.Inward * actualRadius;

        List<Vector2> roundedArc = CreateArc(
            center,
            previous.InnerEnd,
            turn,
            segments);

        // 計算誤差による隙間を避ける。
        roundedArc[0] = previous.InnerEnd;
        roundedArc[^1] = next.InnerStart;

        return new Corner(pivot, roundedArc);
    }

    /// <summary>
    /// 指定した中心・回転角度で円弧を作成する。
    /// </summary>
    private static List<Vector2> CreateArc(
        Vector2 center,
        Vector2 start,
        float radians,
        int segments)
    {
        List<Vector2> points = new(segments + 1);

        Vector2 offset = start - center;

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = radians * t;

            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            Vector2 rotated = new(
                offset.x * cos - offset.y * sin,
                offset.x * sin + offset.y * cos);

            points.Add(center + rotated);
        }

        return points;
    }

    private static void AddIndex(
        Dictionary<(long, long), List<int>> dictionary,
        (long, long) key,
        int index)
    {
        if (!dictionary.TryGetValue(
            key, out List<int> indices))
        {
            indices = new List<int>();
            dictionary.Add(key, indices);
        }

        indices.Add(index);
    }

    /// <summary>
    /// 面積がない多角形は描画対象へ追加しない。
    /// </summary>
    private static void AddPatch(
        List<Patch> patches,
        params Vector2[] points)
    {
        if (points.Length < 3)
            return;

        float area = 0f;

        for (int i = 0; i < points.Length; i++)
        {
            area += Cross(
                points[i],
                points[(i + 1) % points.Length]);
        }

        if (Mathf.Abs(area) <= EPSILON)
            return;

        patches.Add(new Patch(points));
    }

    /// <summary>
    /// 元の3D表面と縁領域の重なった部分からメッシュを生成する。
    /// </summary>
    private static Mesh CreateEdgeMesh(
        Piece piece,
        List<Patch> patches,
        float depthOffset,
        Vector3 toCamera)
    {
        List<Vector3> vertices = new();
        List<int> triangles = new();

        for (int i = 0; i < piece.Triangles.Length; i += 3)
        {
            int i0 = piece.Triangles[i];
            int i1 = piece.Triangles[i + 1];
            int i2 = piece.Triangles[i + 2];

            if (i0 < 0 || i1 < 0 || i2 < 0 ||
                i0 >= piece.WorldVertices.Length ||
                i1 >= piece.WorldVertices.Length ||
                i2 >= piece.WorldVertices.Length)
                continue;

            Vector3 a = piece.WorldVertices[i0];
            Vector3 b = piece.WorldVertices[i1];
            Vector3 c = piece.WorldVertices[i2];

            Vector3 faceNormal =
                Vector3.Cross(b - a, c - a);

            // カメラから見えない裏面は生成しない。
            if (Vector3.Dot(faceNormal, toCamera) <= EPSILON)
                continue;

            Vector2 a2 = new(a.x, a.y);
            Vector2 b2 = new(b.x, b.y);
            Vector2 c2 = new(c.x, c.y);

            if (Mathf.Abs(Cross(b2 - a2, c2 - a2)) <= EPSILON)
                continue;

            Rect triangleBounds = CreateBounds(
                new Vector2[] { a2, b2, c2 });

            foreach (Patch patch in patches)
            {
                if (!BoundsOverlap(
                    triangleBounds, patch.Bounds))
                    continue;

                List<Vector3> clipped = ClipTriangle(
                    a, b, c, patch.Points);

                if (clipped.Count < 3)
                    continue;

                int firstIndex = vertices.Count;

                foreach (Vector3 point in clipped)
                {
                    // 元の3D表面上のZ座標を維持する。
                    Vector3 shifted =
                        point + toCamera * depthOffset;

                    vertices.Add(
                        piece.Source.transform
                            .InverseTransformPoint(shifted));
                }

                for (int j = 1; j < clipped.Count - 1; j++)
                {
                    Vector3 v0 = clipped[0];
                    Vector3 v1 = clipped[j];
                    Vector3 v2 = clipped[j + 1];

                    if (Vector3.Cross(
                        v1 - v0, v2 - v0).sqrMagnitude <
                        EPSILON * EPSILON)
                        continue;

                    triangles.Add(firstIndex);
                    triangles.Add(firstIndex + j);
                    triangles.Add(firstIndex + j + 1);
                }
            }
        }

        if (triangles.Count == 0)
            return null;

        Mesh mesh = new()
        {
            name = "GeneratedStageEdge_" + piece.Source.name,
            indexFormat = IndexFormat.UInt32
        };

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();

        Bounds bounds = piece.Source.sharedMesh.bounds;
        bounds.Expand(Mathf.Max(0.01f, depthOffset * 4f));
        mesh.bounds = bounds;

        return mesh;
    }

    /// <summary>
    /// 元の3D三角形をX/Y平面上の凸多角形で切り抜く。
    /// 交差点のZ座標は三角形の補間によって取得する。
    /// </summary>
    private static List<Vector3> ClipTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector2[] clipPolygon)
    {
        List<Vector3> polygon = new() { a, b, c };

        float orientation = Cross(
            clipPolygon[1] - clipPolygon[0],
            clipPolygon[2] - clipPolygon[1]) >= 0f
            ? 1f
            : -1f;

        for (int i = 0; i < clipPolygon.Length; i++)
        {
            Vector2 edgeA = clipPolygon[i];
            Vector2 edgeB =
                clipPolygon[(i + 1) % clipPolygon.Length];

            if (polygon.Count == 0)
                break;

            List<Vector3> output = new();

            Vector3 previous = polygon[^1];

            float previousSide = GetSide(
                previous, edgeA, edgeB, orientation);

            foreach (Vector3 current in polygon)
            {
                float currentSide = GetSide(
                    current, edgeA, edgeB, orientation);

                bool previousInside =
                    previousSide >= -EPSILON;

                bool currentInside =
                    currentSide >= -EPSILON;

                if (previousInside != currentInside)
                {
                    float denominator =
                        previousSide - currentSide;

                    if (Mathf.Abs(denominator) > EPSILON)
                    {
                        float t = Mathf.Clamp01(
                            previousSide / denominator);

                        output.Add(Vector3.Lerp(
                            previous, current, t));
                    }
                }

                if (currentInside)
                    output.Add(current);

                previous = current;
                previousSide = currentSide;
            }

            polygon = output;
        }

        // 連続する重複点を除外する。
        List<Vector3> cleaned = new();

        foreach (Vector3 point in polygon)
        {
            if (cleaned.Count > 0 &&
                (cleaned[^1] - point).sqrMagnitude <=
                EPSILON * EPSILON)
                continue;

            cleaned.Add(point);
        }

        if (cleaned.Count > 1 &&
            (cleaned[0] - cleaned[^1]).sqrMagnitude <=
            EPSILON * EPSILON)
        {
            cleaned.RemoveAt(cleaned.Count - 1);
        }

        return cleaned;
    }

    private static float GetSide(
        Vector3 point,
        Vector2 a,
        Vector2 b,
        float orientation)
    {
        return Cross(
            b - a,
            new Vector2(point.x, point.y) - a) *
            orientation;
    }

    private static bool IsInsideUnion(
        Vector2 point,
        List<Piece> pieces)
    {
        foreach (Piece piece in pieces)
        {
            if (!ContainsBounds(piece.Bounds, point))
                continue;

            bool inside = true;

            for (int i = 0; i < piece.Hull.Count; i++)
            {
                Vector2 a = piece.Hull[i];
                Vector2 b = piece.Hull[
                    (i + 1) % piece.Hull.Count];

                if (Cross(b - a, point - a) < -EPSILON)
                {
                    inside = false;
                    break;
                }
            }

            if (inside)
                return true;
        }

        return false;
    }

    private static List<Vector2> BuildConvexHull(
        List<Vector2> points)
    {
        points.Sort((a, b) =>
        {
            int comparison = a.x.CompareTo(b.x);
            return comparison != 0
                ? comparison
                : a.y.CompareTo(b.y);
        });

        List<Vector2> hull = new();

        foreach (Vector2 point in points)
        {
            while (hull.Count >= 2 &&
                   Cross(
                       hull[^1] - hull[^2],
                       point - hull[^1]) <= EPSILON)
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
                       hull[^1] - hull[^2],
                       point - hull[^1]) <= EPSILON)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        if (hull.Count > 1)
            hull.RemoveAt(hull.Count - 1);

        return hull;
    }

    private static void AddIntersections(
        Edge first, Edge second)
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

    private static void AddPointCut(
        Vector2 point, Edge edge)
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

    private static Rect CreateBounds(
        Vector2 a, Vector2 b)
    {
        return Rect.MinMaxRect(
            Mathf.Min(a.x, b.x),
            Mathf.Min(a.y, b.y),
            Mathf.Max(a.x, b.x),
            Mathf.Max(a.y, b.y));
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

        return Rect.MinMaxRect(
            minX, minY, maxX, maxY);
    }

    private static bool ContainsBounds(
        Rect bounds, Vector2 point)
    {
        return point.x >= bounds.xMin - EPSILON &&
               point.x <= bounds.xMax + EPSILON &&
               point.y >= bounds.yMin - EPSILON &&
               point.y <= bounds.yMax + EPSILON;
    }

    private static bool BoundsOverlap(
        Rect a, Rect b)
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

    private static float Cross(
        Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }
}
