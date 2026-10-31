using System.Collections.Generic;
using UnityEngine;

// 通常の実行順（0）の敵FixedUpdateより後で、ノックバック速度を反映する。
[DefaultExecutionOrder(10000)]
public class ChainsawAttack : MonoBehaviour
{
    [Header("通常攻撃の判定。食い込み検出とは別")]
    [SerializeField] private Collider hitBox;

    [Header("通常攻撃のノックバック")]
    [Tooltip("命中後、敵の通常移動より攻撃ベクトルを優先する秒数です。終了時にX/Y速度を一度0にして残留速度を止めます。時間は物理更新単位で扱い、ヒットストップ中は進みません。重力が有効な敵には通常の重力加速を加えます。")]
    [SerializeField, Min(0.01f)]
    private float slashKnockbackPriorityDuration = 0.15f;

    // 敵ごとの優先速度と残り時間を保持する。再命中時は同じRigidbodyの記録を更新する。
    private sealed class SlashKnockbackState
    {
        public EnemyAttackHitReceiver Target;
        public EnemyHealth Health;
        public Rigidbody Body;
        public Vector2 Velocity;
        public float RemainingTime;
    }

    private readonly List<SlashKnockbackState> activeSlashKnockbacks =
        new List<SlashKnockbackState>();

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
    private readonly Dictionary<(AttackData, int), AttackData> scaledDiggingData =
        new Dictionary<(AttackData, int), AttackData>();

    private AttackData currentData;
    private float attackHitStopTime;
    private float attackShakeMagnitude;

    private bool attackPrepared;
    private bool hitWindowOpen;
    private bool diggingActive;

    // 通常攻撃の1段ごとに、送信済みの敵を記録する。
    private readonly HashSet<EnemyAttackHitReceiver> hitEnemies =
        new HashSet<EnemyAttackHitReceiver>();

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

        hitWindowOpen = true;
        hitBox.enabled = true;
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

        attackPrepared = false;
        currentData = null;
        attackHitStopTime = 0f;
        attackShakeMagnitude = 0f;

