using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Playerモデルの複製へ攻撃AnimationClipをEditor上でサンプリングし、
/// Chainsaw Tip / Rootの完全な軌跡を<see cref="ChainsawAttackTrajectoryProfile"/>へBakeする。
/// </summary>
/// <remarks>
/// RuntimeのMesh切断、Player攻撃制御、Enemy制御は担当しない。
///
/// Bake時はScene上の実PlayerへAnimationを適用せず、
/// Preview Scene上へPlayer Rootを複製し、その複製だけをサンプリングする。
///
/// Player RootにはRuntimeでPlayer Tagが付くGameObjectのTransformを指定する。
/// Trajectory Referenceには、そのPlayer Root自身または子階層のTransformを指定する。
/// Bakeした相対Hierarchy PathをRuntimeでも使用するため、Player階層を変更した場合は再Bakeが必要になる。
/// </remarks>
public sealed class ChainsawAttackTrajectoryBaker : EditorWindow
{
    private const float DEFAULT_SAMPLE_RATE = 60.0f;
    private const float MINIMUM_SAMPLE_RATE = 1.0f;
    private const int MINIMUM_SAMPLE_COUNT = 2;
    private const float COORDINATE_ROUND_TRIP_WARNING_THRESHOLD = 0.001f;
    private const float REFERENCE_POSITION_CHANGE_WARNING_THRESHOLD = 0.001f;
    private const float REFERENCE_ROTATION_CHANGE_WARNING_DEGREES = 0.1f;
    private const float REFERENCE_SCALE_CHANGE_WARNING_THRESHOLD = 0.001f;

    private GameObject animationRoot;
    private Transform playerRootTransform;
    private Transform trajectoryReferenceTransform;
    private Transform tipTransform;
    private Transform rootTransform;
    private AnimationClip animationClip;
    private AttackData attackData;
    private ChainsawAttackTrajectoryProfile targetProfile;

    private float samplesPerSecond = DEFAULT_SAMPLE_RATE;
    private bool showTrajectory = true;
    private bool showBladeSegments = true;
    private bool enableCoordinateValidationLog = true;

    [MenuItem("Tools/Chainsaw/Attack Trajectory Baker")]
    private static void OpenWindow()
    {
        ChainsawAttackTrajectoryBaker window =
            GetWindow<ChainsawAttackTrajectoryBaker>();

        window.titleContent =
            new GUIContent("Chainsaw Trajectory Baker");

        window.minSize =
            new Vector2(420.0f, 520.0f);
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += DrawTrajectoryInSceneView;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DrawTrajectoryInSceneView;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(
            "チェンソー通常攻撃 軌跡Bake",
            EditorStyles.boldLabel);

        EditorGUILayout.Space();
        DrawBakeSourceFields();

        EditorGUILayout.Space();
        DrawSamplingFields();

        EditorGUILayout.Space();
        DrawOutputFields();

        EditorGUILayout.Space();
        DrawVisualizationFields();

        EditorGUILayout.Space();
        DrawDiagnosticFields();

        EditorGUILayout.Space();
        DrawBakeButtons();
    }

