using System.Collections.Generic;
using UnityEngine;

// 敵の通常更新後に、弱攻撃の進行と接触無視の残り時間を更新する。
[DefaultExecutionOrder(10000)]
public class ChainsawAttack : MonoBehaviour
{
    private const int SLASH_HIT_COUNT = 4;
    private PlayerAnimator weakAttackAnimator;
    private bool weakAttackStartedInAir;
    private MonoBehaviour weakDigTarget;
    private Collider weakDigCollider;
    private Vector3 weakDigPoint;
    private bool weakDigHolding;
    private bool lastHitDealtDamage;
    private bool weakFinisherAcceleratorGranted;
    private SensorChainsawDigging weakAttackDigging;
    private Collider finishingTargetCollider;

    [Header("食い込み中の敵の震え")]
    [Tooltip("振り抜きの攻撃ベクトルに沿って敵モデルを震わせる片側の幅です。ワールド座標の距離で指定し、0なら震えを無効にします。当たり判定とRigidbodyは動かしません。")]
    [SerializeField, Min(0f)] private float enemyDiggingShakeAmplitude = 0.025f;
    [Tooltip("食い込み中に敵モデルが1秒間に往復する回数です。ヒットストップ中は震えの時間も止まります。")]
    [SerializeField, Min(0f)] private float enemyDiggingShakeFrequency = 20f;
    private bool enemyShakeWarningLogged;

    // モデル表示に加算した位置だけを保持し、アニメーションや物理位置と分離する。
    private sealed class EnemyShakeVisual
    {
        public Transform Root;
        public Vector3 LastOffset;
        public Vector3 LastWrittenPosition;
        public bool OffsetApplied;
    }

    // この武器を使用する攻撃アニメーションの管理元を登録する。
    public void ConfigureWeakAttack(PlayerAnimator owner, bool isAirAttack = false)
    {
        weakAttackAnimator = owner;
        weakAttackStartedInAir = isAirAttack;
        weakAttackDigging = owner != null ? owner.GetComponent<SensorChainsawDigging>() : null;
    }


    [Header("通常攻撃の判定。食い込み検出とは別")]
    [SerializeField] private Collider hitBox;

    [Header("振り抜き後のプレイヤーとの接触無視")]
    [Tooltip("振り抜き後に敵がプレイヤーを押さないよう、両者の接触を一時的に無視する秒数です。敵の速度・位置・地面衝突は敵側で制御します。")]
    [SerializeField, Min(0.01f)]
    private float slashKnockbackPriorityDuration = 0.15f;

    // 振り抜いた敵との衝突だけを一時停止する。元から無視していた組は変更しない。
    private sealed class KnockbackCollisionPair
    {
        public Collider PlayerCollider;
        public Collider EnemyCollider;
        public float RemainingTime;
    }

    private readonly List<KnockbackCollisionPair> knockbackCollisionPairs =
        new List<KnockbackCollisionPair>();

    // プレイヤーに吹き飛ばしの力が伝わらないよう、実際の物理Colliderの組を登録する。
    private void ProtectPlayerFromKnockback(Collider enemyCollider)
    {
        if (weakAttackAnimator == null || enemyCollider == null) return;
        Rigidbody playerBody = weakAttackAnimator.GetComponent<Rigidbody>();
        Rigidbody enemyBody = enemyCollider.attachedRigidbody;
        if (playerBody == null || enemyBody == null || playerBody == enemyBody) return;
        Collider[] playerColliders = playerBody.GetComponentsInChildren<Collider>(true);
        Collider[] enemyColliders = enemyBody.GetComponentsInChildren<Collider>(true);
        float remaining = Mathf.Max(Time.fixedDeltaTime, slashKnockbackPriorityDuration);
        foreach (Collider playerCollider in playerColliders)
        {
            if (playerCollider == null || playerCollider.isTrigger ||
                playerCollider.attachedRigidbody != playerBody) continue;
            foreach (Collider targetCollider in enemyColliders)
            {
                if (targetCollider == null || targetCollider.isTrigger ||
                    targetCollider.attachedRigidbody != enemyBody) continue;
                bool alreadyProtected = false;
                foreach (KnockbackCollisionPair pair in knockbackCollisionPairs)
                {
                    if (pair.PlayerCollider == playerCollider && pair.EnemyCollider == targetCollider)
                    {
                        pair.RemainingTime = Mathf.Max(pair.RemainingTime, remaining);
                        alreadyProtected = true;
                        break;
                    }
                }
                if (alreadyProtected || Physics.GetIgnoreCollision(playerCollider, targetCollider)) continue;
                Physics.IgnoreCollision(playerCollider, targetCollider, true);
                knockbackCollisionPairs.Add(new KnockbackCollisionPair
                {
                    PlayerCollider = playerCollider,
                    EnemyCollider = targetCollider,
                    RemainingTime = remaining
                });
            }
        }
    }

