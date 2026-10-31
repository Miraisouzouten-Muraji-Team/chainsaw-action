using System.Collections.Generic;
using UnityEngine;

public class SensorChainsawDigging : MonoBehaviour
{
    public enum InputMode { Toggle, Hold }

    [Header("食い込みパラメータ")]
    [Tooltip("食い込みの操作方法、速度、消費量などを設定したScriptableObject")]
    [SerializeField] private DiggingParameter diggingParameter;

    [Header("参照（Player上の参照は未設定なら自動取得）")]
    [Tooltip("接触先の判定")]
    [SerializeField] private SensorChainsawContactDetector detector;

    [Tooltip("回転速度の管理")]
    [SerializeField] private ChainsawAccelerator accelerator;

    [Tooltip("アニメーション制御")]
    [SerializeField] private PlayerAnimator playerAnimator;

    [Tooltip("攻撃処理")]
    [SerializeField] private ChainsawAttack chainsawAttack;

    [Tooltip("敵への食い込み攻撃データ")]
    [SerializeField] private AttackData diggingAttackData;

    private float wallContactLostTime;
    private float ceilingBonusRamp;

    public bool IsRequested { get; private set; }

    public bool IsDigging =>
        Surface != ChainsawSurface.None;

    public ChainsawSurface Surface { get; private set; }

    private const float BONUS_ACCELERATOR_RATIO = 0.8f;

    // 通常上限に対する現在のアクセルで判定する。最大50なら40以上で成立する。
    private bool MeetsBonusAcceleratorThreshold =>
        accelerator != null && accelerator.SpeedRatio >= BONUS_ACCELERATOR_RATIO;

    // 開始時の判定を保持せず、食い込み中の現在値でボーナスを返す。
    public bool HasBonus =>
        isActiveAndEnabled && IsDigging && MeetsBonusAcceleratorThreshold;

    /// <summary>壁食い込みへ新しく入るたびに増える識別番号。移動制御側の壁進入検出に使用する。</summary>
    public uint WallEntryVersion { get; private set; }

    /// <summary>アクセルが80%以上の間だけ回避する。解除後の時間制回避にも同じ条件を適用する。</summary>
    public bool IsEvading =>
        isActiveAndEnabled && MeetsBonusAcceleratorThreshold &&
        (IsDigging ? HasBonus : Time.time < evadeUntil);

    /// <summary>床・壁・天井を移動する食い込み中か。</summary>
    public bool IsMovementDigging => isActiveAndEnabled && IsDigging &&
        (Surface == ChainsawSurface.Floor || Surface == ChainsawSurface.Wall ||
         Surface == ChainsawSurface.Ceiling);

    public bool SuppressGravity =>
        IsDigging &&
        (
            Surface == ChainsawSurface.Floor ||
            Surface == ChainsawSurface.Wall ||
            Surface == ChainsawSurface.Ceiling ||
            Surface == ChainsawSurface.Enemy
        );

    public Vector3 SurfaceNormal =>
        contact.Normal;

    public float MoveSpeedBonus =>
        !IsDigging || accelerator == null
            ? 0f
            : Surface == ChainsawSurface.Floor
                ? accelerator.CurrentSpeed /
                  Mathf.Max(0.01f, diggingParameter.floorSpeedDivisor)
                : Surface == ChainsawSurface.Ceiling
                    ? accelerator.CurrentSpeed /
                      Mathf.Max(0.01f, diggingParameter.floorSpeedDivisor)
                    : 0f;

    [Header("移動食い込み中の接触攻撃")]
    [Tooltip("床・壁・天井への食い込み移動で敵に触れたときの、食い込み攻撃力に対する倍率。0.3なら30%。")]
    [SerializeField, Range(0f, 1f)] private float movementContactDamageMultiplier = 0.3f;