    private void DrawBakeSourceFields()
    {
        EditorGUILayout.LabelField(
            "Bake元",
            EditorStyles.boldLabel);

        animationRoot =
            (GameObject)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Animation Root",
                    "AnimationMode.SampleAnimationClipへ渡すGameObject。攻撃AnimationClipのBinding基準となるRootを指定する。"),
                animationRoot,
                typeof(GameObject),
                true);

        playerRootTransform =
            (Transform)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Player Root",
                    "RuntimeでPlayer Tagが付くGameObjectと同じTransformを指定する。"),
                playerRootTransform,
                typeof(Transform),
                true);

        trajectoryReferenceTransform =
            (Transform)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Trajectory Reference",
                    "Tip / Root位置をローカル座標化する基準Transform。Player Root自身または子階層を指定する。"),
                trajectoryReferenceTransform,
                typeof(Transform),
                true);

        tipTransform =
            (Transform)EditorGUILayout.ObjectField(
                "Chainsaw Tip",
                tipTransform,
                typeof(Transform),
                true);

        rootTransform =
            (Transform)EditorGUILayout.ObjectField(
                "Chainsaw Root",
                rootTransform,
                typeof(Transform),
                true);

        animationClip =
            (AnimationClip)EditorGUILayout.ObjectField(
                "Animation Clip",
                animationClip,
                typeof(AnimationClip),
                false);

        attackData =
            (AttackData)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Attack Data",
                    "この軌跡を使用するSlashのAttackData。RuntimeでProfile選択に使用する。"),
                attackData,
                typeof(AttackData),
                false);
    }

    private void DrawSamplingFields()
    {
        EditorGUILayout.LabelField(
            "Sampling",
            EditorStyles.boldLabel);

        samplesPerSecond =
            Mathf.Max(
                MINIMUM_SAMPLE_RATE,
                EditorGUILayout.FloatField(
                    new GUIContent(
                        "Samples Per Second",
                        "RuntimeのUpdate回数とは無関係な固定サンプリング密度。"),
                    samplesPerSecond));

        using (
            new EditorGUI.DisabledScope(
                animationClip == null))
        {
            if (GUILayout.Button(
                    "AnimationClip Frame Rateを使用"))
            {
                samplesPerSecond =
                    Mathf.Max(
                        MINIMUM_SAMPLE_RATE,
                        animationClip.frameRate);
            }
        }

        if (animationClip != null)
        {
            int sampleCount =
                CalculateSampleCount(
                    animationClip.length,
                    samplesPerSecond);

            EditorGUILayout.LabelField(
                "予定サンプル数",
                sampleCount.ToString());
        }
    }

    private void DrawOutputFields()
    {
        EditorGUILayout.LabelField(
            "出力",
            EditorStyles.boldLabel);

        targetProfile =
            (ChainsawAttackTrajectoryProfile)EditorGUILayout.ObjectField(
                "Target Profile",
                targetProfile,
                typeof(ChainsawAttackTrajectoryProfile),
                false);

        if (targetProfile != null)
        {
            EditorGUILayout.LabelField(
                "現在のSample数",
                targetProfile.SampleCount.ToString());
        }
    }

    private void DrawVisualizationFields()
    {
        EditorGUILayout.LabelField(
            "Scene View表示",
            EditorStyles.boldLabel);

        showTrajectory =
            EditorGUILayout.Toggle(
                "軌跡を表示",
                showTrajectory);

        using (
            new EditorGUI.DisabledScope(
                !showTrajectory))
        {
            showBladeSegments =
                EditorGUILayout.Toggle(
                    "Tip / Rootを接続",
                    showBladeSegments);
        }
    }

    private void DrawDiagnosticFields()
    {
        EditorGUILayout.LabelField(
            "座標変換診断",
            EditorStyles.boldLabel);

        enableCoordinateValidationLog =
            EditorGUILayout.Toggle(
                new GUIContent(
                    "Bake時の座標検証ログ",
                    "World→Local→Worldの往復誤差、Player Root / Trajectory Reference、先頭・中央・末尾サンプルをConsoleへ出力する。"),
                enableCoordinateValidationLog);
    }

    private void DrawBakeButtons()
    {
        using (
            new EditorGUI.DisabledScope(
                targetProfile == null))
        {
            if (GUILayout.Button(
                    "Target ProfileへBake"))
            {
                BakeIntoProfile(
                    targetProfile);
            }
        }

        if (GUILayout.Button(
                "新しいProfileを作成してBake"))
        {
            CreateProfileAndBake();
        }
    }

    private void CreateProfileAndBake()
    {
        if (!ValidateBakeSettings(
                out string validationMessage))
        {
            EditorUtility.DisplayDialog(
                "Chainsaw Trajectory Bake",
                validationMessage,
                "OK");
            return;
        }

        string defaultName =
            animationClip != null
                ? $"{animationClip.name}_TrajectoryProfile.asset"
                : "ChainsawAttackTrajectoryProfile.asset";

        string assetPath =
            EditorUtility.SaveFilePanelInProject(
                "軌跡Profileの保存先",
                defaultName,
                "asset",
                "Bake済みチェンソー軌跡Profileの保存先を選択してください。");

        if (string.IsNullOrEmpty(assetPath))
        {
            return;
        }

        assetPath =
            AssetDatabase.GenerateUniqueAssetPath(
                assetPath);

        ChainsawAttackTrajectoryProfile createdProfile =
            CreateInstance<ChainsawAttackTrajectoryProfile>();

        AssetDatabase.CreateAsset(
            createdProfile,
            assetPath);

        targetProfile = createdProfile;

        BakeIntoProfile(
            createdProfile);

        Selection.activeObject =
            createdProfile;

        EditorGUIUtility.PingObject(
            createdProfile);
    }

    private void BakeIntoProfile(
        ChainsawAttackTrajectoryProfile profile)
    {
        if (profile == null)
        {
            return;
        }

        if (!ValidateBakeSettings(
                out string validationMessage))
        {
            EditorUtility.DisplayDialog(
                "Chainsaw Trajectory Bake",
                validationMessage,
                "OK");
            return;
        }

        if (AnimationMode.InAnimationMode())
        {
            EditorUtility.DisplayDialog(
                "Chainsaw Trajectory Bake",
                "Animation Modeが既に有効です。Animation Window等のAnimation Modeを終了してからBakeしてください。",
                "OK");
            return;
        }

        if (!TryBuildRelativePath(
                playerRootTransform,
                trajectoryReferenceTransform,
                out string referencePath))
        {
            EditorUtility.DisplayDialog(
                "Chainsaw Trajectory Bake",
                "Trajectory ReferenceはPlayer Root自身、またはPlayer Rootの子階層である必要があります。",
                "OK");
            return;
        }

        if (!TryBuildRelativePath(
                playerRootTransform,
                tipTransform,
                out string tipPath) ||
            !TryBuildRelativePath(
                playerRootTransform,
                rootTransform,
                out string rootPath))
        {
            EditorUtility.DisplayDialog(
                "Chainsaw Trajectory Bake",
                "Chainsaw Tip / RootのPlayer RootからのHierarchy Pathを取得できませんでした。",
                "OK");
            return;
        }

        if (!TrySampleTrajectory(
                out List<ChainsawBladeTrajectorySample> bakedSamples,
                out string failureReason))
        {
            EditorUtility.DisplayDialog(
                "Chainsaw Trajectory Bake",
                failureReason,
                "OK");
            return;
        }

        Undo.RecordObject(
            profile,
            "Bake Chainsaw Attack Trajectory");

        profile.SetBakedData(
            attackData,
            animationClip,
            playerRootTransform.name,
            trajectoryReferenceTransform.name,
            referencePath,
            tipPath,
            rootPath,
            playerRootTransform.lossyScale,
            trajectoryReferenceTransform.lossyScale,
            bakedSamples);

        EditorUtility.SetDirty(
            profile);

        AssetDatabase.SaveAssets();

        SceneView.RepaintAll();
        Repaint();

        Debug.Log(
            $"{nameof(ChainsawAttackTrajectoryBaker)}: " +
            $"{profile.name}へ{bakedSamples.Count}サンプルをBakeしました。 " +
            $"AnimationClip: {animationClip.name}, " +
            $"Reference Path: {(string.IsNullOrEmpty(referencePath) ? "<Player Root>" : referencePath)}",
            profile);
    }

    private bool TrySampleTrajectory(
        out List<ChainsawBladeTrajectorySample> bakedSamples,
        out string failureReason)
    {
        bakedSamples = null;

        int sampleCount =
            CalculateSampleCount(
                animationClip.length,
                samplesPerSecond);

        if (sampleCount < MINIMUM_SAMPLE_COUNT)
        {
            failureReason =
                "軌跡には最低2サンプル必要です。";
            return false;
        }

        if (!TryBuildRelativePath(
                playerRootTransform,
                animationRoot.transform,
                out string animationRootPath) ||
            !TryBuildRelativePath(
                playerRootTransform,
                trajectoryReferenceTransform,
                out string trajectoryReferencePath) ||
            !TryBuildRelativePath(
                playerRootTransform,
                tipTransform,
                out string tipPath) ||
            !TryBuildRelativePath(
                playerRootTransform,
                rootTransform,
                out string rootPath))
        {
            failureReason =
                "Bake対象TransformのHierarchy PathをPlayer Rootから取得できませんでした。";
            return false;
        }

        bakedSamples =
            new List<ChainsawBladeTrajectorySample>(
                sampleCount);

        Scene previewScene = default;
        bool previewSceneCreated = false;
        bool animationModeStarted = false;

        float maximumTipRoundTripError = 0.0f;
        float maximumRootRoundTripError = 0.0f;
        float maximumReferencePositionChange = 0.0f;
        float maximumReferenceRotationChange = 0.0f;
        float maximumReferenceScaleChange = 0.0f;

        Vector3 firstReferencePositionInPlayer = default;
        Quaternion firstReferenceRotationInPlayer = default;
        Vector3 firstReferenceLossyScale = default;
        bool hasFirstReferencePose = false;

        string firstSampleDiagnostic = null;
        string middleSampleDiagnostic = null;
        string lastSampleDiagnostic = null;

        try
        {
            // 実Scene上のPlayerへSampleAnimationClipを適用すると、
            // Humanoidの姿勢変更がEditor上に残る可能性がある。
            // そのためBake専用Preview SceneにPlayerを複製し、複製側だけをサンプリングする。
            previewScene =
                EditorSceneManager.NewPreviewScene();

            previewSceneCreated =
                previewScene.IsValid();

            if (!previewSceneCreated)
            {
                bakedSamples = null;
                failureReason =
                    "Bake用Preview Sceneを作成できませんでした。";
                return false;
            }

            GameObject previewPlayerRootObject =
                Instantiate(
                    playerRootTransform.gameObject);

            if (previewPlayerRootObject == null)
            {
                bakedSamples = null;
                failureReason =
                    "Bake用Playerの複製に失敗しました。";
                return false;
            }

            previewPlayerRootObject.name =
                $"{playerRootTransform.name}_TrajectoryBakePreview";

            SceneManager.MoveGameObjectToScene(
                previewPlayerRootObject,
                previewScene);

            Transform previewPlayerRoot =
                previewPlayerRootObject.transform;

            if (!TryResolveRelativeTransform(
                    previewPlayerRoot,
                    animationRootPath,
                    "Animation Root",
                    out Transform previewAnimationRoot,
                    out failureReason) ||
                !TryResolveRelativeTransform(
                    previewPlayerRoot,
                    trajectoryReferencePath,
                    "Trajectory Reference",
                    out Transform previewTrajectoryReference,
                    out failureReason) ||
                !TryResolveRelativeTransform(
                    previewPlayerRoot,
                    tipPath,
                    "Chainsaw Tip",
                    out Transform previewTip,
                    out failureReason) ||
                !TryResolveRelativeTransform(
                    previewPlayerRoot,
                    rootPath,
                    "Chainsaw Root",
                    out Transform previewRoot,
                    out failureReason))
            {
                bakedSamples = null;
                return false;
            }

            AnimationMode.StartAnimationMode();
            animationModeStarted = true;

            for (int i = 0; i < sampleCount; i++)
            {
                float normalizedTime =
                    sampleCount <= 1
                        ? 0.0f
                        : i / (float)(sampleCount - 1);

                float sampleTime =
                    animationClip.length * normalizedTime;

                AnimationMode.BeginSampling();

                try
                {
                    AnimationMode.SampleAnimationClip(
                        previewAnimationRoot.gameObject,
                        animationClip,
                        sampleTime);
                }
                finally
                {
                    AnimationMode.EndSampling();
                }

                Vector3 tipWorldPosition =
                    previewTip.position;

                Vector3 rootWorldPosition =
                    previewRoot.position;

                Vector3 tipLocalPosition =
                    previewTrajectoryReference
                        .InverseTransformPoint(
                            tipWorldPosition);

                Vector3 rootLocalPosition =
                    previewTrajectoryReference
                        .InverseTransformPoint(
                            rootWorldPosition);

                Vector3 tipRoundTripWorldPosition =
                    previewTrajectoryReference
                        .TransformPoint(
                            tipLocalPosition);

                Vector3 rootRoundTripWorldPosition =
                    previewTrajectoryReference
                        .TransformPoint(
                            rootLocalPosition);

                float tipRoundTripError =
                    Vector3.Distance(
                        tipWorldPosition,
                        tipRoundTripWorldPosition);

                float rootRoundTripError =
                    Vector3.Distance(
                        rootWorldPosition,
                        rootRoundTripWorldPosition);

                maximumTipRoundTripError =
                    Mathf.Max(
                        maximumTipRoundTripError,
                        tipRoundTripError);

                maximumRootRoundTripError =
                    Mathf.Max(
                        maximumRootRoundTripError,
                        rootRoundTripError);

                Vector3 referencePositionInPlayer =
                    previewPlayerRoot.InverseTransformPoint(
                        previewTrajectoryReference.position);

                Quaternion referenceRotationInPlayer =
                    Quaternion.Inverse(
                        previewPlayerRoot.rotation) *
                    previewTrajectoryReference.rotation;

                Vector3 referenceLossyScale =
                    previewTrajectoryReference.lossyScale;

                if (!hasFirstReferencePose)
                {
                    firstReferencePositionInPlayer =
                        referencePositionInPlayer;

                    firstReferenceRotationInPlayer =
                        referenceRotationInPlayer;

                    firstReferenceLossyScale =
                        referenceLossyScale;

                    hasFirstReferencePose = true;
                }
                else
                {
                    maximumReferencePositionChange =
                        Mathf.Max(
                            maximumReferencePositionChange,
                            Vector3.Distance(
                                firstReferencePositionInPlayer,
                                referencePositionInPlayer));

                    maximumReferenceRotationChange =
                        Mathf.Max(
                            maximumReferenceRotationChange,
                            Quaternion.Angle(
                                firstReferenceRotationInPlayer,
                                referenceRotationInPlayer));

                    maximumReferenceScaleChange =
                        Mathf.Max(
                            maximumReferenceScaleChange,
                            Vector3.Distance(
                                firstReferenceLossyScale,
                                referenceLossyScale));
                }

                bakedSamples.Add(
                    new ChainsawBladeTrajectorySample(
                        tipLocalPosition,
                        rootLocalPosition,
                        sampleTime));

                if (enableCoordinateValidationLog)
                {
                    string sampleDiagnostic =
                        BuildBakeSampleDiagnostic(
                            i,
                            sampleTime,
                            tipWorldPosition,
                            tipLocalPosition,
                            tipRoundTripWorldPosition,
                            tipRoundTripError,
                            rootWorldPosition,
                            rootLocalPosition,
                            rootRoundTripWorldPosition,
                            rootRoundTripError);

                    if (i == 0)
                    {
                        firstSampleDiagnostic =
                            sampleDiagnostic;
                    }

                    if (i == sampleCount / 2)
                    {
                        middleSampleDiagnostic =
                            sampleDiagnostic;
                    }

                    if (i == sampleCount - 1)
                    {
                        lastSampleDiagnostic =
                            sampleDiagnostic;
                    }
                }
            }
        }
        catch (System.Exception exception)
        {
            bakedSamples = null;
            failureReason =
                "AnimationClipのサンプリング中に例外が発生しました。\n" +
                exception.Message;
            return false;
        }
        finally
        {
            if (animationModeStarted &&
                AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }

            if (previewSceneCreated &&
                previewScene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(
                    previewScene);
            }
        }

        if (enableCoordinateValidationLog)
        {
            LogBakeCoordinateValidation(
                trajectoryReferencePath,
                maximumTipRoundTripError,
                maximumRootRoundTripError,
                maximumReferencePositionChange,
                maximumReferenceRotationChange,
                maximumReferenceScaleChange,
                firstSampleDiagnostic,
                middleSampleDiagnostic,
                lastSampleDiagnostic);
        }

        failureReason = null;
        return true;
    }

    private void LogBakeCoordinateValidation(
        string referencePath,
        float maximumTipRoundTripError,
        float maximumRootRoundTripError,
        float maximumReferencePositionChange,
        float maximumReferenceRotationChange,
        float maximumReferenceScaleChange,
        string firstSampleDiagnostic,
        string middleSampleDiagnostic,
        string lastSampleDiagnostic)
    {
        string message =
            "[TrajectoryCoordinateValidation][Bake]\n" +
            $"PlayerRoot='{playerRootTransform.name}', " +
            $"PlayerRootPosition={FormatVector3(playerRootTransform.position)}, " +
            $"PlayerRootLossyScale={FormatVector3(playerRootTransform.lossyScale)}\n" +
            $"TrajectoryReference='{trajectoryReferenceTransform.name}', " +
            $"ReferencePath='{(string.IsNullOrEmpty(referencePath) ? "<Player Root>" : referencePath)}', " +
            $"ReferencePosition={FormatVector3(trajectoryReferenceTransform.position)}, " +
            $"ReferenceLossyScale={FormatVector3(trajectoryReferenceTransform.lossyScale)}\n" +
            $"RoundTripMaxError: Tip={maximumTipRoundTripError:F6}, Root={maximumRootRoundTripError:F6}\n" +
            $"ReferenceChangeDuringBake: Position={maximumReferencePositionChange:F6}, " +
            $"Rotation={maximumReferenceRotationChange:F4}deg, " +
            $"Scale={maximumReferenceScaleChange:F6}\n" +
            $"First: {firstSampleDiagnostic}\n" +
            $"Middle: {middleSampleDiagnostic}\n" +
            $"Last: {lastSampleDiagnostic}";

        Debug.Log(
            message,
            targetProfile);

        if (maximumTipRoundTripError >
                COORDINATE_ROUND_TRIP_WARNING_THRESHOLD ||
            maximumRootRoundTripError >
                COORDINATE_ROUND_TRIP_WARNING_THRESHOLD)
        {
            Debug.LogWarning(
                "[TrajectoryCoordinateValidation][Bake] " +
                "World→Local→Worldの往復誤差が大きいです。Transform階層や極端なScaleを確認してください。",
                targetProfile);
        }

        if (maximumReferencePositionChange >
                REFERENCE_POSITION_CHANGE_WARNING_THRESHOLD ||
            maximumReferenceRotationChange >
                REFERENCE_ROTATION_CHANGE_WARNING_DEGREES ||
            maximumReferenceScaleChange >
                REFERENCE_SCALE_CHANGE_WARNING_THRESHOLD)
        {
            Debug.LogWarning(
                "[TrajectoryCoordinateValidation][Bake] " +
                "Trajectory ReferenceがAnimationサンプリング中にPlayer Rootに対して変化しています。" +
                "現在のRuntime復元は1つの現在姿勢のReference Transformから全サンプルを復元するため、" +
                "軌跡基準には攻撃Animationで動かないTransformを使用してください。",
                targetProfile);
        }
    }

    private static string BuildBakeSampleDiagnostic(
        int sampleIndex,
        float sampleTime,
        Vector3 tipWorldPosition,
        Vector3 tipLocalPosition,
        Vector3 tipRoundTripWorldPosition,
        float tipRoundTripError,
        Vector3 rootWorldPosition,
        Vector3 rootLocalPosition,
        Vector3 rootRoundTripWorldPosition,
        float rootRoundTripError)
    {
        return
            $"Index={sampleIndex}, Time={sampleTime:F4}, " +
            $"TipWorld={FormatVector3(tipWorldPosition)}, " +
            $"TipLocal={FormatVector3(tipLocalPosition)}, " +
            $"TipRoundTripWorld={FormatVector3(tipRoundTripWorldPosition)}, " +
            $"TipError={tipRoundTripError:F6}, " +
            $"RootWorld={FormatVector3(rootWorldPosition)}, " +
            $"RootLocal={FormatVector3(rootLocalPosition)}, " +
            $"RootRoundTripWorld={FormatVector3(rootRoundTripWorldPosition)}, " +
            $"RootError={rootRoundTripError:F6}";
    }

    private static string FormatVector3(
        Vector3 value)
    {
        return
            $"({value.x:F4}, {value.y:F4}, {value.z:F4})";
    }

    private static bool TryResolveRelativeTransform(
        Transform root,
        string relativePath,
        string displayName,
        out Transform resolvedTransform,
        out string failureReason)
    {
        if (root == null)
        {
            resolvedTransform = null;
            failureReason =
                $"Bake用Player上の{displayName}を解決できませんでした。Player Rootが存在しません。";
            return false;
        }

        if (string.IsNullOrEmpty(relativePath))
        {
            resolvedTransform = root;
            failureReason = null;
            return true;
        }

        resolvedTransform =
            root.Find(
                relativePath);

        if (resolvedTransform != null)
        {
            failureReason = null;
            return true;
        }

        failureReason =
            $"Bake用Player上の{displayName}を解決できませんでした。\n" +
            $"Hierarchy Path: {relativePath}";
        return false;
    }

    private bool ValidateBakeSettings(
        out string validationMessage)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            validationMessage =
                "Play Mode中はBakeできません。";
            return false;
        }

        if (animationRoot == null)
        {
            validationMessage =
                "Animation Rootを設定してください。";
            return false;
        }

        if (playerRootTransform == null)
        {
            validationMessage =
                "Player Rootを設定してください。";
            return false;
        }

        if (trajectoryReferenceTransform == null)
        {
            validationMessage =
                "Trajectory Referenceを設定してください。";
            return false;
        }

        if (tipTransform == null ||
            rootTransform == null)
        {
            validationMessage =
                "Chainsaw Tip / Rootを両方設定してください。";
            return false;
        }

        if (tipTransform == rootTransform)
        {
            validationMessage =
                "Chainsaw TipとRootに同じTransformは指定できません。";
            return false;
        }

        if (animationClip == null)
        {
            validationMessage =
                "Animation Clipを設定してください。";
            return false;
        }

        if (attackData == null)
        {
            validationMessage =
                "Attack Dataを設定してください。";
            return false;
        }

        if (animationClip.length <= 0.0f)
        {
            validationMessage =
                "Animation Clipの長さが0です。";
            return false;
        }

        if (!IsSameOrChildOf(
                animationRoot.transform,
                tipTransform) ||
            !IsSameOrChildOf(
                animationRoot.transform,
                rootTransform))
        {
            validationMessage =
                "Chainsaw Tip / RootはAnimation Root自身、またはその子階層に配置してください。";
            return false;
        }

        if (!IsSameOrChildOf(
                playerRootTransform,
                animationRoot.transform))
        {
            validationMessage =
                "Animation RootはPlayer Root自身、またはその子階層に配置してください。";
            return false;
        }

        if (!IsSameOrChildOf(
                playerRootTransform,
                trajectoryReferenceTransform))
        {
            validationMessage =
                "Trajectory ReferenceはPlayer Root自身、またはその子階層に配置してください。";
            return false;
        }

        validationMessage = null;
        return true;
    }

    private static int CalculateSampleCount(
        float clipLength,
        float sampleRate)
    {
        int intervalCount =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    clipLength *
                    Mathf.Max(
                        MINIMUM_SAMPLE_RATE,
                        sampleRate)));

        // 開始時刻と終了時刻を必ず含めるため、区間数より1つ多くする。
        return intervalCount + 1;
    }

    private static bool IsSameOrChildOf(
        Transform expectedParent,
        Transform target)
    {
        return target == expectedParent ||
               target.IsChildOf(expectedParent);
    }

    private static bool TryBuildRelativePath(
        Transform root,
        Transform target,
        out string relativePath)
    {
        if (root == null || target == null)
        {
            relativePath = null;
            return false;
        }

        if (root == target)
        {
            relativePath = string.Empty;
            return true;
        }

        if (!target.IsChildOf(root))
        {
            relativePath = null;
            return false;
        }

        var pathParts =
            new List<string>();

        Transform current = target;

        while (current != null &&
               current != root)
        {
            pathParts.Add(
                current.name);

            current =
                current.parent;
        }

        pathParts.Reverse();

        relativePath =
            string.Join(
                "/",
                pathParts);

        return true;
    }

    private void DrawTrajectoryInSceneView(
        SceneView sceneView)
    {
        if (!showTrajectory ||
            targetProfile == null ||
            trajectoryReferenceTransform == null ||
            !targetProfile.HasValidTrajectory)
        {
            return;
        }

        IReadOnlyList<ChainsawBladeTrajectorySample> samples =
            targetProfile.Samples;

        Vector3 previousTip =
            trajectoryReferenceTransform.TransformPoint(
                samples[0].TipPosition);

        Vector3 previousRoot =
            trajectoryReferenceTransform.TransformPoint(
                samples[0].RootPosition);

        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 currentTip =
                trajectoryReferenceTransform.TransformPoint(
                    samples[i].TipPosition);

            Vector3 currentRoot =
                trajectoryReferenceTransform.TransformPoint(
                    samples[i].RootPosition);

            if (i > 0)
            {
                Handles.color = Color.cyan;
                Handles.DrawLine(
                    previousTip,
                    currentTip);

                Handles.color = Color.yellow;
                Handles.DrawLine(
                    previousRoot,
                    currentRoot);
            }

            if (showBladeSegments)
            {
                Handles.color =
                    new Color(
                        1.0f,
                        1.0f,
                        1.0f,
                        0.35f);

                Handles.DrawLine(
                    currentRoot,
                    currentTip);
            }

            previousTip = currentTip;
            previousRoot = currentRoot;
        }
    }
}
