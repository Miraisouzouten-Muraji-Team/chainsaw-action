using System.Collections.Generic;
using MeshFracture;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Enemy死亡時にFragmentを生成して吹き飛ばすMeshCut演出を担当する。
/// </summary>
/// <remarks>
/// MeshFilterは初期化時に事前分割してCacheを共有する。
/// SkinnedMeshRendererは死亡時の現在姿勢をBakeしてから分割し、
/// バインドポーズではなく死亡した瞬間の形状をFragmentへ反映する。
/// 死亡判定、攻撃種別の判定、State遷移は担当しない。
/// </remarks>
public sealed class EnemyMeshCutDeathEffect : MonoBehaviour
{
    private const int MIN_FRAGMENT_COUNT = 2;
    private const float MIN_DURATION = 0.01f;

    [Header("発動対象")]
    [Tooltip("このMeshCut死亡演出を発動させるAttackData。現在はDiggingAttackDataを設定する。")]
    [SerializeField] private AttackData triggerAttackData;

    [Header("分割対象")]
    [Tooltip("SkinnedMeshRendererを使用するEnemyの分割対象。Target Mesh Filterと同時には設定しない。")]
    [SerializeField] private SkinnedMeshRenderer targetSkinnedMeshRenderer;

    [Tooltip("MeshFilter + MeshRendererを使用するEnemyの分割対象。Target Skinned Mesh Rendererと同時には設定しない。")]
    [SerializeField] private MeshFilter targetMeshFilter;

    [Tooltip("分割によって生成される切断面へ設定するMaterial。未設定時は外側と同じMaterialを使用する。")]
    [SerializeField] private Material cutSurfaceMaterial;

    [Header("分割")]
    [Tooltip("生成を要求するFragment数。形状によって実際の生成数が少なくなる場合がある。")]
    [Min(MIN_FRAGMENT_COUNT)]
    [SerializeField] private int fragmentCount = 8;

    [Header("飛散")]
    [Tooltip("各Fragmentへ与える初速の基準値。")]
    [Min(0.0f)]
    [SerializeField] private float explosionForce = 18.0f;

    [Tooltip("Fragmentが消滅するまでの基準時間。")]
    [Min(MIN_DURATION)]
    [SerializeField] private float fragmentLifetime = 5.0f;

    [Tooltip("Fragment消滅時のフェード時間。")]
    [Min(MIN_DURATION)]
    [SerializeField] private float dissolveDuration = 1.0f;

    [Tooltip("全Fragmentが停止した場合、Lifetimeを待たず停止後のフェード開始を許可する。")]
    [SerializeField] private bool dissolveAfterSettle = true;

    [Tooltip("全Fragmentが停止してからフェードを開始するまでの待機時間。")]
    [Min(0.0f)]
    [SerializeField] private float settleHoldDuration = 0.5f;

    [Header("Fragment物理")]
    [Tooltip("有効時は各FragmentへRigidbodyとColliderを付与し、Physics.gravityとステージColliderを使用する。")]
    [SerializeField] private bool useUnityPhysics = true;

    [Range(0.0f, 1.0f)]
    [SerializeField] private float bounciness = 0.2f;

    [Range(0.0f, 1.0f)]
    [SerializeField] private float friction = 0.5f;

    [Min(0.01f)]
    [SerializeField] private float fragmentMass = 0.5f;

    [Header("Trail")]
    [SerializeField] private bool enableTrails;

    [Min(0.0f)]
    [SerializeField] private float trailWidth = 0.12f;

    [SerializeField] private Gradient trailGradient;

    private Renderer sourceRenderer;
    private Transform sourceTransform;
    private Mesh sourceMesh;
    private int bakeKey;
    private bool hasRequestedPreBake;

    private GameObject activeBurstObject;
    private Material runtimeExteriorMaterial;
    private Material runtimeInteriorMaterial;
    private Mesh[] runtimeGeneratedFragmentMeshes;

    /// <summary>
    /// この死亡演出を発動させる攻撃データ。
    /// 攻撃種別の判定自体は呼び出し側が担当する。
    /// </summary>
    public AttackData TriggerAttackData => triggerAttackData;

    public bool IsPlaying => activeBurstObject != null;

    private void Awake()
    {
        RequestPreBake();
    }

    private void Update()
    {
        // FractureBurstは自身をDestroyするため、終了後にこのComponent側でRuntime Materialを破棄する。
        if (activeBurstObject == null)
        {
            ReleaseRuntimeMaterials();
            ReleaseRuntimeFragmentMeshes();
        }
    }

