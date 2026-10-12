using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerHealth : MonoBehaviour
{
    [Tooltip("プレイヤーの最大HPを設定するアセットです。")]
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

    // 同じオブジェクトのRigidbodyと本体Colliderを取得し、HPを初期化する。
    private void Awake()
    {
        playerBody = GetComponent<Rigidbody>();
        if (healthData == null || playerBody == null)
        {
            Debug.LogError("PlayerHealth：HP設定と、同じオブジェクトのRigidbodyが必要です。", this);
            enabled = false;
            return;
        }

        bodyColliders = GetComponentsInChildren<Collider>(true);
        maxHealth = healthData.MaxHealth;
        currentHealth = maxHealth;
        initialized = true;
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
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }
}
