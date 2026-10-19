using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerHealth : MonoBehaviour
{
    [Tooltip("最大HPと、自動回復の待ち時間・回復量・回復間隔を設定するアセットです。")]
    [SerializeField] private PlayerHealthData healthData;

    [Tooltip("実行中の現在HPです。自動更新される確認用の値です。")]
    [SerializeField] private float currentHealth;

    private static readonly HashSet<PlayerHealth> activePlayers =
        new HashSet<PlayerHealth>();

    private readonly Dictionary<DamageFloorData, ExposureState> exposures =
        new Dictionary<DamageFloorData, ExposureState>();

    private Rigidbody playerBody;
    private Collider[] bodyColliders;
    private float maxHealth;
    private bool initialized;
    private double nextRecoveryTime;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => currentHealth <= 0f;

    // 現在HPと最大HP、HPが0になった瞬間を外部へ通知する。
    public event Action<float, float> HealthChanged;
    public event Action Died;

    // プレイヤーごと・設定SOごとの接触履歴。共有SOには書き込まない。
    private sealed class ExposureState
    {
        public double ElapsedSeconds;
        public float AppliedLoss;
        public double LastFixedTime = double.NegativeInfinity;
    }

    // 親の移動用Rigidbodyと、HP側のColliderを取得してHPを初期化する。
    private void Awake()
    {
        SensorPlayerController controller =
            GetComponentInParent<SensorPlayerController>();

        if (controller == null)
        {
            Debug.LogError(
                "PlayerHealth：自分または親に"
                + "SensorPlayerControllerが必要です。",
                this);

            enabled = false;
            return;
        }

        // Mid_Colliderではなく、移動ControllerがあるPlayerのRigidbodyを使う。
        playerBody = controller.GetComponent<Rigidbody>();

        if (healthData == null || playerBody == null)
        {
            Debug.LogError(
                "PlayerHealth：Health Dataと、"
                + "SensorPlayerControllerと同じオブジェクトの"
                + "Rigidbodyが必要です。",
                this);

            enabled = false;
            return;
        }

        // ダメージ床の判定対象は、これまでどおりHP側のColliderを使用する。
        bodyColliders = GetComponentsInChildren<Collider>(true);

        maxHealth = healthData.MaxHealth;
        currentHealth = maxHealth;
        initialized = true;
        ResetRecoveryTimer();
    }

    // 被ダメージ後の待ち時間を過ぎたら、指定間隔で最大HPまで回復し変更を通知する。
    private void FixedUpdate()
    {
        if (!initialized || IsDead || healthData == null ||
            currentHealth >= maxHealth || healthData.RecoveryAmount <= 0f)
        {
            return;
        }

        double currentTime = Time.timeAsDouble;
        if (currentTime < nextRecoveryTime)
        {
            return;
        }

        // 無効化中などに過ぎた回復回数を、一度にまとめて適用しない。
        // 通知先でダメージやリスポーンが発生しても、そこで設定する待ち時間を優先する。
        nextRecoveryTime = currentTime + healthData.RecoveryInterval;
        float recoveredHealth = Mathf.Min(
            maxHealth, currentHealth + healthData.RecoveryAmount);

        if (recoveredHealth <= currentHealth)
        {
            return;
        }

        currentHealth = recoveredHealth;
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    // 次の回復を最後の被ダメージからの待ち時間後へ予約する。経過状態はSOへ保存しない。
    private void ResetRecoveryTimer()
    {
        nextRecoveryTime = Time.timeAsDouble + healthData.RecoveryDelay;
    }
    // 有効なプレイヤーを床の照会対象へ登録する。HPはリセットしない。
    private void OnEnable()
    {
        if (initialized) activePlayers.Add(this);
    }

    // 無効になったプレイヤーを照会対象から外す。接触履歴は保持する。
    private void OnDisable()
    {
        activePlayers.Remove(this);
    }

    // イベントで登録状態が変わっても安全に処理できるよう、対象一覧をコピーする。
    public static void CopyActivePlayers(List<PlayerHealth> destination)
    {
        destination.Clear();
        foreach (PlayerHealth player in activePlayers)
        {
            if (player != null && player.isActiveAndEnabled)
                destination.Add(player);
        }
    }

    // 本体Colliderと指定領域の重なりを調べる。武器や食い込み用Triggerは除外する。
    public bool OverlapsDamageZone(Collider zone)
    {
        if (!initialized || !isActiveAndEnabled || IsDead || zone == null)
            return false;

        foreach (Collider bodyCollider in bodyColliders)
        {
            if (bodyCollider == null || !bodyCollider.enabled ||
                !bodyCollider.gameObject.activeInHierarchy || bodyCollider.isTrigger ||
                bodyCollider.attachedRigidbody != playerBody)
            {
                continue;
            }

            if (Physics.ComputePenetration(
                zone, zone.transform.position, zone.transform.rotation,
                bodyCollider, bodyCollider.transform.position, bodyCollider.transform.rotation,
                out _, out _))
            {
                return true;
            }
        }

        return false;
    }

    // 接触している物理更新だけ時間を進め、最大HP基準の累計減少量の差分を適用する。
    public void ApplyFloorContact(DamageFloorData data)
    {
        if (!initialized || !isActiveAndEnabled || IsDead || data == null)
            return;

        if (!exposures.TryGetValue(data, out ExposureState state))
        {
            state = new ExposureState();
            exposures.Add(data, state);
        }

        // 同じSOを使う複数の床・Colliderが重なっても、1物理更新につき1回にする。
        double fixedTime = Time.fixedTimeAsDouble;
        if (state.LastFixedTime == fixedTime) return;
        state.LastFixedTime = fixedTime;

        state.ElapsedSeconds = Math.Min(
            data.SecondsToZero, state.ElapsedSeconds + Time.fixedDeltaTime);
        float normalizedTime = Mathf.Clamp01(
            (float)(state.ElapsedSeconds / data.SecondsToZero));
        float loss = Mathf.Max(state.AppliedLoss, data.EvaluateLoss(normalizedTime));
        float damage = maxHealth * (loss - state.AppliedLoss);
        state.AppliedLoss = loss;

        // 終了点では浮動小数点の丸めで微小なHPが残らないようにする。
        if (normalizedTime >= 1f || loss >= 1f)
            damage = currentHealth;

        TakeDamage(damage);
    }

    // HPを減らし、変更を通知する。死亡通知は生存から0になったときだけ発行する。
    public void TakeDamage(float amount)
    {
        if (!initialized || IsDead || amount <= 0f ||
            float.IsNaN(amount) || float.IsInfinity(amount)) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        // 敵の攻撃とダメージ床のどちらでも、受けた時点で回復待ち時間を数え直す。
        ResetRecoveryTimer();
        bool died = IsDead;
        HealthChanged?.Invoke(currentHealth, maxHealth);
        if (died) Died?.Invoke();
    }

    // リスポーン処理から呼び、HPとすべての床の接触履歴を初期化する。
    public void RestoreFullHealth()
    {
        if (!initialized) return;

        exposures.Clear();
        currentHealth = maxHealth;
        ResetRecoveryTimer();
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }
}