    private void OnDestroy()
    {
        StopMeshCut();
    }

    /// <summary>
    /// 事前Bake済みFragmentを使ってMeshCut死亡演出を開始する。
    /// </summary>
    /// <returns>
    /// 演出開始まで成功した場合はtrue。Cache未完了や設定不足の場合はfalse。
    /// </returns>
    public bool TryBeginMeshCut()
    {
        if (!isActiveAndEnabled || activeBurstObject != null)
        {
            return false;
        }

        if (!TryResolveSource(out string failureReason))
        {
            LogFailure(failureReason);
            return false;
        }

        Material exteriorSourceMaterial =
            sourceRenderer.sharedMaterial;

        if (exteriorSourceMaterial == null)
        {
            LogFailure(
                "分割対象RendererにMaterialが設定されていません。");

            return false;
        }

        ReleaseRuntimeFragmentMeshes();

        FragmentResult[] fragments;
        Quaternion meshRotation;
        Vector3 meshCenterWorld;
        Mesh[] preBakedColliderMeshes;

        if (targetSkinnedMeshRenderer != null)
        {
            if (!TryBuildSkinnedPoseFragments(
                    out fragments,
                    out meshRotation,
                    out meshCenterWorld,
                    out failureReason))
            {
                LogFailure(failureReason);
                ReleaseRuntimeFragmentMeshes();
                return false;
            }

            // SkinnedMeshは死亡時姿勢からRuntime生成しているため、
            // bind pose由来の事前Collider Cacheは使用できない。
            // FractureBurst側でFragment生成を分散しながらColliderを構築する。
            preBakedColliderMeshes = null;
        }
        else
        {
            if (!hasRequestedPreBake)
            {
                RequestPreBake();
            }

            if (!FragmentCache.TryGet(
                    bakeKey,
                    out FragmentCache.CachedData cachedData))
            {
                LogFailure(
                    "Fragmentの事前Bakeが完了していないため、今回は既存死亡演出へフォールバックします。");

                return false;
            }

            if (!TryBuildWorldFragments(
                    cachedData,
                    out fragments,
                    out meshRotation,
                    out meshCenterWorld,
                    out failureReason))
            {
                LogFailure(failureReason);
                return false;
            }

            preBakedColliderMeshes =
                cachedData.ColliderMeshes;
        }

        ReleaseRuntimeMaterials();

        runtimeExteriorMaterial =
            CreateRuntimeFractureMaterial(
                exteriorSourceMaterial);

        runtimeInteriorMaterial =
            CreateRuntimeFractureMaterial(
                cutSurfaceMaterial != null
                    ? cutSurfaceMaterial
                    : exteriorSourceMaterial);

        var burstObject =
            new GameObject(
                $"{gameObject.name}_MeshCutBurst");

        burstObject.transform.position =
            meshCenterWorld;

        FractureBurst fractureBurst =
            burstObject.AddComponent<FractureBurst>();

        ConfigureBurst(
            fractureBurst,
            preBakedColliderMeshes);

        try
        {
            fractureBurst.Initialize(
                fragments,
                runtimeExteriorMaterial,
                runtimeInteriorMaterial,
                Mathf.Max(
                    0.0f,
                    explosionForce),
                enableTrails
                    ? Mathf.Max(
                        0.0f,
                        trailWidth)
                    : 0.0f,
                enableTrails
                    ? trailGradient
                    : null,
                meshRotation);
        }
        catch (System.Exception exception)
        {
            Destroy(burstObject);
            ReleaseRuntimeMaterials();
            ReleaseRuntimeFragmentMeshes();

            Debug.LogException(
                exception,
                this);

            return false;
        }

        // Fragment生成要求に成功した後だけ元Rendererを隠す。
        // 失敗時はStandardEnemyDeathStrategyが既存2分割へフォールバックできる。
        sourceRenderer.enabled = false;

        activeBurstObject =
            burstObject;

        return true;
    }

    /// <summary>
    /// 実行中のMeshCut死亡演出を停止する。
    /// </summary>
    public void StopMeshCut()
    {
        if (activeBurstObject != null)
        {
            Destroy(activeBurstObject);
            activeBurstObject = null;
        }

        ReleaseRuntimeMaterials();
        ReleaseRuntimeFragmentMeshes();
    }

