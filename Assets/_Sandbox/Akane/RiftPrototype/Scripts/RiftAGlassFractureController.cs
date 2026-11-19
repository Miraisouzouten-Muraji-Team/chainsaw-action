using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RiftPrototypeA
{
    /// <summary>
    /// ステージ選択画面をガラス状の三角形メッシュに分割し、
    /// 破片を3D空間で移動・回転させる。
    /// </summary>
    public sealed class RiftAGlassFractureController : MonoBehaviour
    {
        private const float TWO_PI = Mathf.PI * 2f;
        private const int VERTICES_PER_PIECE = 27;
        private static readonly int MAIN_TEXTURE_ID =
            Shader.PropertyToID("_MainTex");

        [Header("描画参照")]
        [SerializeField] private Camera fractureCamera;
        [SerializeField] private RawImage fractureImage;
        [SerializeField] private Shader fractureShader;
        [SerializeField] private string fractureLayerName = "RiftAFragments";

        [Header("破片の形状")]
        [SerializeField, Range(12, 48)] private int radialSegments = 24;
        [SerializeField, Min(1f)] private float planeDistance = 8f;
        [SerializeField, Min(0.005f)] private float thickness = 0.12f;
        [SerializeField, Range(0.001f, 0.1f)] private float crackWidth = 0.018f;

        [Header("破片の移動")]
        [SerializeField, Min(0f)] private float spreadDistance = 14f;
        [SerializeField, Min(0f)] private float forwardDistance = 10f;
        [SerializeField, Range(0f, 180f)] private float maxRotation = 105f;

        private sealed class Piece
        {
            public int startIndex;
            public int ringIndex;
            public float delay;
            public float crackDistance;
            public Vector3 center;
            public Vector3 direction;
            public Vector3 rotation;
            public Vector3[] baseVertices;
        }

        private readonly List<Piece> pieces = new();
        private readonly List<Vector3> positions = new();
        private readonly List<Vector4> textureCoordinates = new();
        private readonly List<Color> colors = new();
        private readonly List<int> triangles = new();

        private GameObject meshObject;
        private Mesh mesh;
        private Material fractureMaterial;
        private RenderTexture fractureTexture;
        private bool initialized;

        public bool IsReady => initialized;

        private void Awake()
        {
            Initialize();
        }

        /// <summary>
        /// 亀裂専用カメラと描画先を準備する。
        /// </summary>
        public void Initialize()
        {
            if (initialized) return;

            if (fractureCamera == null ||
                fractureImage == null ||
                fractureShader == null)
            {
                Debug.LogError(
                    "RiftAの破片描画に必要な参照が不足しています。",
                    this);
                return;
            }

            int fragmentLayer =
                LayerMask.NameToLayer(fractureLayerName);

            if (fragmentLayer < 0)
            {
                Debug.LogError(
                    $"Layer '{fractureLayerName}' を作成してください。",
                    this);
                return;
            }

            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);

            fractureTexture = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32)
            {
                name = "RiftA_FractureRT",
                antiAliasing = 1
            };

            fractureTexture.Create();

            // 破片だけを描画し、背景は透明にする。
            fractureCamera.orthographic = false;
            fractureCamera.fieldOfView = 60f;
            fractureCamera.nearClipPlane = 0.03f;
            fractureCamera.farClipPlane = 100f;
            fractureCamera.clearFlags = CameraClearFlags.SolidColor;
            fractureCamera.backgroundColor = Color.clear;
            fractureCamera.cullingMask = 1 << fragmentLayer;
            fractureCamera.allowHDR = false;
            fractureCamera.allowMSAA = false;
            fractureCamera.targetTexture = fractureTexture;
            fractureCamera.enabled = false;

            // 破片を1つのメッシュにまとめて描画する。
            meshObject = new GameObject("RiftA_FractureMesh");
            meshObject.layer = fragmentLayer;
            meshObject.transform.SetParent(
                fractureCamera.transform, false);

            MeshFilter meshFilter =
                meshObject.AddComponent<MeshFilter>();

            MeshRenderer meshRenderer =
                meshObject.AddComponent<MeshRenderer>();

            mesh = new Mesh
            {
                name = "RiftA_FractureMesh_Runtime"
            };

            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;

            fractureMaterial = new Material(fractureShader)
            {
                name = "RiftA_GlassFracture_Runtime"
            };

            meshRenderer.sharedMaterial = fractureMaterial;

            fractureImage.texture = fractureTexture;
            fractureImage.material = null;
            fractureImage.raycastTarget = false;
            fractureImage.gameObject.SetActive(false);
            meshObject.SetActive(false);

            initialized = true;
        }

        /// <summary>
        /// 保存したメニュー画像を破片の表面に貼り、分割形状を生成する。
        /// </summary>
        public void Prepare(RenderTexture menuTexture, Vector2 centerUV)
        {
            Initialize();

            if (!initialized || menuTexture == null)
            {
                throw new InvalidOperationException(
                    "RiftAの破片描画を準備できません。");
            }

            fractureMaterial.SetTexture(
                MAIN_TEXTURE_ID, menuTexture);

            BuildMesh(centerUV);
            SetProgress(0f, 0f);

            meshObject.SetActive(true);
            fractureImage.gameObject.SetActive(true);
            fractureCamera.enabled = true;
        }

        /// <summary>
        /// 中心から外側へ広がる不規則な三角形を生成する。
        /// </summary>
        private void BuildMesh(Vector2 centerUV)
        {
            pieces.Clear();
            positions.Clear();
            textureCoordinates.Clear();
            colors.Clear();
            triangles.Clear();

            float aspect =
                (float)Screen.width / Mathf.Max(1, Screen.height);

            List<float> angles = new();

            for (int i = 0; i < radialSegments; i++)
            {
                angles.Add(TWO_PI * i / radialSegments);
            }

            // 四隅の方向も登録し、画面全体を隙間なく覆う。
            Vector2[] corners =
            {
                Vector2.zero,
                Vector2.right,
                Vector2.one,
                Vector2.up
            };

            foreach (Vector2 corner in corners)
            {
                Vector2 delta = corner - centerUV;
                float angle = Mathf.Repeat(
                    Mathf.Atan2(delta.y, delta.x * aspect),
                    TWO_PI);

                angles.Add(angle);
            }

            angles.Sort();

            for (int i = angles.Count - 1; i > 0; i--)
            {
                if (Mathf.Abs(angles[i] - angles[i - 1]) < 0.0001f)
                {
                    angles.RemoveAt(i);
                }
            }

            int count = angles.Count;

            float[] ringScales =
            {
                0.12f,
                0.38f,
                0.72f,
                1f
            };

            Vector2[][] rings = new Vector2[ringScales.Length][];

            for (int ring = 0; ring < rings.Length; ring++)
            {
                rings[ring] = new Vector2[count];

                for (int i = 0; i < count; i++)
                {
                    Vector2 boundary =
                        GetBoundary(centerUV, angles[i], aspect);

                    float jitter = ring == rings.Length - 1
                        ? 1f
                        : Mathf.Lerp(
                            0.88f, 1.12f,
                            Hash(i * 41 + ring * 79));

                    rings[ring][i] = Vector2.LerpUnclamped(
                        centerUV,
                        boundary,
                        ringScales[ring] * jitter);
                }
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;

                // 中央の破片は最初に移動する。
                AddPiece(
                    centerUV,
                    rings[0][i],
                    rings[0][next],
                    0,
                    i,
                    centerUV);

                // 外周へ向かうほど遅れて破片が動き出す。
                for (int ring = 0; ring < 3; ring++)
                {
                    Vector2 a = rings[ring][i];
                    Vector2 b = rings[ring + 1][i];
                    Vector2 c = rings[ring + 1][next];
                    Vector2 d = rings[ring][next];

                    AddPiece(a, b, c, ring + 1, i * 2, centerUV);
                    AddPiece(a, c, d, ring + 1, i * 2 + 1, centerUV);
                }
            }

            mesh.Clear();
            mesh.SetVertices(positions);
            mesh.SetUVs(0, textureCoordinates);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);

            // アニメーションで大きく移動するので広めに設定する。
            mesh.bounds = new Bounds(
                Vector3.zero,
                Vector3.one * 1000f);
        }

        private void AddPiece(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            int ringIndex,
            int seed,
            Vector2 centerUV)
        {
            Vector2[] points = { a, b, c };
            Vector2 centroid = (a + b + c) / 3f;

            Vector3[] front = new Vector3[3];

            for (int i = 0; i < 3; i++)
            {
                front[i] = ToCameraPlane(points[i]);
            }

            Vector3 pieceCenter =
                (front[0] + front[1] + front[2]) / 3f;

            Vector2 radial = centroid - centerUV;

            if (radial.sqrMagnitude < 0.000001f)
            {
                float angle = Hash(seed * 17) * TWO_PI;
                radial = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle));
            }

            radial.Normalize();

            float random = Hash(seed * 23 + ringIndex * 31);

            Piece piece = new Piece
            {
                startIndex = positions.Count,
                ringIndex = ringIndex,
                delay = ringIndex switch
                {
                    0 => 0f,
                    1 => 0.14f,
                    2 => 0.36f,
                    _ => 0.58f
                },
                crackDistance = ringIndex * 0.19f,
                center = pieceCenter,
                direction = new Vector3(radial.x, radial.y, 0f),
                rotation = new Vector3(
                    Mathf.Lerp(-maxRotation, maxRotation, random),
                    Mathf.Lerp(
                        -maxRotation,
                        maxRotation,
                        Hash(seed * 29 + 7)),
                    Mathf.Lerp(
                        -maxRotation,
                        maxRotation,
                        Hash(seed * 37 + 11))),
                baseVertices = new Vector3[VERTICES_PER_PIECE]
            };

            pieces.Add(piece);

            // 破片の表面。UVは元の画面上の位置を使用する。
            int baseIndex = positions.Count;

            for (int i = 0; i < 3; i++)
            {
                AddVertex(piece, front[i],
                    new Vector4(points[i].x, points[i].y, 0f, 0f),
                    Color.white);
            }

            AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);

            // 3辺に厚みを付ける。
            for (int edge = 0; edge < 3; edge++)
            {
                Vector3 p0 = front[edge];
                Vector3 p1 = front[(edge + 1) % 3];
                Vector3 depth = Vector3.forward * thickness;

                int index = positions.Count;

                AddVertex(piece, p0, new Vector4(0f, 0f, 1f, 0f), Color.white);
                AddVertex(piece, p1, new Vector4(0f, 0f, 1f, 0f), Color.white);
                AddVertex(piece, p1 + depth, new Vector4(0f, 0f, 1f, 0f), Color.white);
                AddVertex(piece, p0 + depth, new Vector4(0f, 0f, 1f, 0f), Color.white);

                AddTriangle(index, index + 1, index + 2);
                AddTriangle(index, index + 2, index + 3);
            }

            // 破片の境界に亀裂線を描く。
            for (int edge = 0; edge < 3; edge++)
            {
                Vector3 p0 = front[edge];
                Vector3 p1 = front[(edge + 1) % 3];

                Vector3 direction = (p1 - p0).normalized;
                Vector3 side =
                    Vector3.Cross(Vector3.forward, direction) *
                    crackWidth * 0.5f;

                Vector3 towardCamera = Vector3.back * 0.015f;
                Color crackColor = new Color(1f, 1f, 1f, 0f);

                int index = positions.Count;

                AddVertex(piece, p0 - side + towardCamera,
                    new Vector4(0f, 0f, 2f, 0f), crackColor);
                AddVertex(piece, p0 + side + towardCamera,
                    new Vector4(0f, 0f, 2f, 0f), crackColor);
                AddVertex(piece, p1 - side + towardCamera,
                    new Vector4(0f, 0f, 2f, 0f), crackColor);
                AddVertex(piece, p1 + side + towardCamera,
                    new Vector4(0f, 0f, 2f, 0f), crackColor);

                AddTriangle(index, index + 1, index + 2);
                AddTriangle(index + 1, index + 3, index + 2);
            }
        }

        private void AddVertex(
            Piece piece,
            Vector3 position,
            Vector4 uv,
            Color color)
        {
            int localIndex = positions.Count - piece.startIndex;

            piece.baseVertices[localIndex] = position;
            positions.Add(position);
            textureCoordinates.Add(uv);
            colors.Add(color);
        }

        private void AddTriangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        /// <summary>
        /// ひびの出現と、破片の飛散を更新する。
        /// </summary>
        public void SetProgress(float crackProgress, float breakProgress)
        {
            if (!initialized || mesh == null) return;

            crackProgress = Mathf.Clamp01(crackProgress);
            breakProgress = Mathf.Clamp01(breakProgress);

            foreach (Piece piece in pieces)
            {
                float t = Mathf.Clamp01(
                    (breakProgress - piece.delay) /
                    Mathf.Max(0.001f, 1f - piece.delay));

                // 最初は速く、後半は緩やかに移動する。
                float movement = 1f - Mathf.Pow(1f - t, 3f);

                float radialDistance =
                    spreadDistance * movement *
                    (0.55f + piece.ringIndex * 0.25f);

                float depthDistance =
                    forwardDistance * movement * movement;

                Vector3 offset =
                    piece.direction * radialDistance +
                    Vector3.back * depthDistance;

                Quaternion rotation = Quaternion.Euler(
                    piece.rotation * movement);

                for (int i = 0; i < VERTICES_PER_PIECE; i++)
                {
                    int index = piece.startIndex + i;

                    Vector3 local =
                        piece.baseVertices[i] - piece.center;

                    positions[index] =
                        piece.center +
                        offset +
                        rotation * local;
                }

                // 中心から亀裂線を出現させる。
                float crackVisibility = Mathf.Clamp01(
                    (crackProgress - piece.crackDistance) * 8f);

                // 破片が飛んでいくと亀裂の発光を弱める。
                crackVisibility *= 1f - movement * 0.8f;

                // 最後の12頂点が亀裂線。
                for (int i = 15; i < VERTICES_PER_PIECE; i++)
                {
                    int index = piece.startIndex + i;
                    Color color = colors[index];
                    color.a = crackVisibility;
                    colors[index] = color;
                }
            }

            mesh.SetVertices(positions);
            mesh.SetColors(colors);
        }

        /// <summary>
        /// 破片の描画を終了する。
        /// </summary>
        public void StopFracture()
        {
            if (fractureCamera != null)
                fractureCamera.enabled = false;

            if (fractureImage != null)
                fractureImage.gameObject.SetActive(false);

            if (meshObject != null)
                meshObject.SetActive(false);
        }

        private Vector3 ToCameraPlane(Vector2 uv)
        {
            float height =
                2f * planeDistance *
                Mathf.Tan(fractureCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            float width = height * fractureCamera.aspect;

            return new Vector3(
                (uv.x - 0.5f) * width,
                (uv.y - 0.5f) * height,
                planeDistance);
        }

        private static Vector2 GetBoundary(
            Vector2 center,
            float angle,
            float aspect)
        {
            Vector2 direction = new Vector2(
                Mathf.Cos(angle) / aspect,
                Mathf.Sin(angle));

            float tx = direction.x > 0f
                ? (1f - center.x) / direction.x
                : direction.x < 0f
                    ? -center.x / direction.x
                    : float.PositiveInfinity;

            float ty = direction.y > 0f
                ? (1f - center.y) / direction.y
                : direction.y < 0f
                    ? -center.y / direction.y
                    : float.PositiveInfinity;

            float distance = Mathf.Min(tx, ty);

            return center + direction * distance;
        }

        private static float Hash(int seed)
        {
            return Mathf.Repeat(
                Mathf.Sin(seed * 12.9898f) * 43758.5453f,
                1f);
        }

        private void OnDestroy()
        {
            StopFracture();

            if (fractureCamera != null)
                fractureCamera.targetTexture = null;

            if (mesh != null) Destroy(mesh);
            if (fractureMaterial != null) Destroy(fractureMaterial);

            if (fractureTexture != null)
            {
                fractureTexture.Release();
                Destroy(fractureTexture);
            }

            if (meshObject != null)
                Destroy(meshObject);
        }
    }
}
