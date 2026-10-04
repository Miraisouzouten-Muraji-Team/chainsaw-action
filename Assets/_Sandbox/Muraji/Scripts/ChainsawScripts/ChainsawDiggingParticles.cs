using UnityEngine;

[DefaultExecutionOrder(100)]
public class ChainsawDiggingParticles : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private ChainsawDigging chainsawDigging;
    [SerializeField] private PlayerController playerController;

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

    private void Awake()
    {
        if (chainsawDigging == null)
        {
            chainsawDigging =
                GetComponentInParent<ChainsawDigging>();
        }

        if (playerController == null)
        {
            playerController =
                GetComponentInParent<PlayerController>();
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

        // 初めて必要になった時点で生成する。
        // 以後は同じインスタンスを再利用する。
        if (particleInstance == null)
        {
            CreateParticles(position, rotation);
        }

        particleInstance.transform.SetPositionAndRotation(
            position,
            rotation);

        if (!isEmitting)
        {
            particleInstance.Play(false);
            isEmitting = true;
        }
    }

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

        particleInstance.Stop(
            false,
            ParticleSystemStopBehavior.StopEmittingAndClear);

        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = false;

        isEmitting = false;
    }

    private void StopEmission()
    {
        if (particleInstance != null && isEmitting)
        {
            // 新規発生だけを停止する。
            // 残った粒子は寿命まで表示する。
            particleInstance.Stop(
                false,
                ParticleSystemStopBehavior.StopEmitting);
        }

        isEmitting = false;
    }

    private void OnDisable()
    {
        if (particleInstance != null)
        {
            particleInstance.Stop(
                false,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        isEmitting = false;
    }

    private void OnDestroy()
    {
        // Playerの子ではないため、
        // Playerの破棄時に明示的に片付ける。
        if (particleInstance != null)
        {
            Destroy(particleInstance.gameObject);
        }
    }
}