    private void ConfigureBurst(
        FractureBurst fractureBurst,
        Mesh[] preBakedColliderMeshes)
    {
        fractureBurst.Lifetime =
            Mathf.Max(
                MIN_DURATION,
                fragmentLifetime);

        fractureBurst.DissolveDuration =
            Mathf.Clamp(
                dissolveDuration,
                MIN_DURATION,
                fractureBurst.Lifetime);

        fractureBurst.DissolveAfterSettle =
            dissolveAfterSettle;

        fractureBurst.SettleHoldDuration =
            Mathf.Max(
                0.0f,
                settleHoldDuration);

        fractureBurst.UseUnityPhysics =
            useUnityPhysics;

        fractureBurst.Bounciness =
            bounciness;

        fractureBurst.Friction =
            friction;

        fractureBurst.FragmentMass =
            Mathf.Max(
                0.01f,
                fragmentMass);

        fractureBurst.EnableTrails =
            enableTrails;

        fractureBurst.PreBakedColliderMeshes =
            preBakedColliderMeshes;
    }

    /// <summary>
    /// Gameplay中の死亡フレームでVoronoi計算を行わないため、
    /// 初期化時に事前Bakeを要求する。
    /// </summary>
    private void RequestPreBake()
    {
        if (!TryResolveSource(
                out string failureReason))
        {
            LogFailure(failureReason);
            return;
        }

        // SkinnedMeshは死亡時の姿勢が確定するまで形状を固定できない。
        // sharedMeshのbind poseを事前分割すると死亡姿勢と一致しないため、
        // SkinnedMeshだけはTryBeginMeshCut内でRuntime Bakeする。
        if (targetSkinnedMeshRenderer != null)
        {
            hasRequestedPreBake = false;
            return;
        }

        if (!sourceMesh.isReadable)
        {
            LogFailure(
                "分割対象MeshのRead/Writeが無効です。" +
                "Model Import SettingsでRead/Writeを有効にしてください。");

            return;
        }

        int safeFragmentCount =
            Mathf.Max(
                MIN_FRAGMENT_COUNT,
                fragmentCount);

        bakeKey =
            CalculateBakeKey(
                sourceMesh,
                safeFragmentCount,
                sourceTransform.lossyScale);

        FragmentCache.RequestPreBake(
            bakeKey,
            sourceRenderer.gameObject,
            safeFragmentCount);

        hasRequestedPreBake = true;
    }

    private bool TryResolveSource(
        out string failureReason)
    {
        bool hasSkinnedMeshTarget =
            targetSkinnedMeshRenderer != null;

        bool hasMeshFilterTarget =
            targetMeshFilter != null;

        if (!hasSkinnedMeshTarget &&
            !hasMeshFilterTarget)
        {
            failureReason =
                "分割対象が設定されていません。" +
                "Target Skinned Mesh Rendererまたは" +
                "Target Mesh Filterを設定してください。";

            return false;
        }

        if (hasSkinnedMeshTarget &&
            hasMeshFilterTarget)
        {
            failureReason =
                "Target Skinned Mesh Rendererと" +
                "Target Mesh Filterはどちらか一方だけ設定してください。";

            return false;
        }

        if (hasSkinnedMeshTarget)
        {
            if (targetSkinnedMeshRenderer.sharedMesh == null)
            {
                failureReason =
                    "Target Skinned Mesh RendererにsharedMeshがありません。";

                return false;
            }

            sourceRenderer =
                targetSkinnedMeshRenderer;

            sourceTransform =
                targetSkinnedMeshRenderer.transform;

            sourceMesh =
                targetSkinnedMeshRenderer.sharedMesh;

            failureReason = null;
            return true;
        }

        if (targetMeshFilter.sharedMesh == null)
        {
            failureReason =
                "Target Mesh FilterにsharedMeshがありません。";

            return false;
        }

        MeshRenderer meshRenderer =
            targetMeshFilter.GetComponent<MeshRenderer>();

        if (meshRenderer == null)
        {
            failureReason =
                "Target Mesh Filterと同じGameObjectに" +
                "MeshRendererがありません。";

            return false;
        }

        sourceRenderer =
            meshRenderer;

        sourceTransform =
            targetMeshFilter.transform;

        sourceMesh =
            targetMeshFilter.sharedMesh;

        failureReason = null;
        return true;
    }