    // 優先時間が終了し、両者の接触も離れた組だけ衝突を戻す。重なったままの復元を防ぐ。
    private void UpdateKnockbackCollisionPairs()
    {
        for (int index = knockbackCollisionPairs.Count - 1; index >= 0; index--)
        {
            KnockbackCollisionPair pair = knockbackCollisionPairs[index];
            pair.RemainingTime -= Time.fixedDeltaTime;
            bool valid = pair.PlayerCollider != null && pair.EnemyCollider != null;
            bool active = valid && pair.PlayerCollider.enabled && pair.EnemyCollider.enabled &&
                pair.PlayerCollider.gameObject.activeInHierarchy && pair.EnemyCollider.gameObject.activeInHierarchy;
            if (active && (pair.RemainingTime > 0f ||
                pair.PlayerCollider.bounds.Intersects(pair.EnemyCollider.bounds))) continue;
            RestoreKnockbackCollisionPair(index);
        }
    }

    // この武器が変更した衝突ペアだけを復元して、管理対象から外す。
    private void RestoreKnockbackCollisionPair(int index)
    {
        KnockbackCollisionPair pair = knockbackCollisionPairs[index];
        if (pair.PlayerCollider != null && pair.EnemyCollider != null)
            Physics.IgnoreCollision(pair.PlayerCollider, pair.EnemyCollider, false);
        knockbackCollisionPairs.RemoveAt(index);
    }

    // 武器の無効化や破棄時に、一時的に無効にした衝突をすべて元に戻す。
    private void RestoreAllKnockbackCollisions()
    {
        for (int index = knockbackCollisionPairs.Count - 1; index >= 0; index--)
            RestoreKnockbackCollisionPair(index);
    }

    // 1～3撃目の見た目の震えだけを管理する。敵の物理制御は変更しない。
    private sealed class HeldEnemyState
    {
        public EnemyAttackHitReceiver Target;
        public EnemyHealth Health;
        public Rigidbody Body;
        public Vector3 ShakeDirection;
        public float ShakeTime;
        public readonly List<EnemyShakeVisual> ShakeVisuals = new List<EnemyShakeVisual>();
    }

    private readonly List<HeldEnemyState> heldEnemies =
        new List<HeldEnemyState>();

    [Header("ダメージ数字")]
    [Tooltip("ダメージ数字を表示するDamagePopupPresenterです。未設定なら起動時にシーン内から取得します。")]
    [SerializeField] private DamagePopupPresenter damagePopupPresenter;

    [Header("ヒット演出")]
    [SerializeField] private GameObject hitParticle;
    [Tooltip("ゲーム画面のカメラを揺らすコンポーネントを指定してください。未設定なら自動取得します。")]
    [SerializeField] private CameraShake_System cameraShake;

    [Tooltip("通常攻撃・食い込み共通の揺れ時間です。0なら0.15秒を使用します。揺れの強さは各攻撃SOで設定します。")]
    [SerializeField, Min(0f)]
    private float duration = 0.15f;

    [Tooltip("命中時に時間を停止するコンポーネントを指定してください。未設定なら自動取得します。")]
    [SerializeField] private HitStop_System hitStopSystem;
    private const float DEFAULT_SHAKE_DURATION = 0.15f;

    // 設定不足の警告を、食い込みの連続命中で繰り返さないための記録。
    private bool shakeWarningLogged;
    private bool hitStopWarningLogged;
    private bool knockbackWarningLogged;

    // 敵受信側はAttackData.damageを直接読むため、共有SOを変更せず
    // ダメージ別のランタイムコピーを保持する。敵のLastPlayerAttackHitDataからも参照される。
    private readonly Dictionary<(AttackData, int, bool), AttackData> scaledDiggingData =
        new Dictionary<(AttackData, int, bool), AttackData>();

    private AttackData currentData;
    private float attackHitStopTime;
    private float attackShakeMagnitude;

    private bool attackPrepared;
    private bool hitWindowOpen;
    private bool diggingActive;
    private int slashHitIndex;

    // 通常攻撃の1段ごとに、送信済みの敵を記録する。
    private readonly HashSet<MonoBehaviour> hitEnemies =
        new HashSet<MonoBehaviour>();

    [Header("アクセルによるダメージボーナス")]
    [Tooltip("ダメージボーナスと弱攻撃の命中加算に使うプレイヤーのアクセル管理です。未指定なら、この武器の最上位の親とその子から取得します。")]
    [SerializeField] private ChainsawAccelerator damageBonusAccelerator;

    [Header("弱攻撃の命中で増えるアクセル")]
    [Tooltip("食い込み3連撃でダメージが成立するたびに増えるアクセル値です。")]
    [SerializeField, Min(0f)] private float weakDiggingAcceleratorGain = 1f;

