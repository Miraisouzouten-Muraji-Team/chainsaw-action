using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 通常チェンソー攻撃のAnimation ClipをEditor上で最後までサンプリングし、
/// ChainsawAttackTrajectoryProfileへ完成軌跡を書き込むEditorWindow。
/// </summary>
/// <remarks>
/// Runtime gameplayでの軌跡記録は担当しない。
/// Animation Root以下のTip / Rootを指定し、Bake時とRuntime時で同じ責務の
/// Trajectory Reference Transformを使用すること。
/// </remarks>
public sealed class ChainsawAttackTrajectoryBaker : EditorWindow
{
    private const int MIN_SAMPLE_COUNT = 2;
    private const int DEFAULT_SAMPLE_COUNT = 31;

    private GameObject animationRoot;
    private Transform trajectoryReferenceTransform;
    private Transform tipTransform;
    private Transform rootTransform;
    private AnimationClip animationClip;
    private ChainsawAttackTrajectoryProfile outputProfile;
    private int sampleCount = DEFAULT_SAMPLE_COUNT;

    [MenuItem("Tools/ChainBlade/Chainsaw Attack Trajectory Baker")]
    private static void OpenWindow()
    {
        GetWindow<ChainsawAttackTrajectoryBaker>(
            "Chainsaw Trajectory Baker");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(
            "通常攻撃の完成軌跡を事前生成",
            EditorStyles.boldLabel);

        EditorGUILayout.Space();

        animationRoot =
            (GameObject)EditorGUILayout.ObjectField(
                "Animation Root",
                animationRoot,
                typeof(GameObject),
                true);

        trajectoryReferenceTransform =
            (Transform)EditorGUILayout.ObjectField(
                "Trajectory Reference",
                trajectoryReferenceTransform,
                typeof(Transform),
                true);

        tipTransform =
            (Transform)EditorGUILayout.ObjectField(
                "Blade Tip",
                tipTransform,
                typeof(Transform),
                true);

        rootTransform =
            (Transform)EditorGUILayout.ObjectField(
                "Blade Root",
                rootTransform,
                typeof(Transform),
                true);

        animationClip =
            (AnimationClip)EditorGUILayout.ObjectField(
                "Animation Clip",
                animationClip,
                typeof(AnimationClip),
                false);

        outputProfile =
            (ChainsawAttackTrajectoryProfile)EditorGUILayout.ObjectField(
                "Output Profile",
                outputProfile,
                typeof(ChainsawAttackTrajectoryProfile),
                false);

        sampleCount = EditorGUILayout.IntField(
            "Sample Count",
            sampleCount);

        sampleCount = Mathf.Max(
            MIN_SAMPLE_COUNT,
            sampleCount);

        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "Animation Root / Trajectory Reference / Tip / Rootは同じ階層内から指定してください。"
            + " Bakeは一時CloneへAnimationClipを適用して行うため、Scene上の元Objectの姿勢は変更しません。",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(!CanBake()))
        {
            if (GUILayout.Button("Bake Trajectory Profile"))
            {
                Bake();
            }
        }
    }

    private bool CanBake()
    {
        return animationRoot != null &&
               trajectoryReferenceTransform != null &&
               tipTransform != null &&
               rootTransform != null &&
               animationClip != null &&
               outputProfile != null &&
               sampleCount >= MIN_SAMPLE_COUNT;
    }

    private void Bake()
    {
        if (!TryValidateReferences(
                out string validationFailureReason))
        {
            Debug.LogError(
                $"{nameof(ChainsawAttackTrajectoryBaker)}: " +
                validationFailureReason);

            return;
        }

        string referencePath =
            AnimationUtility.CalculateTransformPath(
                trajectoryReferenceTransform,
                animationRoot.transform);

        string tipPath =
            AnimationUtility.CalculateTransformPath(
                tipTransform,
                animationRoot.transform);

        string rootPath =
            AnimationUtility.CalculateTransformPath(
                rootTransform,
                animationRoot.transform);

        GameObject previewRoot = null;

        try
        {
            previewRoot = UnityEngine.Object.Instantiate(animationRoot);
            previewRoot.name =
                $"{animationRoot.name}_TrajectoryBakePreview";

            previewRoot.hideFlags =
                HideFlags.HideAndDontSave;

            Transform previewReference =
                ResolveTransformByPath(
                    previewRoot.transform,
                    referencePath);

            Transform previewTip =
                ResolveTransformByPath(
                    previewRoot.transform,
                    tipPath);

            Transform previewBladeRoot =
                ResolveTransformByPath(
                    previewRoot.transform,
                    rootPath);

            if (previewReference == null ||
                previewTip == null ||
                previewBladeRoot == null)
            {
                Debug.LogError(
                    $"{nameof(ChainsawAttackTrajectoryBaker)}: " +
                    "一時Clone上でTrajectory Reference / Tip / Rootを解決できませんでした。",
                    animationRoot);

                return;
            }

            var normalizedTimes =
                new List<float>(sampleCount);

            var tipLocalPositions =
                new List<Vector3>(sampleCount);

            var rootLocalPositions =
                new List<Vector3>(sampleCount);

            for (int i = 0; i < sampleCount; i++)
            {
                float normalizedTime =
                    (float)i / (sampleCount - 1);

                float sampleTime =
                    animationClip.length * normalizedTime;

                animationClip.SampleAnimation(
                    previewRoot,
                    sampleTime);

                normalizedTimes.Add(
                    normalizedTime);

                tipLocalPositions.Add(
                    previewReference.InverseTransformPoint(
                        previewTip.position));

                rootLocalPositions.Add(
                    previewReference.InverseTransformPoint(
                        previewBladeRoot.position));
            }

            Undo.RecordObject(
                outputProfile,
                "Bake Chainsaw Attack Trajectory");

            outputProfile.SetBakedSamples(
                animationClip,
                normalizedTimes,
                tipLocalPositions,
                rootLocalPositions);

            EditorUtility.SetDirty(
                outputProfile);

            AssetDatabase.SaveAssets();

            Debug.Log(
                $"{nameof(ChainsawAttackTrajectoryBaker)}: " +
                $"{animationClip.name} を {sampleCount} サンプルでBakeしました。 " +
                $"Output: {AssetDatabase.GetAssetPath(outputProfile)}",
                outputProfile);
        }
        finally
        {
            if (previewRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(previewRoot);
            }
        }
    }

    private bool TryValidateReferences(
        out string failureReason)
    {
        if (!CanBake())
        {
            failureReason =
                "Bakeに必要な参照またはSample Countが不足しています。";

            return false;
        }

        if (!IsSameOrChildOf(
                trajectoryReferenceTransform,
                animationRoot.transform))
        {
            failureReason =
                "Trajectory ReferenceはAnimation Root自身または子階層から指定してください。";

            return false;
        }

        if (!IsSameOrChildOf(
                tipTransform,
                animationRoot.transform) ||
            !IsSameOrChildOf(
                rootTransform,
                animationRoot.transform))
        {
            failureReason =
                "Blade Tip / Blade RootはAnimation Rootの子階層から指定してください。";

            return false;
        }

        if (tipTransform == rootTransform)
        {
            failureReason =
                "Blade TipとBlade Rootに同じTransformは指定できません。";

            return false;
        }

        if (animationClip.length <= 0.0f)
        {
            failureReason =
                "Animation Clipの長さが0以下です。";

            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool IsSameOrChildOf(
        Transform candidate,
        Transform expectedRoot)
    {
        return candidate == expectedRoot ||
               candidate.IsChildOf(expectedRoot);
    }

    private static Transform ResolveTransformByPath(
        Transform root,
        string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return root;
        }

        return root.Find(path);
    }
}
