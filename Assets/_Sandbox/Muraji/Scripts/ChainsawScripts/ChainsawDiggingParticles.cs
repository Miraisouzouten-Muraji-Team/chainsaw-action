using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class ChainsawDiggingParticles : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private SensorChainsawDigging chainsawDigging;
    [SerializeField] private SensorPlayerController playerController;

    [Tooltip("Project内のParticle SystemのPrefabを設定")]
    [SerializeField] private ParticleSystem particlePrefab;

    [Tooltip("刃に置いた発生位置用の空オブジェクト")]
    [SerializeField] private Transform emissionPoint;

    [Header("床エフェクトの接触判定")]
    [Tooltip("地面との接触を確認する、チェーンソーの刃に配置した空オブジェクトです。未設定の場合は発生位置用の空オブジェクトを使用します。Colliderの追加は不要です。")]
    [SerializeField] private Transform groundContactPoint;

    [Tooltip("空オブジェクトを中心に地面との接触を調べる半径です。大きくすると地面に届く前に再生されやすくなります。")]
    [SerializeField, Min(0.001f)] private float groundContactRadius = 0.03f;

    [Tooltip("接触対象の地面レイヤーを指定してください。Nothingの場合は食い込み判定で使用している地形レイヤーを使用します。敵のレイヤーは含めないでください。")]
    [SerializeField] private LayerMask groundContactLayers;

    // 毎回の接触判定で配列を生成しないよう、結果を再利用する。
    private Collider[] groundContactBuffer = new Collider[16];

    [Header("右向き時の噴出角度。左向き時は左右反転")]
    [Tooltip("0=右、90=上、180=左、270=下")]
    [SerializeField, Range(0f, 360f)]
    private float floorAngle = 135f;

    [SerializeField, Range(0f, 360f)]
    private float wallAngle = 180f;

    [SerializeField, Range(0f, 360f)]
    private float ceilingAngle = 225f;

    [SerializeField, Range(0f, 360f)]
    private float enemyAngle = 160f;

    private ParticleSystem particleInstance;
    private bool isEmitting;

    // 放出終了後も、生存中の粒子が自然に消えるまで生成物を保持する。
    private readonly List<ParticleSystem> finishingParticles = new List<ParticleSystem>();

    // 必要な参照を取得し、設定不足なら処理を停止する。
    private void Awake()
    {
        if (chainsawDigging == null)
        {
            chainsawDigging =
                GetComponentInParent<SensorChainsawDigging>();
        }

        if (playerController == null)
        {
            playerController =
                GetComponentInParent<SensorPlayerController>();
        }

        if (chainsawDigging == null ||
            playerController == null ||
            particlePrefab == null ||
            emissionPoint == null)
        {
            Debug.LogError(
                "食い込みパーティクルの参照・Prefab・発生位置を設定してください。",
                this);

            enabled = false;
        }
    }

    // 食い込み中に生成・追従・再生し、放出を終了した粒子は寿命が尽きてから破棄する。
    private void FixedUpdate()
    {
        // 食い込みが終了していても、放出済み粒子の後片付けは継続する。
        CleanupFinishedParticles();

        if (chainsawDigging == null ||
            playerController == null ||
            particlePrefab == null ||
            emissionPoint == null ||
            !chainsawDigging.isActiveAndEnabled ||
            !playerController.isActiveAndEnabled ||
            !chainsawDigging.IsDigging)
        {
            StopEmission();
            return;
        }

        float angle;

        switch (chainsawDigging.Surface)
        {
            case ChainsawSurface.Floor:
                // 食い込み状態だけでは再生せず、刃が地面に届くまで待つ。
                if (!IsGroundContact())
                {
                    StopEmission();
                    return;
                }

                angle = floorAngle;
                break;

            case ChainsawSurface.Wall:
                angle = wallAngle;
                break;

            case ChainsawSurface.Ceiling:
                angle = ceilingAngle;
                break;

            case ChainsawSurface.Enemy:
                angle = enemyAngle;
                break;

            default:
                StopEmission();
                return;
        }

        float facingDirection =
            playerController.FacingDirection < 0f ? -1f : 1f;

        float radians = angle * Mathf.Deg2Rad;

        Vector3 direction = new Vector3(
            Mathf.Cos(radians) * facingDirection,
            Mathf.Sin(radians),
            0f);

        Quaternion rotation = Quaternion.FromToRotation(
            Vector3.forward,
            direction);

        Vector3 position = emissionPoint.position;

        // 食い込み開始ごとに生成する。継続中だけ同じインスタンスを使う。
        if (particleInstance == null)
        {
            CreateParticles(position, rotation);
        }

        particleInstance.transform.SetPositionAndRotation(
            position,
            rotation);

        if (!isEmitting)
        {
            particleInstance.Play(true);
            isEmitting = true;
        }
    }

    // 刃の空オブジェクト付近に、プレイヤー自身以外の地面Colliderがあるかを調べる。
    private bool IsGroundContact()
    {
        Transform contactPoint = groundContactPoint != null
            ? groundContactPoint
            : emissionPoint;

        if (contactPoint == null || !contactPoint.gameObject.activeInHierarchy)
        {
            return false;
        }

        int layerMask = groundContactLayers.value != 0
            ? groundContactLayers.value
            : chainsawDigging.TerrainLayerMask;

        if (layerMask == 0)
        {
            return false;
        }

        int count;

        // 結果が満杯の場合は拡張し、自分のColliderで地面の検出が欠けるのを防ぐ。
        while (true)
        {
            count = Physics.OverlapSphereNonAlloc(
                contactPoint.position,
                Mathf.Max(0.001f, groundContactRadius),
                groundContactBuffer,
                layerMask,
                QueryTriggerInteraction.Ignore);

            if (count < groundContactBuffer.Length)
            {
                break;
            }

            System.Array.Resize(ref groundContactBuffer, groundContactBuffer.Length * 2);
        }

        Transform playerRoot = playerController.transform;
        bool hasGroundContact = false;

        for (int i = 0; i < count; i++)
        {
            Collider target = groundContactBuffer[i];
            groundContactBuffer[i] = null;

            if (target == null ||
                target.transform == playerRoot ||
                target.transform.IsChildOf(playerRoot))
            {
                continue;
            }

            hasGroundContact = true;
        }

        return hasGroundContact;
    }

    // 選択中に接触判定の位置と大きさを表示し、刃先と地面の距離を調整しやすくする。
    private void OnDrawGizmosSelected()
    {
        Transform contactPoint = groundContactPoint != null
            ? groundContactPoint
            : emissionPoint;

        if (contactPoint == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(
            contactPoint.position,
            Mathf.Max(0.001f, groundContactRadius));
    }

    // 食い込み専用のインスタンスを作り、ループ再生の準備をする。
    private void CreateParticles(
        Vector3 position,
        Quaternion rotation)
    {
        // 見た目の左右反転やスケールの影響を避けるため、
        // Playerの子にはせず生成する。
        particleInstance = Instantiate(
            particlePrefab,
            position,
            rotation);

        ParticleSystem.MainModule main = particleInstance.main;

        main.stopAction = ParticleSystemStopAction.None;

        // 子の停止アクションによる意図しない破棄を防ぎ、寿命はこのスクリプトで管理する。
        foreach (ParticleSystem particles in particleInstance.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule childMain = particles.main;
            childMain.stopAction = ParticleSystemStopAction.None;
        }

        particleInstance.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear);

        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = false;

        particleInstance.gameObject.SetActive(true);
        isEmitting = false;
    }

    // 新しい粒子の放出だけを止め、残っている粒子は寿命まで再生する。
    private void StopEmission()
    {
        isEmitting = false;
        if (particleInstance == null) return;

        ParticleSystem instance = particleInstance;
        particleInstance = null;

        // Clearや非表示化を行わず、子のParticleSystemも放出だけ停止する。
        // 追従対象から外すため、以降は終了した位置に残る。
        instance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        finishingParticles.Add(instance);
    }

    // 子を含む粒子がすべて消えた生成物だけを破棄する。
    private void CleanupFinishedParticles()
    {
        for (int i = finishingParticles.Count - 1; i >= 0; i--)
        {
            ParticleSystem instance = finishingParticles[i];

            if (instance != null && instance.IsAlive(true))
            {
                continue;
            }

            if (instance != null)
            {
                Destroy(instance.gameObject);
            }

            finishingParticles.RemoveAt(i);
        }
    }

    // プレイヤー無効化・破棄時は更新できなくなるため、すべての生成物を即座に片付ける。
    private void ClearAllParticles()
    {
        StopEmission();

        foreach (ParticleSystem instance in finishingParticles)
        {
            if (instance == null) continue;

            instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            instance.gameObject.SetActive(false);
            Destroy(instance.gameObject);
        }

        finishingParticles.Clear();
    }

    // 無効化やリスポーン中に、親なしで生成したパーティクルを残さない。
    private void OnDisable()
    {
        ClearAllParticles();
    }

    // オブジェクト破棄時にも片付ける。既に停止済みなら何もしない。
    private void OnDestroy()
    {
        ClearAllParticles();
    }
}