    /// <summary>
    /// SkinnedMeshRendererの死亡時姿勢を静的MeshへBakeし、
    /// その姿勢を直接Voronoi分割する。
    /// </summary>
    /// <remarks>
    /// FragmentはFractureBurst終了後に破棄するRuntime MeshとしてこのComponentが所有する。
    /// useScale=trueでRenderer ScaleをBake側へ反映し、Fragment側ではPosition/Rotationだけを適用する。
    /// </remarks>
    private bool TryBuildSkinnedPoseFragments(
        out FragmentResult[] fragments,
        out Quaternion meshRotation,
        out Vector3 meshCenterWorld,
        out string failureReason)
    {
        fragments = null;
        meshRotation = Quaternion.identity;
        meshCenterWorld = Vector3.zero;

        if (targetSkinnedMeshRenderer == null)
        {
            failureReason =
                "SkinnedMeshRendererが設定されていません。";

            return false;
        }

        var bakedMesh = new Mesh
        {
            name =
                $"{targetSkinnedMeshRenderer.name}_MeshCutDeathBake"
        };

        try
        {
            // FractureBurstのFragmentは元Renderer階層の外へ生成されるため、
            // 元TransformのScaleを継承しない。ここではScaleをBake Meshへ反映し、
            // Runtime Fragmentに同じScaleを二重適用しない。
            targetSkinnedMeshRenderer.BakeMesh(
                bakedMesh,
                true);

            bakedMesh.RecalculateBounds();

            if (bakedMesh.vertexCount == 0)
            {
                failureReason =
                    "死亡時姿勢をBakeしたMeshに頂点がありません。";

                DestroyRuntimeMesh(bakedMesh);
                return false;
            }

            int safeFragmentCount =
                Mathf.Max(
                    MIN_FRAGMENT_COUNT,
                    fragmentCount);

            FragmentResult[] localFragments =
                MeshFragmenter.Fragment(
                    bakedMesh,
                    safeFragmentCount,
                    bakedMesh.bounds.center);

            if (localFragments == null ||
                localFragments.Length == 0)
            {
                failureReason =
                    "死亡時姿勢のVoronoi分割結果が空です。";

                DestroyRuntimeMesh(bakedMesh);
                return false;
            }

            meshRotation =
                sourceTransform.rotation;

            meshCenterWorld =
                sourceTransform.position +
                meshRotation *
                bakedMesh.bounds.center;

            fragments =
                new FragmentResult[
                    localFragments.Length];

            var ownedMeshes =
                new HashSet<Mesh>();

            bool bakedMeshIsFragment = false;

            for (int i = 0;
                 i < localFragments.Length;
                 i++)
            {
                Mesh fragmentMesh =
                    localFragments[i].Mesh;

                if (fragmentMesh == null)
                {
                    failureReason =
                        "死亡時姿勢の分割結果にnull Meshが含まれています。";

                    foreach (Mesh ownedMesh in ownedMeshes)
                    {
                        DestroyRuntimeMesh(ownedMesh);
                    }

                    if (!bakedMeshIsFragment)
                    {
                        DestroyRuntimeMesh(bakedMesh);
                    }

                    fragments = null;
                    return false;
                }

                if (fragmentMesh == bakedMesh)
                {
                    bakedMeshIsFragment = true;
                }

                ownedMeshes.Add(
                    fragmentMesh);

                fragments[i] =
                    new FragmentResult
                    {
                        Mesh =
                            fragmentMesh,

                        Centroid =
                            sourceTransform.position +
                            meshRotation *
                            localFragments[i].Centroid
                    };
            }

            if (!bakedMeshIsFragment)
            {
                DestroyRuntimeMesh(
                    bakedMesh);
            }

            runtimeGeneratedFragmentMeshes =
                new Mesh[ownedMeshes.Count];

            ownedMeshes.CopyTo(
                runtimeGeneratedFragmentMeshes);

            failureReason = null;
            return true;
        }
        catch (System.Exception exception)
        {
            DestroyRuntimeMesh(
                bakedMesh);

            failureReason =
                $"死亡時SkinnedMeshのBakeまたは分割中に例外が発生しました: {exception.Message}";

            return false;
        }
    }

