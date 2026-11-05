using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ChargeEnemyのShield MeshをEditor上で固定Planeにより2分割し、
/// 完成済みMesh Assetとして保存する。
/// </summary>
/// <remarks>
/// Runtimeの死亡処理、表示切替、Collider、Rigidbody、Shield追従は担当しない。
/// ゲーム実行中に分割計算を持ち込まないため、この型はEditorフォルダー内だけに配置する。
/// </remarks>
public sealed class ChargeEnemyShieldSplitMeshGenerator : EditorWindow
{
    private const float DEFAULT_CUT_SURFACE_UV_SCALE = 1.0f;
    private const float POINT_DISTANCE_EPSILON_RATIO = 0.000001f;
    private const float MINIMUM_POINT_DISTANCE_EPSILON = 0.000001f;
    private const float COLLINEAR_SIN_SQUARED_EPSILON = 0.00000001f;

    private const string DEFAULT_OUTPUT_FOLDER = "Assets";
    private const string DEFAULT_FIRST_PIECE_ASSET_NAME = "Shield_Piece_A";
    private const string DEFAULT_SECOND_PIECE_ASSET_NAME = "Shield_Piece_B";

    private MeshFilter sourceMeshFilter;
    private Transform cutPointA;
    private Transform cutPointB;
    private Transform cutDepthPoint;

    private float cutSurfaceUvScale = DEFAULT_CUT_SURFACE_UV_SCALE;
    private string outputFolder = DEFAULT_OUTPUT_FOLDER;
    private string firstPieceAssetName = DEFAULT_FIRST_PIECE_ASSET_NAME;
    private string secondPieceAssetName = DEFAULT_SECOND_PIECE_ASSET_NAME;

    private string statusMessage;
    private MessageType statusMessageType = MessageType.None;

