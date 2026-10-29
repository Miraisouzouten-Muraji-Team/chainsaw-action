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

    // 食い込み中だけ生成・追従・再生し、条件が終了したら生成物を破棄する。
    private void FixedUpdate()
    {
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

    // 子を含む全粒子を即座に消し、生成物を破棄して次回の新規生成へ備える。
    private void StopEmission()
    {
        isEmitting = false;
        if (particleInstance == null) return;

        ParticleSystem instance = particleInstance;
        particleInstance = null;
        instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        // Destroyはフレーム末尾なので、先に非表示にして残像を残さない。
        instance.gameObject.SetActive(false);
        Destroy(instance.gameObject);
    }

    // 無効化やリスポーン中にも、親なしで生成したパーティクルを残さない。
    private void OnDisable()
    {
        StopEmission();
    }

    // オブジェクト破棄時にも片付ける。既に停止済みなら何もしない。
    private void OnDestroy()
    {
        StopEmission();
    }
}