    [Tooltip("振り抜きでダメージが成立したときに増えるアクセル値です。複数の敵に当たっても弱攻撃1回につき一度だけ増えます。")]
    [SerializeField, Min(0f)] private float weakFinisherAcceleratorGain = 5f;

    // AttackDataのチェック・発動基準・上乗せ割合から、今回のダメージ倍率を求める。
    private float GetAttackDamageMultiplier(AttackData data)
    {
        if (data == null || !data.useAcceleratorDamageMultiplier || damageBonusAccelerator == null)
            return 1f;

        float ratio = Mathf.Clamp01(damageBonusAccelerator.SpeedRatio);
        float threshold = Mathf.Clamp01(data.acceleratorDamageBonusThresholdPercent / 100f);
        if (ratio < threshold) return 1f;
        return 1f + Mathf.Max(0f, data.acceleratorDamageBonusPercent) / 100f;
    }

    // 武器の判定と演出の参照、およびダメージボーナス用アクセル参照を初期化する。
    private void Awake()
    {
        if (hitBox == null)
        {
            Debug.LogError(
                "ChainsawAttackのHit Boxを設定してください。",
                this
            );

            enabled = false;
            return;
        }

        hitBox.enabled = false;
        if (damageBonusAccelerator == null)
            damageBonusAccelerator = transform.root.GetComponentInChildren<ChainsawAccelerator>(true);
        if (damagePopupPresenter == null)
            damagePopupPresenter = FindAnyObjectByType<DamagePopupPresenter>();

        if (cameraShake == null)
        {
            cameraShake =
                FindAnyObjectByType<CameraShake_System>();
        }

        if (hitStopSystem == null)
        {
            hitStopSystem =
                FindAnyObjectByType<HitStop_System>();
        }
    }

    public void BeginAttack(AttackData data)
    {
        EndDigging();
        EndAttack();

        if (data == null ||
            hitBox == null ||
            !isActiveAndEnabled)
        {
            return;
        }

        currentData = data;
        slashHitIndex = 0;
        weakFinisherAcceleratorGranted = false;

        // 各段の開始時点で演出値を保存する。
        attackHitStopTime = Mathf.Max(0f, data.hitStopTime);
        attackShakeMagnitude = Mathf.Max(0f, data.cameraShack);

        attackPrepared = true;
    }

    public void EnableHitBox()
    {
        if (!attackPrepared ||
            !isActiveAndEnabled ||
            hitBox == null)
        {
            return;
        }

        if (weakDigHolding || hitWindowOpen || slashHitIndex >= SLASH_HIT_COUNT)
        {
            return;
        }

        // アニメーションイベント1回につき1撃分の判定を開く。
        slashHitIndex = slashHitIndex == 0 ? 1 : SLASH_HIT_COUNT;
        hitEnemies.Clear();
        hitWindowOpen = true;
        hitBox.enabled = true;
        // 振り抜きは食い込んだ敵へ送る。姿勢変化でColliderが離れても取りこぼさない。
        if (slashHitIndex == SLASH_HIT_COUNT && finishingTargetCollider != null)
        {
            Collider targetCollider = finishingTargetCollider;
            finishingTargetCollider = null;
            TryHitSlash(targetCollider);
        }
    }

    // 振り抜きのイベントから、接続していた敵への4回目の攻撃判定を開始する。
    public void EnableWeakAttackFinisher()
    {
        if (!attackPrepared || weakDigHolding || slashHitIndex == 0 ||
            slashHitIndex >= SLASH_HIT_COUNT) return;
        DisableHitBox();
        EnableHitBox();
    }

    public void DisableHitBox()
    {
        hitWindowOpen = false;

        if (hitBox != null)
        {
            hitBox.enabled = false;
        }
    }

    public void EndAttack()
    {
        DisableHitBox();
        FinishWeakDigHold();
        ReleaseAllHeldEnemies();

        attackPrepared = false;
        currentData = null;
        attackHitStopTime = 0f;
        attackShakeMagnitude = 0f;

        hitEnemies.Clear();
        slashHitIndex = 0;
        finishingTargetCollider = null;
    }

    public void BeginDigging()
    {
        EndAttack();

        diggingActive = true;
    }

    public void EndDigging()
    {
        if (!diggingActive)
        {
            return;
        }

        diggingActive = false;
    }