    [MenuItem("Tools/Enemy/Charge Enemy/Shield Split Mesh Generator")]
    private static void OpenWindow()
    {
        ChargeEnemyShieldSplitMeshGenerator window =
            GetWindow<ChargeEnemyShieldSplitMeshGenerator>();

        window.titleContent =
            new GUIContent("Shield Split Mesh Generator");

        window.minSize =
            new Vector2(440.0f, 430.0f);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(
            "ChargeEnemy Shield 事前分割Mesh生成",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "ShieldをEditor上で1回だけ2分割し、Runtimeでは完成済みMesh Assetを使用するためのツールです。",
            MessageType.Info);

        EditorGUILayout.Space();
        DrawSourceFields();

        EditorGUILayout.Space();
        DrawCutFields();

        EditorGUILayout.Space();
        DrawOutputFields();

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate", GUILayout.Height(32.0f)))
        {
            GenerateSplitMeshAssets();
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                statusMessage,
                statusMessageType);
        }
    }

    private void DrawSourceFields()
    {
        EditorGUILayout.LabelField(
            "Source",
            EditorStyles.boldLabel);

        sourceMeshFilter =
            (MeshFilter)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Source MeshFilter",
                    "分割対象ShieldのMeshFilter。元MeshはsharedMeshから読み取り、変更しません。"),
                sourceMeshFilter,
                typeof(MeshFilter),
                true);
    }

    private void DrawCutFields()
    {
        EditorGUILayout.LabelField(
            "Cut Plane",
            EditorStyles.boldLabel);

        cutPointA =
            (Transform)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Cut Point A",
                    "正面から見た斜め切断線の片端。想定では左下側。"),
                cutPointA,
                typeof(Transform),
                true);

        cutPointB =
            (Transform)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Cut Point B",
                    "正面から見た斜め切断線のもう片端。想定では右上側。"),
                cutPointB,
                typeof(Transform),
                true);

        cutDepthPoint =
            (Transform)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Cut Depth Point",
                    "Cut Point A / Bの線をShieldの厚み方向へ広げてPlaneにするための3点目。"),
                cutDepthPoint,
                typeof(Transform),
                true);

        cutSurfaceUvScale =
            EditorGUILayout.FloatField(
                new GUIContent(
                    "Cut Surface UV Scale",
                    "MeshPlaneSplitterが生成する切断面CapのUVスケール。0より大きい値を指定します。"),
                cutSurfaceUvScale);
    }

    private void DrawOutputFields()
    {
        EditorGUILayout.LabelField(
            "Output",
            EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        outputFolder =
            EditorGUILayout.TextField(
                new GUIContent(
                    "Output Folder",
                    "Assets配下の既存フォルダーを指定します。"),
                outputFolder);

        if (GUILayout.Button("Select...", GUILayout.Width(80.0f)))
        {
            SelectOutputFolder();
        }

        EditorGUILayout.EndHorizontal();

        firstPieceAssetName =
            EditorGUILayout.TextField(
                "First Piece Asset Name",
                firstPieceAssetName);

        secondPieceAssetName =
            EditorGUILayout.TextField(
                "Second Piece Asset Name",
                secondPieceAssetName);
    }

    private void SelectOutputFolder()
    {
        string startFolder = Application.dataPath;

        if (TryNormalizeOutputFolder(
                outputFolder,
                out string normalizedCurrentFolder,
                out _))
        {
            startFolder =
                ConvertAssetPathToAbsolutePath(
                    normalizedCurrentFolder);
        }

        string selectedFolder =
            EditorUtility.OpenFolderPanel(
                "Shield Piece Mesh Output Folder",
                startFolder,
                string.Empty);

        if (string.IsNullOrEmpty(selectedFolder))
        {
            return;
        }

        if (!TryConvertAbsoluteFolderToAssetFolder(
                selectedFolder,
                out string assetFolder,
                out string failureReason))
        {
            SetError(failureReason, true);
            return;
        }

        outputFolder = assetFolder;
        ClearStatus();
    }

    private void GenerateSplitMeshAssets()
    {
        if (!TryValidateGenerationInputs(
                out Mesh sourceMesh,
                out Plane cutPlane,
                out string normalizedFirstPieceName,
                out string normalizedSecondPieceName,
                out string firstPieceAssetPath,
                out string secondPieceAssetPath,
                out string validationFailureReason))
        {
            SetError(validationFailureReason, true);
            return;
        }

        Mesh firstPieceMesh = null;
        Mesh secondPieceMesh = null;
        bool firstAssetCreated = false;
        bool secondAssetCreated = false;

        try
        {
            var splitter =
                new MeshPlaneSplitter();

            if (!TrySplitWithBoundaryDiagnostics(
                    splitter,
                    sourceMesh,
                    cutPlane,
                    normalizedFirstPieceName,
                    normalizedSecondPieceName,
                    out firstPieceMesh,
                    out secondPieceMesh,
                    out string splitFailureReason))
            {
                DestroyTemporaryMesh(firstPieceMesh);
                DestroyTemporaryMesh(secondPieceMesh);

                string message =
                    "Shield Meshの分割に失敗しました。\n" +
                    (string.IsNullOrEmpty(splitFailureReason)
                        ? "MeshPlaneSplitterから失敗理由が返されませんでした。"
                        : splitFailureReason);

                SetError(message, true);
                return;
            }

            if (firstPieceMesh == null || secondPieceMesh == null)
            {
                DestroyTemporaryMesh(firstPieceMesh);
                DestroyTemporaryMesh(secondPieceMesh);

                SetError(
                    "MeshPlaneSplitterは成功を返しましたが、生成Meshが不足しています。",
                    true);
                return;
            }

            // MeshPlaneSplitterはSource Meshのローカル頂点座標を維持して生成する。
            // ここでは頂点を再中心化せず、Asset上の表示名だけを確定する。
            firstPieceMesh.name = normalizedFirstPieceName;
            secondPieceMesh.name = normalizedSecondPieceName;

            AssetDatabase.CreateAsset(
                firstPieceMesh,
                firstPieceAssetPath);
            firstAssetCreated = true;

            AssetDatabase.CreateAsset(
                secondPieceMesh,
                secondPieceAssetPath);
            secondAssetCreated = true;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.objects =
                new UnityEngine.Object[]
                {
                    firstPieceMesh,
                    secondPieceMesh
                };

            EditorGUIUtility.PingObject(firstPieceMesh);

            string successMessage =
                "Shield Piece Meshを生成しました。\n" +
                firstPieceAssetPath + "\n" +
                secondPieceAssetPath;

            SetSuccess(successMessage);
            Debug.Log(successMessage);
        }
        catch (Exception exception)
        {
            // CreateAssetは既存Assetを上書きできるAPIなので、事前検証に加えて
            // 途中失敗時も今回作成したAssetだけを削除し、中途半端な結果を残さない。
            if (secondAssetCreated)
            {
                AssetDatabase.DeleteAsset(secondPieceAssetPath);
            }

            if (firstAssetCreated)
            {
                AssetDatabase.DeleteAsset(firstPieceAssetPath);
            }

            DestroyTemporaryMesh(firstPieceMesh);
            DestroyTemporaryMesh(secondPieceMesh);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.LogException(exception);

            SetError(
                "Shield Piece Meshの生成中に例外が発生しました。\n" +
                exception.Message,
                true);
        }
    }

    private bool TrySplitWithBoundaryDiagnostics(
        MeshPlaneSplitter splitter,
        Mesh sourceMesh,
        Plane cutPlane,
        string firstPieceMeshName,
        string secondPieceMeshName,
        out Mesh firstPieceMesh,
        out Mesh secondPieceMesh,
        out string failureReason)
    {
        firstPieceMesh = null;
        secondPieceMesh = null;
        failureReason = null;

        Type cutBoundaryValidatorType =
            typeof(Func<IReadOnlyList<Vector3>, string>);

        MethodInfo diagnosticTrySplit =
            typeof(MeshPlaneSplitter).GetMethod(
                "TrySplit",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(Mesh),
                    typeof(Plane),
                    typeof(float),
                    typeof(string),
                    typeof(string),
                    cutBoundaryValidatorType,
                    typeof(bool),
                    typeof(bool),
                    typeof(Mesh).MakeByRefType(),
                    typeof(Mesh).MakeByRefType(),
                    typeof(string).MakeByRefType()
                },
                null);

        if (diagnosticTrySplit == null)
        {
            failureReason =
                "MeshPlaneSplitterの診断付きTrySplitを取得できませんでした。" +
                "MeshPlaneSplitterの内部APIが変更されていないか確認してください。";
            return false;
        }

        object[] arguments =
        {
            sourceMesh,
            cutPlane,
            cutSurfaceUvScale,
            firstPieceMeshName,
            secondPieceMeshName,
            null,
            true,
            true,
            null,
            null,
            null
        };

        try
        {
            // Shield Editor生成だけ、診断とPlane上頂点fan対応を有効化する。
            // Runtimeの既存public TrySplitはfalseを渡すため、従来アルゴリズムのまま動作する。
            object invocationResult =
                diagnosticTrySplit.Invoke(
                    splitter,
                    arguments);

            firstPieceMesh =
                arguments[8] as Mesh;

            secondPieceMesh =
                arguments[9] as Mesh;

            failureReason =
                arguments[10] as string;

            if (invocationResult is bool succeeded)
            {
                return succeeded;
            }

            failureReason =
                "MeshPlaneSplitterの診断付きTrySplitから有効な結果を取得できませんでした。";
            return false;
        }
        catch (TargetInvocationException exception)
        {
            firstPieceMesh =
                arguments[8] as Mesh;

            secondPieceMesh =
                arguments[9] as Mesh;

            Exception actualException =
                exception.InnerException ?? exception;

            failureReason =
                "MeshPlaneSplitterの診断付き分割中に例外が発生しました。\n" +
                actualException.Message;

            Debug.LogException(actualException);
            return false;
        }
        catch (Exception exception)
        {
            failureReason =
                "MeshPlaneSplitterの診断付き呼び出しに失敗しました。\n" +
                exception.Message;

            Debug.LogException(exception);
            return false;
        }
    }

    private bool TryValidateGenerationInputs(
        out Mesh sourceMesh,
        out Plane cutPlane,
        out string normalizedFirstPieceName,
        out string normalizedSecondPieceName,
        out string firstPieceAssetPath,
        out string secondPieceAssetPath,
        out string failureReason)
    {
        sourceMesh = null;
        cutPlane = default;
        normalizedFirstPieceName = null;
        normalizedSecondPieceName = null;
        firstPieceAssetPath = null;
        secondPieceAssetPath = null;

        if (sourceMeshFilter == null)
        {
            failureReason =
                "Source MeshFilterを指定してください。";
            return false;
        }

        sourceMesh = sourceMeshFilter.sharedMesh;
        if (sourceMesh == null)
        {
            failureReason =
                "Source MeshFilterにsharedMeshが設定されていません。";
            return false;
        }

        if (cutPointA == null)
        {
            failureReason =
                "Cut Point Aを指定してください。";
            return false;
        }

        if (cutPointB == null)
        {
            failureReason =
                "Cut Point Bを指定してください。";
            return false;
        }

        if (cutDepthPoint == null)
        {
            failureReason =
                "Cut Depth Pointを指定してください。";
            return false;
        }

        if (!IsFinite(cutSurfaceUvScale) ||
            cutSurfaceUvScale <= 0.0f)
        {
            failureReason =
                "Cut Surface UV Scaleは0より大きい有限値を指定してください。";
            return false;
        }

        if (!TryBuildLocalCutPlane(
                sourceMesh,
                out cutPlane,
                out failureReason))
        {
            return false;
        }

        if (!TryNormalizeOutputFolder(
                outputFolder,
                out string normalizedOutputFolder,
                out failureReason))
        {
            return false;
        }

        if (!TryNormalizeAssetName(
                firstPieceAssetName,
                "First Piece Asset Name",
                out normalizedFirstPieceName,
                out failureReason))
        {
            return false;
        }

        if (!TryNormalizeAssetName(
                secondPieceAssetName,
                "Second Piece Asset Name",
                out normalizedSecondPieceName,
                out failureReason))
        {
            return false;
        }

        if (string.Equals(
                normalizedFirstPieceName,
                normalizedSecondPieceName,
                StringComparison.OrdinalIgnoreCase))
        {
            failureReason =
                "First PieceとSecond Pieceには異なるAsset名を指定してください。";
            return false;
        }

        firstPieceAssetPath =
            normalizedOutputFolder + "/" +
            normalizedFirstPieceName + ".asset";

        secondPieceAssetPath =
            normalizedOutputFolder + "/" +
            normalizedSecondPieceName + ".asset";

        if (AssetPathExists(firstPieceAssetPath))
        {
            failureReason =
                "既存Assetを上書きしないため生成を中止しました。\n" +
                firstPieceAssetPath;
            return false;
        }

        if (AssetPathExists(secondPieceAssetPath))
        {
            failureReason =
                "既存Assetを上書きしないため生成を中止しました。\n" +
                secondPieceAssetPath;
            return false;
        }

        failureReason = null;
        return true;
    }

    private bool TryBuildLocalCutPlane(
        Mesh sourceMesh,
        out Plane cutPlane,
        out string failureReason)
    {
        Transform sourceTransform =
            sourceMeshFilter.transform;

        // Scene上で調整する3点をMeshローカル空間へ揃えることで、
        // X/Y/X/Zなどゲーム固有の軸を仮定せず、Mesh頂点と同じ座標空間でPlaneを定義する。
        Vector3 localPointA =
            sourceTransform.InverseTransformPoint(
                cutPointA.position);

        Vector3 localPointB =
            sourceTransform.InverseTransformPoint(
                cutPointB.position);

        Vector3 localDepthPoint =
            sourceTransform.InverseTransformPoint(
                cutDepthPoint.position);

        if (!IsFinite(localPointA) ||
            !IsFinite(localPointB) ||
            !IsFinite(localDepthPoint))
        {
            cutPlane = default;
            failureReason =
                "Cut PointをSource Meshのローカル座標へ変換できませんでした。Source MeshFilterのTransformを確認してください。";
            return false;
        }

        float pointDistanceEpsilon =
            Mathf.Max(
                sourceMesh.bounds.size.magnitude *
                POINT_DISTANCE_EPSILON_RATIO,
                MINIMUM_POINT_DISTANCE_EPSILON);

        float pointDistanceEpsilonSquared =
            pointDistanceEpsilon *
            pointDistanceEpsilon;

        Vector3 lineDirection =
            localPointB - localPointA;

        Vector3 depthDirection =
            localDepthPoint - localPointA;

        Vector3 remainingDirection =
            localDepthPoint - localPointB;

        if (lineDirection.sqrMagnitude <= pointDistanceEpsilonSquared ||
            depthDirection.sqrMagnitude <= pointDistanceEpsilonSquared ||
            remainingDirection.sqrMagnitude <= pointDistanceEpsilonSquared)
        {
            cutPlane = default;
            failureReason =
                "Cut Point A / B / Depth Pointのうち2点以上が同一点または近すぎます。3点を離して配置してください。";
            return false;
        }

        Vector3 cross =
            Vector3.Cross(
                lineDirection,
                depthDirection);

        float directionMagnitudeProduct =
            lineDirection.sqrMagnitude *
            depthDirection.sqrMagnitude;

        if (cross.sqrMagnitude <=
            directionMagnitudeProduct *
            COLLINEAR_SIN_SQUARED_EPSILON)
        {
            cutPlane = default;
            failureReason =
                "Cut Point A / B / Depth Pointが一直線上またはほぼ一直線上です。Cut Depth Pointを切断線からShieldの厚み方向へ離してください。";
            return false;
        }

        cutPlane =
            new Plane(
                localPointA,
                localPointB,
                localDepthPoint);

        if (!IsFinite(cutPlane.normal) ||
            !IsFinite(cutPlane.distance) ||
            cutPlane.normal.sqrMagnitude <= 0.0f)
        {
            cutPlane = default;
            failureReason =
                "指定した3点から有効なCut Planeを構築できませんでした。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryNormalizeOutputFolder(
        string folder,
        out string normalizedFolder,
        out string failureReason)
    {
        normalizedFolder = null;

        if (string.IsNullOrWhiteSpace(folder))
        {
            failureReason =
                "Output Folderを指定してください。";
            return false;
        }

        string requestedFolder =
            folder.Trim()
                .Replace('\\', '/')
                .TrimEnd('/');

        if (requestedFolder.Length == 0)
        {
            failureReason =
                "Output Folderを指定してください。";
            return false;
        }

        string projectRoot =
            Path.GetDirectoryName(Application.dataPath);

        if (string.IsNullOrEmpty(projectRoot))
        {
            failureReason =
                "Unity Projectのルートパスを取得できませんでした。";
            return false;
        }

        string absoluteAssetsFolder =
            NormalizeAbsolutePath(
                Path.GetFullPath(Application.dataPath));

        string absoluteRequestedFolder;

        try
        {
            absoluteRequestedFolder =
                NormalizeAbsolutePath(
                    Path.GetFullPath(
                        Path.Combine(
                            projectRoot,
                            requestedFolder)));
        }
        catch (Exception)
        {
            failureReason =
                "Output Folderのパスが不正です。";
            return false;
        }

        bool isAssetsRoot =
            string.Equals(
                absoluteRequestedFolder,
                absoluteAssetsFolder,
                StringComparison.OrdinalIgnoreCase);

        bool isInsideAssets =
            absoluteRequestedFolder.StartsWith(
                absoluteAssetsFolder + "/",
                StringComparison.OrdinalIgnoreCase);

        if (!isAssetsRoot && !isInsideAssets)
        {
            failureReason =
                "Output FolderはUnity ProjectのAssets配下を指定してください。";
            return false;
        }

        normalizedFolder =
            isAssetsRoot
                ? "Assets"
                : "Assets" +
                  absoluteRequestedFolder.Substring(
                      absoluteAssetsFolder.Length);

        if (!AssetDatabase.IsValidFolder(normalizedFolder))
        {
            failureReason =
                "Output FolderがProject内の既存フォルダーとして認識されていません。\n" +
                normalizedFolder;
            normalizedFolder = null;
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryConvertAbsoluteFolderToAssetFolder(
        string absoluteFolder,
        out string assetFolder,
        out string failureReason)
    {
        assetFolder = null;

        string absoluteAssetsFolder =
            NormalizeAbsolutePath(
                Path.GetFullPath(Application.dataPath));

        string normalizedAbsoluteFolder;

        try
        {
            normalizedAbsoluteFolder =
                NormalizeAbsolutePath(
                    Path.GetFullPath(absoluteFolder));
        }
        catch (Exception)
        {
            failureReason =
                "選択したOutput Folderのパスが不正です。";
            return false;
        }

        bool isAssetsRoot =
            string.Equals(
                normalizedAbsoluteFolder,
                absoluteAssetsFolder,
                StringComparison.OrdinalIgnoreCase);

        bool isInsideAssets =
            normalizedAbsoluteFolder.StartsWith(
                absoluteAssetsFolder + "/",
                StringComparison.OrdinalIgnoreCase);

        if (!isAssetsRoot && !isInsideAssets)
        {
            failureReason =
                "Output FolderはUnity ProjectのAssets配下を選択してください。";
            return false;
        }

        assetFolder =
            isAssetsRoot
                ? "Assets"
                : "Assets" +
                  normalizedAbsoluteFolder.Substring(
                      absoluteAssetsFolder.Length);

        if (!AssetDatabase.IsValidFolder(assetFolder))
        {
            failureReason =
                "選択したフォルダーがUnity Project内の有効なAssetフォルダーではありません。\n" +
                assetFolder;
            assetFolder = null;
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryNormalizeAssetName(
        string assetName,
        string fieldName,
        out string normalizedAssetName,
        out string failureReason)
    {
        normalizedAssetName = null;

        if (string.IsNullOrWhiteSpace(assetName))
        {
            failureReason =
                fieldName + "を指定してください。";
            return false;
        }

        normalizedAssetName =
            assetName.Trim();

        const string assetExtension = ".asset";
        if (normalizedAssetName.EndsWith(
                assetExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            normalizedAssetName =
                normalizedAssetName.Substring(
                    0,
                    normalizedAssetName.Length -
                    assetExtension.Length);
        }

        if (string.IsNullOrWhiteSpace(normalizedAssetName) ||
            normalizedAssetName == "." ||
            normalizedAssetName == "..")
        {
            failureReason =
                fieldName + "に有効な名前を指定してください。";
            normalizedAssetName = null;
            return false;
        }

        if (normalizedAssetName.IndexOf('/') >= 0 ||
            normalizedAssetName.IndexOf('\\') >= 0 ||
            normalizedAssetName.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
        {
            failureReason =
                fieldName +
                "にはフォルダー区切り文字やファイル名に使用できない文字を含めないでください。";
            normalizedAssetName = null;
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool AssetPathExists(
        string assetPath)
    {
        if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
        {
            return true;
        }

        return File.Exists(
            ConvertAssetPathToAbsolutePath(
                assetPath));
    }

    private static string ConvertAssetPathToAbsolutePath(
        string assetPath)
    {
        string projectRoot =
            Path.GetDirectoryName(Application.dataPath);

        if (string.IsNullOrEmpty(projectRoot))
        {
            return assetPath;
        }

        return NormalizeAbsolutePath(
            Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    assetPath)));
    }

    private static string NormalizeAbsolutePath(
        string path)
    {
        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static void DestroyTemporaryMesh(
        Mesh mesh)
    {
        if (mesh == null ||
            EditorUtility.IsPersistent(mesh))
        {
            return;
        }

        UnityEngine.Object.DestroyImmediate(mesh);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z);
    }

    private void SetSuccess(
        string message)
    {
        statusMessage = message;
        statusMessageType = MessageType.Info;
    }

    private void SetError(
        string message,
        bool showDialog)
    {
        statusMessage = message;
        statusMessageType = MessageType.Error;

        Debug.LogError(message);

        if (showDialog)
        {
            EditorUtility.DisplayDialog(
                "Shield Split Mesh Generator",
                message,
                "OK");
        }
    }

    private void ClearStatus()
    {
        statusMessage = null;
        statusMessageType = MessageType.None;
    }
}