        hitEnemies.Clear();
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
            !diggingActive ||
            enemy == null ||
            data == null)
        {
            return false;
        }

        EnemyAttackHitReceiver receiver =
            enemy.GetComponentInParent<EnemyAttackHitReceiver>();

        if (receiver == null)
        {
            return false;
        }

        // 敵側の既存APIは倍率を受け取らないため、送信するSOのランタイムコピーに反映。
        // 共有AttackDataは絶対に変更しない。
        int scaledDamage = Mathf.Max(0, Mathf.RoundToInt(data.damage * Mathf.Max(0f, multiplier)));
        if (scaledDamage <= 0)
        {
            return false;
        }

        AttackData sendData = GetScaledDiggingData(data, scaledDamage);
        if (!SendAttackHit(receiver, sendData))
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

        if (receiver == null)
        {
            return false;
        }

        // 敵側の既存APIは倍率を受け取らないため、送信するSOのランタイムコピーに反映。
        // 共有AttackDataは絶対に変更しない。
        int scaledDamage = Mathf.Max(0, Mathf.RoundToInt(data.damage * Mathf.Max(0f, multiplier)));
        if (scaledDamage <= 0)
        {
            return false;
        }

        AttackData sendData = GetScaledDiggingData(data, scaledDamage);
        if (!SendAttackHit(receiver, sendData))
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
    private AttackData GetScaledDiggingData(AttackData source, int damage)
    {
        var key = (source, damage);
        if (scaledDiggingData.TryGetValue(key, out AttackData cached) && cached != null)
        {
            return cached;
        }

        AttackData runtimeData = Instantiate(source);
        runtimeData.name = source.name + "_RuntimeDamage_" + damage;
        runtimeData.damage = damage;
        scaledDiggingData[key] = runtimeData;
        return runtimeData;
    }

    /// <summary>武器破棄時にのみ、作成したランタイムSOを解放する。</summary>
    private void OnDestroy()
    {
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
            Time.timeScale <= 0f)
        {
            return;
        }

        EnemyAttackHitReceiver receiver =
            other.GetComponentInParent<EnemyAttackHitReceiver>();

        if (receiver == null ||
            !receiver.isActiveAndEnabled ||
            hitEnemies.Contains(receiver))
        {
            return;
        }

        Vector3 point =
            other.ClosestPoint(hitBox.bounds.center);

        // 受信側で攻撃が中断されても、今回の命中に使う演出値を維持する。
        float hitStopTime = attackHitStopTime;
        float shakeMagnitude = attackShakeMagnitude;

        // 受信側の処理で攻撃が終了しても、命中時のSOと左右判定を維持する。
        AttackData attackData = currentData;
        bool hasKnockback = TryGetSlashKnockbackVector(
            receiver,
            attackData,
            out Vector2 knockbackVector);

        // 送信先の処理中に再度呼ばれても重複送信しないよう、先に登録する。
        hitEnemies.Add(receiver);

        if (!SendAttackHit(receiver, attackData))
        {
            // 送信できなかった場合は、次の接触通知で再試行できるようにする。
            hitEnemies.Remove(receiver);
            return;
        }

        // 通常攻撃だけに適用する。食い込みの既存の押し出しとは重複させない。
        if (hasKnockback)
        {
            ApplySlashKnockback(receiver, other, knockbackVector);
        }

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
                "Player本体を取得できないためノックバックを適用できません。" +
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

    // 生存している敵の初速を設定し、通常移動より優先する速度と時間を登録する。
    private void ApplySlashKnockback(
        EnemyAttackHitReceiver target,
        Collider targetCollider,
        Vector2 knockbackVector)
    {
        if (target == null || !target.isActiveAndEnabled)
        {
            return;
        }

        EnemyHealth enemyHealth = target.GetComponent<EnemyHealth>();
        if (enemyHealth == null || enemyHealth.IsDead)
        {
            // 死亡時の移動・切断演出には干渉しない。
            return;
        }

        // 命中したColliderを実際に動かすRigidbodyを優先する。
        Rigidbody enemyRigidbody = targetCollider != null
            ? targetCollider.attachedRigidbody
            : null;
        if (enemyRigidbody == null)
        {
            enemyRigidbody = target.GetComponentInParent<Rigidbody>();
        }

        if (enemyRigidbody == null || enemyRigidbody.isKinematic)
        {
            WarnKnockbackConfiguration(
                "ノックバックには敵のColliderを動かすRigidbodyが必要です。" +
                "Rigidbodyの有無とIs Kinematicがオフかを確認してください。" +
                "ダメージ処理は継続します。",
                target);
            return;
        }

        if (!enemyRigidbody.gameObject.activeInHierarchy)
        {
            return;
        }

        // 毎回SOの初速でX/Yを置き換え、連続命中による速度の積み上がりを防ぐ。
        // XY平面外のZ速度、重力、質量、位置固定などの設定は変更しない。
        Vector3 velocity = enemyRigidbody.linearVelocity;
        velocity.x = knockbackVector.x;
        velocity.y = knockbackVector.y;
        enemyRigidbody.linearVelocity = velocity;

        // 別の段で再命中したときは、最新のベクトルと優先時間で置き換える。
        SlashKnockbackState knockbackState = null;
        for (int index = 0; index < activeSlashKnockbacks.Count; index++)
        {
            if (activeSlashKnockbacks[index].Body == enemyRigidbody)
            {
                knockbackState = activeSlashKnockbacks[index];
                break;
            }
        }

        if (knockbackState == null)
        {
            knockbackState = new SlashKnockbackState();
            activeSlashKnockbacks.Add(knockbackState);
        }

        knockbackState.Target = target;
        knockbackState.Health = enemyHealth;
        knockbackState.Body = enemyRigidbody;
        knockbackState.Velocity = knockbackVector;
        knockbackState.RemainingTime = Mathf.Max(
            Time.fixedDeltaTime,
            slashKnockbackPriorityDuration);
    }

    // 敵の通常の物理更新後にノックバック速度を反映し、優先時間の終了で制御を返す。
    private void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        for (int index = activeSlashKnockbacks.Count - 1; index >= 0; index--)
        {
            SlashKnockbackState knockbackState = activeSlashKnockbacks[index];
            Rigidbody enemyRigidbody = knockbackState.Body;

            if (knockbackState.Target == null ||
                !knockbackState.Target.isActiveAndEnabled ||
                knockbackState.Health == null ||
                knockbackState.Health.IsDead ||
                enemyRigidbody == null ||
                !enemyRigidbody.gameObject.activeInHierarchy ||
                enemyRigidbody.isKinematic)
            {
                activeSlashKnockbacks.RemoveAt(index);
                continue;
            }

            if (knockbackState.RemainingTime <= 0f)
            {
                StopSlashKnockback(knockbackState);
                activeSlashKnockbacks.RemoveAt(index);
                continue;
            }

            // 敵側の通常移動による速度の上書きを、この物理ステップで置き換える。
            // Z速度は引き継ぎ、SOのベクトルは変更しない。
            Vector3 velocity = enemyRigidbody.linearVelocity;
            velocity.x = knockbackState.Velocity.x;
            velocity.y = knockbackState.Velocity.y;
            enemyRigidbody.linearVelocity = velocity;

            // この後の物理演算が加える重力を、次のステップ用の速度にも引き継ぐ。
            // 初速のYを毎回再設定して、空中で浮き続けることを防ぐ。
            if (enemyRigidbody.useGravity)
            {
                Vector3 gravity = Physics.gravity;
                knockbackState.Velocity += new Vector2(gravity.x, gravity.y) * deltaTime;
            }

            knockbackState.RemainingTime -= deltaTime;
            // 今回設定した速度は、この後の物理演算で使用する。
            // 次の物理更新で残り時間を確認して停止するため、最終ステップも移動できる。
        }
    }

    // 優先時間終了・武器無効化時に、生存中の敵のX/Y残留速度を一度だけ止める。
    private void StopSlashKnockback(SlashKnockbackState knockbackState)
    {
        Rigidbody enemyRigidbody = knockbackState.Body;
        if (knockbackState.Target == null ||
            !knockbackState.Target.isActiveAndEnabled ||
            knockbackState.Health == null ||
            knockbackState.Health.IsDead ||
            enemyRigidbody == null ||
            enemyRigidbody.isKinematic ||
            !enemyRigidbody.gameObject.activeInHierarchy)
        {
            // 死亡処理や無効化済みの敵の速度には干渉しない。
            return;
        }

        // 通常移動が同じステップで設定したX/Y速度もここで一度止まる。
        // 次の更新からは敵自身が通常移動を再開し、重力も通常どおり適用される。
        Vector3 velocity = enemyRigidbody.linearVelocity;
        velocity.x = 0f;
        velocity.y = 0f;
        enemyRigidbody.linearVelocity = velocity;
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
        AttackData attackData)
    {
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

        // 敵の受信実装を、既存の共通インターフェース経由で呼び出す。
        IAttackHitReceiver receiver = target;

        receiver.ReceiveAttackHit(
            new PlayerAttackHitData(attackData));

        return true;
    }
    // 武器が無効になった場合は攻撃を終了し、管理中の敵の残留速度も停止する。
    private void OnDisable()
    {
        EndDigging();
        EndAttack();
        for (int index = 0; index < activeSlashKnockbacks.Count; index++)
        {
            StopSlashKnockback(activeSlashKnockbacks[index]);
        }
        activeSlashKnockbacks.Clear();
    }
}