    /// <summary>
    /// 食い込み対象の敵へ命中情報を送り、送信できた場合に専用演出を再生する。
    /// </summary>
    /// <remarks>
    /// 対象をComponentで受け取り、既存の食い込み側からの呼び出しを維持する。
    /// 受信側がAttackData.damageを直接読むため、倍率は共有SOではなく実行時コピーに反映する。
    /// </remarks>
    public bool HitDigging(
        Component enemy,
        AttackData data,
        float multiplier,
        Vector3 point)
    {
        if (!isActiveAndEnabled ||
            (!diggingActive && !weakDigHolding) ||
            enemy == null ||
            data == null)
        {
            return false;
        }

        EnemyAttackHitReceiver receiver =
            enemy.GetComponentInParent<EnemyAttackHitReceiver>();

        if (receiver == null && !(enemy is IAttackHitReceiver))
        {
            return false;
        }

        // 敵側の既存APIは倍率を受け取らないため、送信するSOのランタイムコピーに反映。
        // 共有AttackDataは絶対に変更しない。
        // 接触などの固定倍率に、このAttackDataで許可したボーナスだけを一度加える。
        int scaledDamage = Mathf.Max(0, Mathf.RoundToInt(
            data.damage * Mathf.Max(0f, multiplier) * GetAttackDamageMultiplier(data)));
        if (scaledDamage <= 0)
        {
            return false;
        }

        AttackData sendData = GetScaledDiggingData(data, scaledDamage, weakDigHolding || receiver == null);
        // 専用ReceiverにはHP取得APIがないため、有効な接続先への送信を1撃として数える。
        if (receiver == null)
        {
            ((IAttackHitReceiver)enemy).ReceiveAttackHit(new PlayerAttackHitData(sendData));
            lastHitDealtDamage = true;
        }
        else if (!SendAttackHit(receiver, sendData, point))
        {
            return false;
        }

        // 食い込みも、呼び出し元が渡した攻撃SOの演出設定を使用する。
        PlayHitEffects(
            point,
            Mathf.Max(0f, data.hitStopTime),
            true,
            Mathf.Max(0f, data.cameraShack));

        return true;
    }

    // 成立済みの移動接触を送信する。被弾で食い込みが解除されても今回の命中は取り消さない。
    public bool HitMovementContact(
        Component enemy,
        AttackData data,
        float multiplier,
        Vector3 point)
    {
        if (!isActiveAndEnabled ||
            enemy == null ||
            data == null)
        {
            return false;
        }

        EnemyAttackHitReceiver receiver =
            enemy.GetComponentInParent<EnemyAttackHitReceiver>();

        if (receiver == null && !(enemy is IAttackHitReceiver))
        {
            return false;
        }

        // 敵側の既存APIは倍率を受け取らないため、送信するSOのランタイムコピーに反映。
        // 共有AttackDataは絶対に変更しない。
        // 接触などの固定倍率に、このAttackDataで許可したボーナスだけを一度加える。
        int scaledDamage = Mathf.Max(0, Mathf.RoundToInt(
            data.damage * Mathf.Max(0f, multiplier) * GetAttackDamageMultiplier(data)));
        if (scaledDamage <= 0)
        {
            return false;
        }

        AttackData sendData = GetScaledDiggingData(data, scaledDamage, receiver == null);
        if (receiver == null)
        {
            ((IAttackHitReceiver)enemy).ReceiveAttackHit(new PlayerAttackHitData(sendData));
        }
        else if (!SendAttackHit(receiver, sendData, point))
        {
            return false;
        }

        // 食い込みも、呼び出し元が渡した攻撃SOの演出設定を使用する。
        PlayHitEffects(
            point,
            Mathf.Max(0f, data.hitStopTime),
            true,
            Mathf.Max(0f, data.cameraShack));

        return true;
    }

    /// <summary>
    /// 敵側を変更せず、指定した整数ダメージを送るための実行時AttackDataを取得する。
    /// コピーは武器の生存中保持し、受信側の最後の命中情報から参照可能にする。
    /// </summary>
    private AttackData GetScaledDiggingData(AttackData source, int damage, bool suppressKnockback = false)
    {
        var key = (source, damage, suppressKnockback);
        if (scaledDiggingData.TryGetValue(key, out AttackData cached) && cached != null)
        {
            return cached;
        }

        AttackData runtimeData = Instantiate(source);
        runtimeData.name = source.name + "_RuntimeDamage_" + damage;
        runtimeData.damage = damage;
        // 弱攻撃の最初の3回は共有SOを変更せず、送信するコピーの吹き飛ばしだけを無効にする。
        if (suppressKnockback) runtimeData.attackVector = Vector2.zero;
        scaledDiggingData[key] = runtimeData;
        return runtimeData;
    }

    /// <summary>武器破棄時にのみ、作成したランタイムSOを解放する。</summary>
    private void OnDestroy()
    {
        RestoreAllKnockbackCollisions();
        ReleaseAllHeldEnemies();
        foreach (AttackData data in scaledDiggingData.Values)
        {
            if (data != null) Destroy(data);
        }
        scaledDiggingData.Clear();

    }