    // 物理衝突を無視するボーナス中も検出できるよう、Overlapで接触を調べる。
    // 一度触れた敵は離れるまで再攻撃しない。
    private readonly HashSet<EnemyAttackHitReceiver> touchingEnemies = new HashSet<EnemyAttackHitReceiver>();
    private readonly HashSet<EnemyAttackHitReceiver> detectedEnemies = new HashSet<EnemyAttackHitReceiver>();
    private readonly List<Collider> movementBodyColliders = new List<Collider>();
    private Collider[] movementOverlapBuffer = new Collider[32];
    private Rigidbody movementBody;
    private readonly List<SensorChainsawContact> movementSweepContacts =
        new List<SensorChainsawContact>();
    private readonly HashSet<EnemyAttackHitReceiver> normalMovementHitEnemies =
        new HashSet<EnemyAttackHitReceiver>();
    private readonly List<EnemyAttackHitReceiver> pendingMovementCollisionEnemies =
        new List<EnemyAttackHitReceiver>();
    private readonly HashSet<EnemyAttackHitReceiver> queuedMovementCollisionEnemies =
        new HashSet<EnemyAttackHitReceiver>();
    private bool movementCollisionWindow;
    private bool flushingMovementCollisions;

    private SensorChainsawContact contact;
    // 食い込み開始時の敵との位置差。敵の回転ではプレイヤーを振り回さない。
    private Vector3 enemyFollowOffset;
    // 敵が移動しても命中位置を更新できるよう、敵基準の座標を保存する。
    private Vector3 enemyLocalHitPoint;
    // 同じ食い込み中に押し出しを繰り返し、敵と追従中のプレイヤーを加速させない。
    private bool enemyPushApplied;

    // 継続中の敵に対して、開始時の位置差を保つプレイヤーの追従先を返す。
    public bool TryGetEnemyFollowPosition(out Vector3 position)
    {
        position = default;

        if (!isActiveAndEnabled ||
            !IsRequested ||
            Surface != ChainsawSurface.Enemy ||
            !SensorChainsawContactDetector.CanReceiveEnemyHit(contact.Enemy))
        {
            return false;
        }

        position = contact.Enemy.transform.position + enemyFollowOffset;
        return true;
    }
    private float timer;
    private float evadeUntil;
    private bool holdBlocked;
    private float pendingDash;

    private float lastPressTime = float.NegativeInfinity;

    private bool hasPendingBounce;
    private float pendingBounceDirection;
    // 空中で食い込みを入力し、最初の接触を待っている状態。
    private bool isWaitingForContact;

    // Floor → Wallの切り替え直後に一時的に保持するWall候補。
    private Collider pendingWallCollider;
    private float pendingWallTime;
    public int TerrainLayerMask =>
    detector != null ? detector.TerrainLayerMask : 0;

    // 天井の接触情報を検出器から取得する。
    public bool TryGetCeilingContact(
        out SensorChainsawContact ceilingContact)
    {
        ceilingContact = default;

        return isActiveAndEnabled &&
            detector != null &&
            detector.TryGetCeilingContact(out ceilingContact);
    }

    // 必要な参照を自動取得し、設定アセットが未登録なら処理を止める。
    private void Awake()
    {
        if (diggingParameter == null)
        {
            Debug.LogError("Digging Parameterを設定してください。", this);
            enabled = false;
            return;
        }

        if (detector == null)
        {
            detector = GetComponent<SensorChainsawContactDetector>();
        }

        if (accelerator == null)
        {
            accelerator = GetComponentInChildren<ChainsawAccelerator>();
        }

        if (playerAnimator == null)
        {
            playerAnimator = GetComponent<PlayerAnimator>();
        }

        if (chainsawAttack == null)
        {
            chainsawAttack = GetComponentInChildren<ChainsawAttack>();
        }

        movementBody = GetComponent<Rigidbody>();

        if (detector == null ||
            accelerator == null ||
            playerAnimator == null ||
            chainsawAttack == null)
        {
            Debug.LogError(
                "ChainsawDiggingのDetector / Accelerator / " +
                "PlayerAnimator / ChainsawAttackを設定してください。",
                this
            );

            enabled = false;
        }
    }

    // PlayerControllerから呼び、操作モードに応じて食い込み要求を更新する。
    // 操作モードと接地状態に応じて、食い込み要求を更新する。
    public void HandleInput(
        bool pressed,
        bool held,
        bool attacking,
        float facingDirection = 1f,
        bool isGrounded = true)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (pressed)
        {
            lastPressTime = Time.time;
        }

        if (!held)
        {
            holdBlocked = false;
        }

        // 攻撃中は食い込みを開始しない。
        if (attacking)
        {
            if (IsRequested || IsDigging)
            {
                Cancel(true);
            }

            if (held)
            {
                holdBlocked = true;
            }

            return;
        }