    /// <summary>
    /// Cache内のMesh座標を死亡時点のRenderer Transformへ合わせ、
    /// 現在の向きでFragmentを配置する。
    /// </summary>
    private bool TryBuildWorldFragments(
        FragmentCache.CachedData cachedData,
        out FragmentResult[] fragments,
        out Quaternion meshRotation,
        out Vector3 meshCenterWorld,
        out string failureReason)
    {
        fragments = null;
        meshRotation = Quaternion.identity;
        meshCenterWorld = Vector3.zero;

        if (cachedData.Meshes == null ||
            cachedData.LocalCentroids == null ||
            cachedData.Meshes.Length == 0 ||
            cachedData.Meshes.Length !=
            cachedData.LocalCentroids.Length)
        {
            failureReason =
                "FragmentCacheのMeshまたはCentroid情報が不正です。";

            return false;
        }

        meshRotation =
            sourceTransform.rotation;

        meshCenterWorld =
            sourceTransform.position +
            meshRotation *
            cachedData.MeshCenter;

        fragments =
            new FragmentResult[
                cachedData.Meshes.Length];

        for (int i = 0;
             i < fragments.Length;
             i++)
        {
            Vector3 offsetFromMeshCenter =
                cachedData.LocalCentroids[i] -
                cachedData.MeshCenter;

            fragments[i] =
                new FragmentResult
                {
                    Mesh =
                        cachedData.Meshes[i],

                    Centroid =
                        meshCenterWorld +
                        meshRotation *
                        offsetFromMeshCenter
                };
        }

        failureReason = null;
        return true;
    }

    /// <summary>
    /// 同じMesh・分割数・ScaleのEnemy間で事前Bake結果を共有できるようにCache Keyを作る。
    /// </summary>
    private static int CalculateBakeKey(
        Mesh mesh,
        int count,
        Vector3 lossyScale)
    {
        unchecked
        {
            int hash = 17;

            hash =
                hash * 31 +
                mesh.GetInstanceID();

            hash =
                hash * 31 +
                count;

            hash =
                hash * 31 +
                lossyScale.GetHashCode();

            return hash;
        }
    }

    /// <summary>
    /// FractureBurstがFade中にMaterialを直接変更するため、
    /// 共有Material Assetではなく専用Instanceを渡す。
    /// </summary>
    private static Material CreateRuntimeFractureMaterial(
        Material sourceMaterial)
    {
        var material =
            new Material(
                sourceMaterial);

        if (material.HasProperty(
                "_BaseColor"))
        {
            Color color =
                material.GetColor(
                    "_BaseColor");

            color.a = 1.0f;

            material.SetColor(
                "_BaseColor",
                color);
        }

        SetFloatIfPresent(
            material,
            "_Surface",
            1.0f);

        SetFloatIfPresent(
            material,
            "_Blend",
            0.0f);

        SetFloatIfPresent(
            material,
            "_AlphaClip",
            0.0f);

        SetFloatIfPresent(
            material,
            "_Cull",
            0.0f);

        SetFloatIfPresent(
            material,
            "_SrcBlend",
            (float)BlendMode.SrcAlpha);

        SetFloatIfPresent(
            material,
            "_DstBlend",
            (float)BlendMode.OneMinusSrcAlpha);

        SetFloatIfPresent(
            material,
            "_ZWrite",
            1.0f);

        material.EnableKeyword(
            "_SURFACE_TYPE_TRANSPARENT");

        material.DisableKeyword(
            "_ALPHATEST_ON");

        material.DisableKeyword(
            "_ALPHAPREMULTIPLY_ON");

        material.SetOverrideTag(
            "RenderType",
            "Transparent");

        material.renderQueue =
            (int)RenderQueue.Transparent;

        return material;
    }

    private static void SetFloatIfPresent(
        Material material,
        string propertyName,
        float value)
    {
        if (material.HasProperty(
                propertyName))
        {
            material.SetFloat(
                propertyName,
                value);
        }
    }

    private void ReleaseRuntimeMaterials()
    {
        if (runtimeExteriorMaterial != null)
        {
            Destroy(
                runtimeExteriorMaterial);

            runtimeExteriorMaterial = null;
        }

        if (runtimeInteriorMaterial != null)
        {
            Destroy(
                runtimeInteriorMaterial);

            runtimeInteriorMaterial = null;
        }
    }

    private void ReleaseRuntimeFragmentMeshes()
    {
        if (runtimeGeneratedFragmentMeshes == null)
        {
            return;
        }

        foreach (Mesh runtimeMesh in
                 runtimeGeneratedFragmentMeshes)
        {
            DestroyRuntimeMesh(
                runtimeMesh);
        }

        runtimeGeneratedFragmentMeshes =
            null;
    }

    private static void DestroyRuntimeMesh(
        Mesh mesh)
    {
        if (mesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(mesh);
        }
        else
        {
            DestroyImmediate(mesh);
        }
    }

    private void LogFailure(
        string failureReason)
    {
        Debug.LogWarning(
            $"{nameof(EnemyMeshCutDeathEffect)}: " +
            failureReason,
            this);
    }
}