    private void OnTriggerEnter(Collider other)
    {
        TryHitSlash(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryHitSlash(other);
    }

    /// <summary>
    /// 通常攻撃の命中を確認し、同じ敵へ1段につき1回だけ命中情報を送る。
    /// </summary>
    private void TryHitSlash(Collider other)
    {
        if (!isActiveAndEnabled ||
            !attackPrepared ||
            !hitWindowOpen ||
            hitBox == null ||
            !hitBox.enabled ||
            other == null ||
            !ChainsawContactZone.AllowsAttack(other) ||
            Time.timeScale <= 0f || weakDigHolding)
        {
            return;
        }

        EnemyAttackHitReceiver regular = other.GetComponentInParent<EnemyAttackHitReceiver>();
        MonoBehaviour receiver = regular;
        if (regular == null && weakAttackDigging != null && weakAttackDigging.IsEnemyHitLayer(other))
            receiver = other.GetComponentInParent<IAttackHitReceiver>() as MonoBehaviour;

        if (receiver == null ||
            !receiver.isActiveAndEnabled ||
            hitEnemies.Contains(receiver))
        {
            return;
        }

        Vector3 point =
            other.ClosestPoint(hitBox.bounds.center);

        if (slashHitIndex < SLASH_HIT_COUNT)
        {
            if (weakAttackAnimator == null || weakAttackDigging == null ||
                !weakAttackAnimator.HoldWeakAttackPose()) return;
            weakDigTarget = receiver;
            weakDigCollider = other;
            weakDigPoint = point;
            weakDigHolding = true;
            if (!weakAttackDigging.BeginWeakEnemyAttack(receiver, other, point, weakAttackStartedInAir))
            {
                FinishWeakDigHold();
                return;
            }
            finishingTargetCollider = other;
            if (regular != null) HoldEnemy(regular, other);
            DisableHitBox();
            return;
        }

        // 受信側で攻撃が中断されても、今回の命中に使う演出値を維持する。
        float hitStopTime = attackHitStopTime;
        float shakeMagnitude = attackShakeMagnitude;

        // 受信側の処理で攻撃が終了しても、命中時のSOと左右判定を維持する。
        bool isFinishingHit = slashHitIndex >= SLASH_HIT_COUNT;
        // OnWeakAttackSwingで開いたこの区間に、開始時の振り抜き専用SOのベクトルを適用する。
        AttackData sourceData = currentData;
        int attackDamage = Mathf.Max(0, Mathf.RoundToInt(
            sourceData.damage * GetAttackDamageMultiplier(sourceData)));
        // 共有SOを変更せず、今回の命中に使うダメージだけを実行時コピーへ反映する。
        AttackData attackData = GetScaledDiggingData(sourceData, attackDamage);
        // 専用Receiverには振り抜きダメージだけを送り、物理制御はボスに任せる。
        if (regular == null)
        {
            hitEnemies.Add(receiver);
            AttackData bossData = GetScaledDiggingData(attackData, attackData.damage, true);
            ((IAttackHitReceiver)receiver).ReceiveAttackHit(new PlayerAttackHitData(bossData));
            // 専用ReceiverはHPを公開しないため、既存の食い込みと同様に正のダメージの送信を命中として扱う。
            if (attackDamage > 0) GrantWeakFinisherAccelerator();
            PlayHitEffects(point, hitStopTime, true, shakeMagnitude);
            return;
        }
        bool hasKnockback = TryGetSlashKnockbackVector(
            regular,
            currentData,
            out Vector2 knockbackVector);

        // 送信先の処理中に再度呼ばれても重複送信しないよう、先に登録する。
        hitEnemies.Add(regular);

        if (isFinishingHit)
        {
            ReleaseHeldEnemy(regular);
            if (hasKnockback) ProtectPlayerFromKnockback(other);
        }
        if (!SendAttackHit(regular, attackData, point))
        {
            // 送信できなかった場合は、次の接触通知で再試行できるようにする。
            hitEnemies.Remove(regular);
            return;
        }

        if (lastHitDealtDamage) GrantWeakFinisherAccelerator();

        // ベクトルは既存の命中情報で敵側へ送信済み。速度をプレイヤー側から再設定しない。

        PlayHitEffects(
            point,
            hitStopTime,
            true,
            shakeMagnitude);
    }

    // 命中時のPlayer本体と敵の位置から、SOのXだけを必要に応じて反転する。
    private bool TryGetSlashKnockbackVector(
        EnemyAttackHitReceiver target,
        AttackData attackData,
        out Vector2 knockbackVector)
    {
        knockbackVector = Vector2.zero;

        if (attackData == null || attackData.damage <= 0 ||
            attackData.attackVector == Vector2.zero)
        {
            return false;
        }

        if (!target.TryGetPlayerTransform(out Transform playerTransform))
        {
            WarnKnockbackConfiguration(
                "Player本体を取得できないため、震え方向と接触無視の左右判定を取得できません。" +
                "Player本体にPlayerタグを設定してください。",
                target);
            return false;
        }

        // SOはPlayerが敵の左側にいる場合の初速（m/s）として扱う。
        // 正規化せず大きさを維持し、右側から攻撃した場合だけXを反転する。
        knockbackVector = attackData.attackVector;
        if (playerTransform.position.x > target.transform.position.x)
        {
            knockbackVector.x = -knockbackVector.x;
        }

        return true;
    }

    // 弱攻撃の進行・震え対象の有効性・一時的な接触無視を更新する。
    private void FixedUpdate()
    {
        UpdateKnockbackCollisionPairs();
        TickWeakDigHold();
        UpdateHeldEnemies();
    }

    // 接続先の消失やアニメーション中断を監視する。ダメージ間隔は通常食い込み側で管理する。
    private void TickWeakDigHold()
    {
        if (!weakDigHolding || Time.timeScale <= 0f) return;
        if (!attackPrepared || weakAttackAnimator == null || !weakAttackAnimator.IsWeakAttackHeld ||
            weakAttackDigging == null || !weakAttackDigging.IsWeakEnemyAttack ||
            weakDigTarget == null || !weakDigTarget.isActiveAndEnabled || weakDigCollider == null)
        {
            FinishWeakDigHold();
        }
    }

    // 通常敵食い込みの共通処理から、HPを減らせた回数を数えて3回で再開する。
    public bool RegisterWeakEnemyDamage()
    {
        if (!weakDigHolding || !lastHitDealtDamage) return false;
        slashHitIndex++;
        if (damageBonusAccelerator != null)
            damageBonusAccelerator.AddSpeed(weakDiggingAcceleratorGain);
        // 初段の接触自体はダメージに数えないため、接触時の値1に3回を足す。
        if (slashHitIndex >= SLASH_HIT_COUNT) FinishWeakDigHold();
        return true;
    }

    // 複数の敵に振り抜きが当たった場合も、加算は弱攻撃1回につき一度にする。
    private void GrantWeakFinisherAccelerator()
    {
        if (weakFinisherAcceleratorGranted || damageBonusAccelerator == null) return;

        weakFinisherAcceleratorGranted = true;
        damageBonusAccelerator.AddSpeed(weakFinisherAcceleratorGain);
    }

    // プレイヤーの食い込み停止姿勢を解除し、次の判定を振り抜きとして扱う。
    private void FinishWeakDigHold()
    {
        if (weakDigHolding) DisableHitBox();
        foreach (HeldEnemyState state in heldEnemies) RestoreEnemyShake(state);
        weakDigHolding = false;
        if (weakAttackDigging != null) weakAttackDigging.EndWeakEnemyAttack();
        weakDigTarget = null;
        weakDigCollider = null;
        if (weakAttackAnimator != null) weakAttackAnimator.ResumeWeakAttackPose();
        // 次の判定開始イベントを振り抜きとして扱う。
        if (attackPrepared && slashHitIndex > 0) slashHitIndex = SLASH_HIT_COUNT - 1;
    }

    // 敵死亡や通常食い込み側のキャンセルで、停止姿勢を解除する。
    public void EndWeakEnemyDamage()
    {
        FinishWeakDigHold();
        ReleaseAllHeldEnemies();
    }

    // 最初の3撃で命中した敵を、物理状態を変更せず震えの対象に登録する。
    private void HoldEnemy(EnemyAttackHitReceiver target, Collider targetCollider)
    {
        if (target == null || !target.isActiveAndEnabled)
        {
            return;
        }

        EnemyHealth health = target.GetComponent<EnemyHealth>();
        if (health == null || health.IsDead)
        {
            return;
        }

        Rigidbody body = targetCollider != null ? targetCollider.attachedRigidbody : null;
        if (body == null)
        {
            body = target.GetComponentInParent<Rigidbody>();
        }

        if (body == null || body.isKinematic || !body.gameObject.activeInHierarchy)
        {
            return;
        }

        for (int index = 0; index < heldEnemies.Count; index++)
        {
            if (heldEnemies[index].Body == body)
            {
                return;
            }
        }

        HeldEnemyState state = new HeldEnemyState
        {
            Target = target,
            Health = health,
            Body = body
        };

        if (TryGetSlashKnockbackVector(target, currentData, out Vector2 vector))
            state.ShakeDirection = new Vector3(vector.x, vector.y, 0f).normalized;
        CollectEnemyShakeVisuals(state);
        heldEnemies.Add(state);
    }

    // 物理要素を持たない表示用のTransformだけを、敵モデルから収集する。
    private void CollectEnemyShakeVisuals(HeldEnemyState state)
    {
        Transform boundary = state.Body.transform;
        foreach (Renderer renderer in state.Body.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer;
            Transform candidate = skin != null && skin.rootBone != null ? skin.rootBone : renderer.transform;
            if (candidate == boundary || !candidate.IsChildOf(boundary) || !CanShakeVisual(candidate)) continue;
            // 骨とメッシュをまとめられる場合は、安全な共通の親を選ぶ。
            while (candidate.parent != null && candidate.parent != boundary && CanShakeVisual(candidate.parent))
                candidate = candidate.parent;
            bool covered = false;
            for (int index = state.ShakeVisuals.Count - 1; index >= 0; index--)
            {
                Transform existing = state.ShakeVisuals[index].Root;
                if (candidate == existing || candidate.IsChildOf(existing))
                {
                    covered = true;
                    break;
                }
                if (existing.IsChildOf(candidate)) state.ShakeVisuals.RemoveAt(index);
            }
            if (!covered) state.ShakeVisuals.Add(new EnemyShakeVisual { Root = candidate });
        }
        if (state.ShakeVisuals.Count == 0 && !enemyShakeWarningLogged && enemyDiggingShakeAmplitude > 0f)
        {
            enemyShakeWarningLogged = true;
            Debug.LogWarning("食い込みの震えに使用できる表示用の子Transformがありません。敵モデルの表示部分をCollider・Rigidbodyと別の子階層に分けてください。", state.Target);
        }
    }

    // 移動すると物理判定へ影響するTransformを、震えの対象から除外する。
    private bool CanShakeVisual(Transform candidate)
    {
        return candidate.GetComponentsInChildren<Collider>(true).Length == 0 &&
               candidate.GetComponentsInChildren<Rigidbody>(true).Length == 0;
    }

    // アニメーション評価後に、振り抜き方向へモデルだけを一定幅で往復させる。
    private void LateUpdate()
    {
        foreach (HeldEnemyState state in heldEnemies)
        {
            bool shaking = weakDigHolding && state.Target == weakDigTarget &&
                state.Target != null && state.Target.isActiveAndEnabled &&
                state.Health != null && !state.Health.IsDead;
            if (!shaking)
            {
                RestoreEnemyShake(state);
                continue;
            }
            if (Time.timeScale <= 0f) continue;
            state.ShakeTime += Time.deltaTime;
            float distance = Mathf.Sin(state.ShakeTime * Mathf.Max(0f, enemyDiggingShakeFrequency) * Mathf.PI * 2f) *
                Mathf.Max(0f, enemyDiggingShakeAmplitude);
            Vector3 worldOffset = state.ShakeDirection * distance;
            foreach (EnemyShakeVisual visual in state.ShakeVisuals)
            {
                RestoreEnemyShakeVisual(visual);
                if (visual.Root == null || !visual.Root.gameObject.activeInHierarchy) continue;
                Vector3 localOffset = visual.Root.parent != null
                    ? visual.Root.parent.InverseTransformVector(worldOffset) : worldOffset;
                visual.Root.localPosition += localOffset;
                visual.LastOffset = localOffset;
                visual.LastWrittenPosition = visual.Root.localPosition;
                visual.OffsetApplied = true;
            }
        }
    }

    // 食い込み中の敵モデルに加えた震えを解除する。
    private void RestoreEnemyShake(HeldEnemyState state)
    {
        foreach (EnemyShakeVisual visual in state.ShakeVisuals) RestoreEnemyShakeVisual(visual);
    }

    // 追加した位置だけを戻す。アニメーション等が位置を書き直していた場合は差し引かない。
    private void RestoreEnemyShakeVisual(EnemyShakeVisual visual)
    {
        if (visual.Root != null && visual.OffsetApplied &&
            (visual.Root.localPosition - visual.LastWrittenPosition).sqrMagnitude < 0.0000000001f)
            visual.Root.localPosition -= visual.LastOffset;
        visual.OffsetApplied = false;
        visual.LastOffset = Vector3.zero;
    }

    // 震え対象の敵が死亡・無効化した場合に、表示補正と記録だけを解除する。
    private void UpdateHeldEnemies()
    {
        for (int index = heldEnemies.Count - 1; index >= 0; index--)
        {
            HeldEnemyState state = heldEnemies[index];
            if (state.Target == null || !state.Target.isActiveAndEnabled ||
                state.Health == null || state.Health.IsDead ||
                state.Body == null || state.Body.isKinematic || !state.Body.gameObject.activeInHierarchy)
            {
                RestoreHeldEnemy(index);
                continue;
            }

        }
    }

    // 振り抜きが当たった敵の震えを終了する。
    private void ReleaseHeldEnemy(EnemyAttackHitReceiver target)
    {
        for (int index = heldEnemies.Count - 1; index >= 0; index--)
        {
            if (heldEnemies[index].Target == target)
            {
                RestoreHeldEnemy(index);
            }
        }
    }

    // 攻撃終了時に、すべての敵の震えを終了する。
    private void ReleaseAllHeldEnemies()
    {
        for (int index = heldEnemies.Count - 1; index >= 0; index--)
        {
            RestoreHeldEnemy(index);
        }
    }

    // 見た目に加えた震えを戻し、敵の物理設定に触れず管理対象から外す。
    private void RestoreHeldEnemy(int index)
    {
        HeldEnemyState state = heldEnemies[index];
        RestoreEnemyShake(state);

        heldEnemies.RemoveAt(index);
    }

    // ノックバックに必要な設定が不足している場合、このコンポーネントにつき一度だけ知らせる。
    private void WarnKnockbackConfiguration(string message, Object target)
    {
        if (knockbackWarningLogged)
        {
            return;
        }

        knockbackWarningLogged = true;
        Debug.LogWarning("ChainsawAttack：" + message, target);
    }
    // 命中送信が成功したときに、攻撃SOの設定で揺れ・時間停止・命中エフェクトを実行する。
    private void PlayHitEffects(
        Vector3 point,
        float hitStop,
        bool shake,
        float shakeMagnitude)
    {
        if (shake && shakeMagnitude > 0f)
        {
            if (cameraShake != null && cameraShake.isActiveAndEnabled)
            {
                float shakeDuration = duration > 0f
                    ? duration
                    : DEFAULT_SHAKE_DURATION;

                cameraShake.Shake(shakeDuration, shakeMagnitude);
            }
            else if (!shakeWarningLogged)
            {
                shakeWarningLogged = true;
                Debug.LogWarning(
                    "命中しましたがカメラシェイクを実行できません。"
                    + "Camera Shakeにゲーム画面の有効なCameraShake_Systemを指定してください。",
                    this);
            }
        }

        if (hitStop > 0f)
        {
            if (hitStopSystem != null && hitStopSystem.isActiveAndEnabled)
            {
                hitStopSystem.StopTime(hitStop);
            }
            else if (!hitStopWarningLogged)
            {
                hitStopWarningLogged = true;
                Debug.LogWarning(
                    "命中しましたがヒットストップを実行できません。"
                    + "Hit Stop Systemに有効なHitStop_Systemを指定してください。",
                    this);
            }
        }

        if (hitParticle == null)
        {
            return;
        }

        GameObject particle = Instantiate(
            hitParticle,
            point,
            Quaternion.identity
        );

        ParticleSystem particleSystem =
            particle.GetComponent<ParticleSystem>();

        float lifetime =
            particleSystem == null
                ? 2f
                : particleSystem.main.duration +
                  particleSystem.main.startLifetime.constantMax;

        Destroy(particle, lifetime);
    }

    /// <summary>
    /// 生存中の敵へ、攻撃データをIAttackHitReceiver経由で送信する。
    /// </summary>
    /// <returns>
    /// 命中情報を送信した場合はtrue。実際のHP変更は受信側が担当する。
    /// </returns>
    private bool SendAttackHit(
        EnemyAttackHitReceiver target,
        AttackData attackData,
        Vector3 point)
    {
        lastHitDealtDamage = false;
        if (target == null ||
            !target.isActiveAndEnabled ||
            attackData == null)
        {
            return false;
        }

        EnemyHealth enemyHealth =
            target.GetComponent<EnemyHealth>();

        if (enemyHealth == null || enemyHealth.IsDead)
        {
            return false;
        }

        // HP変更通知を受信中だけ読み、無効な命中や0ダメージでは数字を出さない。
        // 死亡演出で敵がその場で破棄されても、通知された実ダメージを保持する。
        int actualDamage = 0;
        // 敵HPの減少だけを集計し、回復通知をダメージへ加算しない。
        void RecordDamage(int previousHealth, int currentHealth)
        {
            actualDamage += Mathf.Max(0, previousHealth - currentHealth);
        }

        enemyHealth.HealthChanged += RecordDamage;
        try
        {
            // 敵の受信実装を、既存の共通インターフェース経由で呼び出す。
            IAttackHitReceiver receiver = target;
            receiver.ReceiveAttackHit(new PlayerAttackHitData(attackData));
        }
        finally
        {
            enemyHealth.HealthChanged -= RecordDamage;
        }

        lastHitDealtDamage = actualDamage > 0;
        // 表示の有無で攻撃の成功や消費量を変えない。
        if (actualDamage > 0 && damagePopupPresenter != null)
            damagePopupPresenter.Show(point, actualDamage, attackData);
        return true;
    }
    // 武器が無効になった場合は攻撃と震えを終了し、一時的な接触無視を解除する。
    private void OnDisable()
    {
        RestoreAllKnockbackCollisions();
        EndDigging();
        EndAttack();

    }
}