        if (diggingParameter.inputMode == InputMode.Toggle)
        {
            if (!pressed)
            {
                return;
            }

            if (IsRequested)
            {
                Cancel(false);
            }
            else
            {
                BeginRequest(facingDirection, !isGrounded);
            }
        }
        else
        {
            bool requested =
                held &&
                !holdBlocked;

            if (!requested && IsRequested)
            {
                Cancel(false);
            }

            if (pressed && requested && !IsRequested)
            {
                BeginRequest(facingDirection, !isGrounded);
            }
        }
    }

    // 食い込み判定に使用するプレイヤーの向きを更新する。
    public void SetFacingDirection(float direction)
    {
        if (detector != null) detector.SetFacingDirection(direction);
    }

    // 入力時に接触を確認し、接触中ならその場で成立、空中なら接触待機を開始する。
    private void BeginRequest(float facingDirection, bool waitForContact)
    {
        detector.SetFacingDirection(facingDirection);
        // 天井専用センサーの接触を優先し、それ以外は既存の面選択を使用する。
        bool hasContact = detector.TryGetCeilingContact(out SensorChainsawContact initialContact);
        if (!hasContact)
        {
            hasContact = detector.TryGetContact(null, out initialContact,
                ChainsawSurface.None, facingDirection);
        }

        contact = initialContact;
        Surface = ChainsawSurface.None;
        timer = 0f;
        wallContactLostTime = 0f;
        IsRequested = hasContact || waitForContact;
        isWaitingForContact = !hasContact && waitForContact;

        if (hasContact)
        {
            BeginDigging();
            return;
        }

        if (!playerAnimator.StartDiggingAnimation())
        {
            Cancel(true);
            return;
        }

        if (waitForContact)
        {
            // 空中用イベントの姿勢まで再生する。接触検出は直ちに開始する。
            playerAnimator.ResumeDiggingAnimation();
        }
        else
        {
            // 地上で対象がなければ空振りモーションを最後まで再生する。
            holdBlocked = true;
            playerAnimator.ReleaseDiggingAnimation();
        }
    }

    // 床に食い込んでいる場合だけ、床用の姿勢でアニメーションを止める。
    public void OnFloorDiggingFrame() => HoldSurfaceAnimation(ChainsawSurface.Floor);

    // 壁に食い込んでいる場合だけ、壁用の姿勢でアニメーションを止める。
    public void OnWallDiggingFrame() => HoldSurfaceAnimation(ChainsawSurface.Wall);

    // 天井に食い込んでいる場合だけ、天井用の姿勢でアニメーションを止める。
    public void OnCeilingDiggingFrame() => HoldSurfaceAnimation(ChainsawSurface.Ceiling);

    // 敵に食い込んでいる場合だけ、敵用の姿勢でアニメーションを止める。
    public void OnEnemyDiggingFrame() => HoldSurfaceAnimation(ChainsawSurface.Enemy);

    // 未接触の空中待機中だけ、空中用の姿勢でアニメーションを止める。
    public void OnAirDiggingFrame()
    {
        if (isActiveAndEnabled && IsRequested && isWaitingForContact && !IsDigging &&
            playerAnimator != null && playerAnimator.IsDiggingAnimationActive)
        {
            playerAnimator.HoldDiggingAnimationImmediately();
        }
    }

    // 旧イベント名は床の姿勢停止として受け付ける。
    public void OnDiggingContactFrame() => OnFloorDiggingFrame();

    // 現在食い込んでいる面と一致するイベントだけで姿勢を止める。
    private void HoldSurfaceAnimation(ChainsawSurface surface)
    {
        if (isActiveAndEnabled && IsRequested && Surface == surface &&
            playerAnimator != null && playerAnimator.IsDiggingAnimationActive)
        {
            playerAnimator.HoldDiggingAnimationImmediately();
        }
    }

    // PlayerControllerのFixedUpdateから呼び、接触成立・継続・消費を処理する。
    public void Tick(
    float deltaTime,
    float facingDirection = 0f,
    float wallImpactMinimumSpeed = float.PositiveInfinity,
    float floorMoveSpeed = 0f,
    int wallImpactLayers = 0)
    {
        // 閾値を下回ったら、古い回避時間と未実行のボーナスダッシュを持ち越さない。
        // 食い込み要求がない場合も消去し、再加速だけで古い回避が復活するのを防ぐ。
        if (!MeetsBonusAcceleratorThreshold)
        {
            evadeUntil = 0f;
            pendingDash = 0f;
        }

        if (!isActiveAndEnabled || !IsRequested)
        {
            return;
        }

        bool foundContact;
        SensorChainsawContact next;

        if (Surface == ChainsawSurface.Enemy)
        {
            // 一度開始した敵は再検出しない。範囲外や他の敵への切り替えで解除しない。
            if (!SensorChainsawContactDetector.CanReceiveEnemyHit(contact.Enemy))
            {
                Cancel(false);
                return;
            }

            next = contact;
            next.Point = contact.Enemy.transform.TransformPoint(enemyLocalHitPoint);
            foundContact = true;
        }
        else
        {
            // 地形と最初の接触は従来のセンサー判定を使用する。
            foundContact = detector.TryGetContact(
                contact.Collider,
                out next,
                Surface,
                facingDirection);
        }

        // 前方の敵を検出した場合は、
        // 壁の接触猶予より敵への遷移を優先する。
        bool foundEnemy =
            foundContact &&
            next.Surface == ChainsawSurface.Enemy;

        if (Surface == ChainsawSurface.Wall && !foundEnemy)
        {
            bool foundWall =
                foundContact &&
                next.Surface == ChainsawSurface.Wall;

            if (foundWall)
            {
                wallContactLostTime = 0f;
            }
            else
            {
                wallContactLostTime += deltaTime;

                if (wallContactLostTime >= diggingParameter.wallContactGraceTime)
                {
                    Debug.Log(
                        "[壁登り終了] 壁を見失って猶予時間が経過",
                        this);

                    Cancel(false);
                    return;
                }

                // 接触が切れても、直前の壁情報で上昇を続ける。
                next = contact;
            }
        }
        else
        {
            wallContactLostTime = 0f;

            if (!foundContact)
            {
                // 空中入力後の最初の接触待ちでは、要求とモーションを維持する。
                // 接触後に対象を見失った場合は、従来どおり解除する。
                if (!isWaitingForContact)
                {
                    Cancel(false);
                }

                return;
            }
        }

        if (IsDigging &&
            (
                next.Surface != Surface ||
                (
                    Surface == ChainsawSurface.Enemy &&
                    next.Enemy != contact.Enemy
                )
            ))
        {
            bool isFloorToWall =
                Surface == ChainsawSurface.Floor &&
                next.Surface == ChainsawSurface.Wall;

            if (isFloorToWall)
            {
                bool isFrontWall =
                    facingDirection * next.Normal.x < -0.1f;

                bool timedPress =
                    Time.time - lastPressTime <= diggingParameter.wallClimbInputWindow;

                if (isFrontWall && !timedPress)
                {
                    // 高速時はRayだけでのけぞらず、本体の衝突まで待つ。
                    Vector3 floorNormal = contact.Normal;

                    Vector3 floorTangent = new Vector3(
                        floorNormal.y,
                        -floorNormal.x,
                        0f
                    ).normalized;

                    if (floorTangent.x < 0f)
                    {
                        floorTangent = -floorTangent;
                    }

                    float approachSpeed = -Vector3.Dot(
                        floorTangent * floorMoveSpeed,
                        next.Normal
                    );

                    bool impactLayer =
                        (wallImpactLayers &
                         (1 << next.Collider.gameObject.layer)) != 0;

                    if (impactLayer && approachSpeed >= wallImpactMinimumSpeed)
                    {
                        pendingWallCollider = null;
                        pendingWallTime = 0f;
                        return;
                    }
                }

                // 壁登り条件を満たしていれば、
                // 食い込み要求を維持したまま壁へ切り替える。
                contact = next;
                Surface = ChainsawSurface.None;

                timer = 0f;

                // 床用の未使用ダッシュを持ち越さない。
                pendingDash = 0f;

                pendingWallCollider = null;
                pendingWallTime = 0f;

                if (!BeginDigging())
                {
                    return;
                }

                Debug.Log(
                    $"[チェーンソー食い込み] " +
                    $"地面 → 壁へ切り替え：" +
                    $"{contact.Collider.name}",
                    contact.Collider);
            }
            else if (next.Surface == ChainsawSurface.Enemy &&
                     Surface != ChainsawSurface.Enemy)
            {
                // 判定範囲内で検出した敵へ、
                // 食い込み要求を維持したまま切り替える。
                contact = next;
                Surface = ChainsawSurface.None;

                timer = 0f;
                pendingDash = 0f;

                pendingWallCollider = null;
                pendingWallTime = 0f;
                wallContactLostTime = 0f;

                if (!BeginDigging())
                {
                    return;
                }
            }
            else
            {
                // 上記以外の切り替えは、既存の解除処理を使う。
                pendingWallCollider = null;
                pendingWallTime = 0f;

                Cancel(false);
                return;
            }
        }
        else
        {
            // 壁候補がなくなった場合は待機状態をリセットする。
            if (next.Surface != ChainsawSurface.Wall)
            {
                pendingWallCollider = null;
                pendingWallTime = 0f;
            }
        }

        // 同じ種類の地形の継ぎ目は状態を維持して更新する。
        contact = next;

        if (!IsDigging && !BeginDigging())
        {
            return;
        }

        timer += deltaTime;
        // 敵への食い込み中、指定間隔ごとにリソースを消費して攻撃する。
        if (Surface == ChainsawSurface.Enemy)
        {
            float interval =
                Mathf.Max(0.01f, diggingParameter.enemyAttackInterval);

            while (timer >= interval)
            {
                timer -= interval;

                // 検出時と同じ条件で、敵が食い込みを受けられる状態か確認する。
                if (!SensorChainsawContactDetector.CanReceiveEnemyHit(contact.Enemy))
                {
                    Cancel(false);
                    return;
                }

                float speedRatio =
                    Mathf.Clamp01(accelerator.SpeedRatio);

                float damageMultiplier =
                    Mathf.Lerp(
                        diggingParameter.minDamageMultiplier,
                        diggingParameter.maxDamageMultiplier,
                        speedRatio
                    );

                float consumeAmount =
                    HasBonus
                        ? diggingParameter.bonusEnemyConsumeAmount
                        : diggingParameter.enemyConsumeAmount;

                if (!accelerator.TryConsume(consumeAmount))
                {
                    // リソース不足でも接続は保持し、今回のダメージだけを停止する。
                    // 回復後の一括攻撃を防ぐため、未実行の間隔を持ち越さない。
                    timer = 0f;
                    return;
                }

                bool hit = chainsawAttack.HitDigging(
                    contact.Enemy,
                    diggingAttackData,
                    damageMultiplier *
                        (HasBonus
                            ? diggingParameter.bonusDamageMultiplier
                            : 1f),
                    contact.Point
                );

                if (!hit)
                {
                    // 一時的な送信失敗で接続を解除せず、次の攻撃間隔で再試行する。
                    timer = 0f;
                    return;
                }

                // 今回の攻撃で死亡・削除された場合、押し出し前に接続を終了する。
                if (!SensorChainsawContactDetector.CanReceiveEnemyHit(contact.Enemy))
                {
                    Cancel(false);
                    return;
                }

                // 押し出し済みなら、以降はダメージだけを継続する。
                if (enemyPushApplied)
                {
                    continue;
                }

                // 最初の命中時だけ、敵のRigidbodyへ押し出しを加える。
                Rigidbody enemyRigidbody =
                    contact.Enemy.GetComponent<Rigidbody>();

                if (enemyRigidbody == null)
                {
                    enemyRigidbody =
                        contact.Enemy.GetComponentInParent<Rigidbody>();
                }

                if (enemyRigidbody == null)
                {
                    enemyRigidbody =
                        contact.Enemy.GetComponentInChildren<Rigidbody>();
                }

                float direction =
                    Mathf.Abs(facingDirection) > 0.01f
                        ? Mathf.Sign(facingDirection)
                        : 1f;

                if (enemyRigidbody != null &&
                    !enemyRigidbody.isKinematic &&
                    diggingParameter.enemyPushPower > 0f)
                {
                    enemyPushApplied = true;
                    enemyRigidbody.AddForce(
                        Vector3.right *
                            direction *
                            diggingParameter.enemyPushPower,
                        ForceMode.Impulse
                    );
                }
            }

            return;
        }

        if (Surface == ChainsawSurface.Wall)
        {
            float interval =
                Mathf.Max(0.01f, diggingParameter.wallConsumeInterval);

            while (timer >= interval)
            {
                timer -= interval;

                if (!accelerator.TryConsume(diggingParameter.wallConsumeAmount))
                {
                    Cancel(false);
                    return;
                }
            }
        }
    }
    // 保留していた壁反発の向きを取得して消費する。
    public bool TryTakeWallBounce(
        out float direction)
    {
        direction =
            pendingBounceDirection;

        bool result =
            hasPendingBounce;

        hasPendingBounce = false;
        pendingBounceDirection = 0f;

        return result;
    }

    // 食い込み対象と接触情報をログに出力する。
    private void LogDiggingContact()
    {
        string surfaceName;

        switch (Surface)
        {
            case ChainsawSurface.Floor:
                surfaceName = "地面";
                break;

            case ChainsawSurface.Wall:
                surfaceName = "壁";
                break;

            case ChainsawSurface.Ceiling:
                surfaceName = "天井";
                break;

            case ChainsawSurface.Enemy:
                surfaceName = "敵";
                break;

            default:
                return;
        }

        string targetName =
            contact.Collider != null
                ? contact.Collider.gameObject.name
                : "不明";

        Debug.Log(
            $"[チェーンソー食い込み] " +
            $"種類：{surfaceName} | " +
            $"対象：{targetName} | " +
            $"接触位置：{contact.Point.ToString("F2")} | " +
            $"面の向き：{contact.Normal.ToString("F2")} | " +
            $"ボーナス：{HasBonus}",
            contact.Collider != null
                ? (Object)contact.Collider
                : this);
    }

    // 接触面に応じて食い込み状態とボーナスを初期化する。
    private bool BeginDigging()
    {
        if (contact.Surface ==
            ChainsawSurface.Enemy &&
            diggingAttackData == null)
        {
            Debug.LogError(
                "敵用のDigging Attack Dataを設定してください。",
                this
            );

            Cancel(false);
            return false;
        }

        // ボーナスはHasBonusが現在のアクセルから判定するため、開始時の値を固定しない。
        Surface =
            contact.Surface;

        // 地面→壁の切り替え時は呼び出し元が一旦Noneにするため、
        // 壁へ食い込み成立した時点で必ず進入バージョンを更新する。
        if (Surface == ChainsawSurface.Wall)
        {
            unchecked { WallEntryVersion++; }
        }

        timer = 0f;
        ceilingBonusRamp = 0f;

        // 成立した面のモーションを開始し、面ごとのAnimation Event位置まで再生する。
        if (!playerAnimator.StartDiggingAnimation(Surface))
        {
            Cancel(true);
            return false;
        }
        // 再生後は各On～DiggingFrameだけで止め、成立時点で早期固定しない。
        playerAnimator.ResumeDiggingAnimation();

        if (Surface ==
            ChainsawSurface.Enemy)
        {
            // 開始した敵と位置差を保持し、被弾による移動にも追従する。
            if (!SensorChainsawContactDetector.CanReceiveEnemyHit(contact.Enemy))
            {
                Cancel(false);
                return false;
            }

            enemyFollowOffset = transform.position - contact.Enemy.transform.position;
            enemyLocalHitPoint = contact.Enemy.transform.InverseTransformPoint(contact.Point);
            enemyPushApplied = false;
        }

        // 敵以外の移動食い込みでも接触攻撃を送信できるようにする。
        chainsawAttack.BeginDigging();

        if (Surface ==
            ChainsawSurface.Floor)
        {
            Debug.Log(
                $"[食い込み] 地面への食い込み開始！ " +
                $"回転速度：" +
                $"{accelerator.CurrentSpeed:F1} " +
                $"ボーナス：{HasBonus}",
                this
            );

            if (HasBonus)
            {
                GrantEvade(
                    diggingParameter.floorEvadeTime);

                pendingDash =
                    diggingParameter.floorDashPower;
            }
        }

        if (HasBonus &&
            Surface ==
            ChainsawSurface.Ceiling)
        {
            GrantEvade(
                diggingParameter.ceilingEvadeTime);

            pendingDash =
                diggingParameter.ceilingDashPower;
        }

        // 接触後は面別イベントで姿勢を止める。固定後に毎フレーム再開しない。
        isWaitingForContact = false;

        LogDiggingContact();

        return true;
    }

    // 保留しているダッシュ加算値を取得して消費する。
    public bool TryTakeDash(out float power)
    {
        power = pendingDash;
        pendingDash = 0f;

        hasPendingBounce = false;
        pendingBounceDirection = 0f;

        // 壁判定の待機状態はTick / Cancelで管理する。
        // 毎FixedUpdate呼ばれるここでは、
        // pendingWallColliderとpendingWallTimeをリセットしない。

        return power > 0f;
    }

    // 壁ジャンプの強さを取得し、必要に応じて回避時間を設定する。
    public bool TryGetWallJump(
        out float power)
    {
        power = 0f;

        if (Surface !=
            ChainsawSurface.Wall)
        {
            return false;
        }

        power =
            HasBonus
                ? diggingParameter.bonusWallJumpPower
                : diggingParameter.wallJumpPower;

        if (HasBonus)
        {
            GrantEvade(
                diggingParameter.wallEvadeTime);
        }

        return true;
    }

    // 指定時間まで回避判定を延長する。
    private void GrantEvade(
        float duration)
    {
        evadeUntil =
            Mathf.Max(
                evadeUntil,
                Time.time + duration);
    }

    /// <summary>
    /// 移動食い込み中、プレイヤー本体と重なる敵に一度だけ30%接触攻撃を送る。
    /// Physics.IgnoreCollision中もOverlapBoxで検出可能なため、ボーナス貫通に対応。
    /// 敵からプレイヤーへのダメージは既存の敵攻撃とPlayerAttackHitReceiverに委ねる。
    /// </summary>
    private void ProcessMovementEnemyContacts(Vector3 displacement)
    {
        if (!IsMovementDigging || movementBody == null || detector == null ||
            chainsawAttack == null || diggingAttackData == null)
        {
            touchingEnemies.Clear();
            detectedEnemies.Clear();
            return;
        }

        detectedEnemies.Clear();
        movementBody.GetComponentsInChildren<Collider>(true, movementBodyColliders);
        int enemyMask = detector.EnemyLayerMask;
        if (enemyMask == 0) return;

        foreach (Collider bodyCollider in movementBodyColliders)
        {
            if (bodyCollider == null || !bodyCollider.enabled || bodyCollider.isTrigger ||
                !bodyCollider.gameObject.activeInHierarchy ||
                bodyCollider.attachedRigidbody != movementBody)
            {
                continue;
            }

            Bounds bounds = bodyCollider.bounds;
            int count;
            while (true)
            {
                count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents,
                    movementOverlapBuffer, Quaternion.identity, enemyMask,
                    QueryTriggerInteraction.Ignore);
                if (count < movementOverlapBuffer.Length) break;
                System.Array.Resize(ref movementOverlapBuffer, movementOverlapBuffer.Length * 2);
            }

            for (int i = 0; i < count; i++)
            {
                Collider enemyCollider = movementOverlapBuffer[i];
                if (enemyCollider == null || enemyCollider.attachedRigidbody == movementBody)
                    continue;

                EnemyAttackHitReceiver enemy = enemyCollider.GetComponentInParent<EnemyAttackHitReceiver>();
                if (!SensorChainsawContactDetector.CanReceiveEnemyHit(enemy)) continue;
                if (!Physics.ComputePenetration(bodyCollider,
                        bodyCollider.transform.position, bodyCollider.transform.rotation,
                        enemyCollider, enemyCollider.transform.position, enemyCollider.transform.rotation,
                        out _, out _))
                    continue;

                detectedEnemies.Add(enemy);
                if (touchingEnemies.Contains(enemy)) continue;

                // 同一物理ステップで複数Colliderに接触しても二重命中しない。
                touchingEnemies.Add(enemy);
                chainsawAttack.HitMovementContact(enemy, diggingAttackData,
                    movementContactDamageMultiplier,
                    enemyCollider.ClosestPoint(bounds.center));
            }
        }

        // 高速の貫通で、物理更新時点の重なりを飛び越す敵も今回の移動経路から検出する。
        detector.CollectMovementEnemyContacts(movementSweepContacts, displacement, contact.Normal);
        for (int i = 0; i < movementSweepContacts.Count; i++)
        {
            if (!IsMovementDigging || !IsRequested) break;
            SensorChainsawContact candidate = movementSweepContacts[i];
            if (!SensorChainsawContactDetector.CanReceiveEnemyHit(candidate.Enemy)) continue;
            detectedEnemies.Add(candidate.Enemy);
            if (!touchingEnemies.Add(candidate.Enemy)) continue;
            chainsawAttack.HitMovementContact(candidate.Enemy, diggingAttackData,
                movementContactDamageMultiplier, candidate.Point);
        }
        touchingEnemies.RemoveWhere(enemy => enemy == null || !detectedEnemies.Contains(enemy));
    }

    // Controllerの移動決定後に、通常時の本体衝突受付とボーナス時の貫通攻撃を切り替える。
    public void HitMovementEnemies(Vector3 displacement)
    {
        movementCollisionWindow = false;
        if (!IsMovementDigging || !IsRequested || Time.timeScale <= 0f ||
            chainsawAttack == null || diggingAttackData == null) return;
        if (!HasBonus)
        {
            // この物理ステップの状態を保存し、被弾で解除された後の衝突通知も受け付ける。
            movementCollisionWindow = true;
            return;
        }
        ProcessMovementEnemyContacts(displacement);
    }

    // 通常食い込み中の実際の本体衝突を記録し、敵の接触攻撃より先にやられ状態へ変えない。
    public void QueueMovementEnemyCollision(Collision collision)
    {
        if (!movementCollisionWindow || collision == null || collision.collider == null) return;
        EnemyAttackHitReceiver enemy =
            collision.collider.GetComponentInParent<EnemyAttackHitReceiver>();
        if (!SensorChainsawContactDetector.CanReceiveEnemyHit(enemy) ||
            normalMovementHitEnemies.Contains(enemy) || !queuedMovementCollisionEnemies.Add(enemy))
            return;
        normalMovementHitEnemies.Add(enemy);
        pendingMovementCollisionEnemies.Add(enemy);
    }

    // 衝突通知が終わった次の物理更新で、成立済みの接触攻撃を送信する。
    public void FlushMovementCollisionHits()
    {
        movementCollisionWindow = false;
        if (flushingMovementCollisions) return;
        flushingMovementCollisions = true;
        try
        {
            if (chainsawAttack == null || diggingAttackData == null) return;
            for (int i = 0; i < pendingMovementCollisionEnemies.Count; i++)
            {
                EnemyAttackHitReceiver enemy = pendingMovementCollisionEnemies[i];
                if (!SensorChainsawContactDetector.CanReceiveEnemyHit(enemy)) continue;
                chainsawAttack.HitMovementContact(enemy, diggingAttackData,
                    movementContactDamageMultiplier,
                    enemy.transform.position);
            }
        }
        finally
        {
            pendingMovementCollisionEnemies.Clear();
            queuedMovementCollisionEnemies.Clear();
            flushingMovementCollisions = false;
        }
    }

    // 食い込みの状態を解除し、アニメーションと攻撃状態を終了する。
    public void Cancel(
        bool immediate)
    {
        // 被弾で解除されても、直前の物理衝突で成立した命中は次の物理更新まで残す。
        normalMovementHitEnemies.Clear();
        // 攻撃・ジャンプ・無効化でも、接触待ちを残さない。
        isWaitingForContact = false;
        IsRequested = false;
        Surface =
            ChainsawSurface.None;

        touchingEnemies.Clear();
        detectedEnemies.Clear();
        contact = default;

        // 次の食い込みへ、前の敵との位置情報を持ち越さない。
        enemyFollowOffset = Vector3.zero;
        enemyLocalHitPoint = Vector3.zero;
        enemyPushApplied = false;

        timer = 0f;
        ceilingBonusRamp = 0f;
        pendingDash = 0f;
        holdBlocked = true;

        // 保留中のFloor → Wall判定をリセット。
        pendingWallCollider = null;
        pendingWallTime = 0f;

        if (chainsawAttack != null)
        {
            chainsawAttack.EndDigging();
        }

        if (playerAnimator == null)
        {
            return;
        }

        if (immediate)
        {
            playerAnimator
                .ExitDiggingImmediately();
        }
        else if (playerAnimator
            .IsDiggingAnimationActive)
        {
            // 未接触の構え中でも、
            // 解除時は続きを再生する。
            playerAnimator
                .ReleaseDiggingAnimation();
        }

        // 回避時間は、アクセルが80%以上である間だけ解除後も指定時間まで利用する。
    }

    // コンポーネント停止時に食い込みと回避状態を解除する。
    private void OnDisable()
    {
        Cancel(true);
        evadeUntil = 0f;
        movementCollisionWindow = false;
        pendingMovementCollisionEnemies.Clear();
        queuedMovementCollisionEnemies.Clear();
    }
}
