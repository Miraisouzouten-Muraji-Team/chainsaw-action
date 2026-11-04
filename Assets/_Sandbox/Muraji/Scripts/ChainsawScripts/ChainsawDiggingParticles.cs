using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class ChainsawDiggingParticles : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private SensorChainsawDigging chainsawDigging;
    [SerializeField] private SensorPlayerController playerController;

    [Tooltip("第1段階のParticle SystemのPrefabです。従来のPrefab設定をそのまま引き継ぎます。")]
    [SerializeField] private ParticleSystem particlePrefab;

    [Tooltip("敵への食い込み中に使用する専用のParticle SystemのPrefabです。未設定なら通常のアクセル段階に従います。")]
    [SerializeField] private ParticleSystem enemyParticlePrefab;

    [Header("アクセルによる3段階の強さ")]
    [Tooltip("アクセル値を参照するコンポーネントです。未設定ならプレイヤーの子から非表示のものも含めて取得します。")]
    [SerializeField] private ChainsawAccelerator accelerator;
    [Tooltip("第2段階のPrefabです。未設定の場合は第1段階を使用します。")]
    [SerializeField] private ParticleSystem mediumParticlePrefab;
    [Tooltip("第3段階のPrefabです。未設定の場合は第2段階、続いて第1段階を使用します。")]
    [SerializeField] private ParticleSystem strongParticlePrefab;
    [Tooltip("このアクセル値以上で第2段階になります。割合ではなくアクセルの数値で指定します。")]
    [SerializeField, Min(0f)] private float mediumAcceleratorThreshold = 20f;
    [Tooltip("このアクセル値以上で第3段階になります。第2段階のしきい値より大きい値を指定してください。")]
    [SerializeField, Min(0f)] private float strongAcceleratorThreshold = 35f;

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
    private ParticleSystem activeParticlePrefab;

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

        if (accelerator == null && playerController != null)
            accelerator = playerController.GetComponentInChildren<ChainsawAccelerator>(true);

        if (accelerator == null)
            Debug.LogWarning("食い込みエフェクト：アクセル参照がないため第1段階のみ使用します。", this);

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

    // 有効化時に停止キーフレームの通知を登録し、途中から有効化した場合も現在の状態へ合わせる。
    private void OnEnable()
    {
        if (chainsawDigging != null)
            chainsawDigging.DiggingParticleFrameChanged += UpdateEmission;
        UpdateEmission();
    }

    // 固定キーフレーム中の追従と、終了後の粒子の自然消滅・破棄を更新する。
    private void FixedUpdate()
    {
        CleanupFinishedParticles();
        UpdateEmission();
    }

    // キーフレーム到達時に即座に開始し、姿勢固定中は同じインスタンスでループ放出する。
    private void UpdateEmission()
    {
        if (!isActiveAndEnabled || chainsawDigging == null ||
            playerController == null ||
            particlePrefab == null ||
            emissionPoint == null ||
            !chainsawDigging.isActiveAndEnabled ||
            !playerController.isActiveAndEnabled ||
            !chainsawDigging.IsParticleFrameActive)
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

        ParticleSystem selectedPrefab = SelectParticlePrefab();
        if (particleInstance != null && activeParticlePrefab != selectedPrefab)
        {
            // 旧段階の粒子は消去せず、その場で寿命まで再生させる。
            StopEmission();
        }

        // 段階が変わったときだけ作り直し、旧粒子が残る間に新しい放出を開始する。
        if (particleInstance == null)
        {
            CreateParticles(selectedPrefab, position, rotation);
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

    // 敵専用Prefabを優先し、それ以外はアクセル値で段階を選ぶ。
    private ParticleSystem SelectParticlePrefab()
    {
        if (chainsawDigging.Surface == ChainsawSurface.Enemy && enemyParticlePrefab != null)
        {
            return enemyParticlePrefab;
        }

        float acceleratorValue = accelerator != null ? accelerator.CurrentSpeed : 0f;
        float mediumThreshold = Mathf.Max(0f, mediumAcceleratorThreshold);
        float strongThreshold = Mathf.Max(mediumThreshold + 0.01f, strongAcceleratorThreshold);
        if (accelerator != null && acceleratorValue >= strongThreshold && strongParticlePrefab != null)
            return strongParticlePrefab;
        if (accelerator != null && acceleratorValue >= mediumThreshold && mediumParticlePrefab != null)
            return mediumParticlePrefab;
        return particlePrefab;
    }

    // Inspectorでしきい値の逆転を防ぎ、第1→第2→第3の順序を維持する。
    private void OnValidate()
    {
        mediumAcceleratorThreshold = Mathf.Max(0f, mediumAcceleratorThreshold);
        strongAcceleratorThreshold = Mathf.Max(mediumAcceleratorThreshold + 0.01f, strongAcceleratorThreshold);
    }

    // 食い込み専用のインスタンスを作り、ループ再生の準備をする。
    private void CreateParticles(
        ParticleSystem selectedPrefab,
        Vector3 position,
        Quaternion rotation)
    {
        // 見た目の左右反転やスケールの影響を避けるため、
        // Playerの子にはせず生成する。
        particleInstance = Instantiate(
            selectedPrefab,
            position,
            rotation);
        activeParticlePrefab = selectedPrefab;

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
        activeParticlePrefab = null;
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
        if (chainsawDigging != null)
            chainsawDigging.DiggingParticleFrameChanged -= UpdateEmission;
        ClearAllParticles();
    }

    // オブジェクト破棄時にも片付ける。既に停止済みなら何もしない。
    private void OnDestroy()
    {
        ClearAllParticles();
    }
}
