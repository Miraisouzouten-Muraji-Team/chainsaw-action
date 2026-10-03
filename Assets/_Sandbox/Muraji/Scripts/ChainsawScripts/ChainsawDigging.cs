using UnityEngine;

public class ChainsawDigging : MonoBehaviour
{
    public enum InputMode { Toggle, Hold }

    [Header("参照（Player上の参照は未設定なら自動取得）")]
    [Tooltip("接触先の判定")]
    [SerializeField] private ChainsawContactDetector detector;

    [Tooltip("回転速度の管理")]
    [SerializeField] private ChainsawAccelerator accelerator;

    [Tooltip("アニメーション制御")]
    [SerializeField] private PlayerAnimator playerAnimator;

    [Tooltip("攻撃処理")]
    [SerializeField] private ChainsawAttack chainsawAttack;

    [Tooltip("敵への食い込み攻撃データ")]
    [SerializeField] private AttackData diggingAttackData;

    [Header("操作と開始ボーナス")]
    [Tooltip("Toggle：押すたび切替／Hold：長押し")]
    [SerializeField] private InputMode inputMode = InputMode.Toggle;

    [Tooltip("開始ボーナスに必要な回転速度の割合")]
    [SerializeField, Range(0f, 1f)] private float bonusThreshold = 0.8f;

    [Header("床")]
    [Tooltip("速度加算＝回転速度÷この値")]
    [SerializeField, Min(0.01f)] private float floorSpeedDivisor = 5f;

    [Tooltip("ボーナス時のダッシュ加算速度")]
    [SerializeField, Min(0f)] private float floorDashPower = 10f;

    [Tooltip("ボーナス時の回避時間（秒）")]
    [SerializeField, Min(0f)] private float floorEvadeTime = 0.5f;

    [Header("壁")]
    [Tooltip("回転リソースの消費間隔（秒）")]
    [SerializeField, Min(0.01f)] private float wallConsumeInterval = 0.1f;

    [Tooltip("1回あたりの消費量")]
    [SerializeField, Min(0f)] private float wallConsumeAmount = 20f;

    [Tooltip("通常の壁ジャンプ速度")]
    [SerializeField, Min(0f)] private float wallJumpPower = 10f;

    [Tooltip("ボーナス時の壁ジャンプ速度")]
    [SerializeField, Min(0f)] private float bonusWallJumpPower = 15f;

    [Tooltip("ボーナス壁ジャンプの回避時間（秒）")]
    [SerializeField, Min(0f)] private float wallEvadeTime = 0.25f;

    [Tooltip("壁を見失っても上昇を続ける時間（秒）")]
    [SerializeField, Min(0f)]
    private float wallContactGraceTime = 2f;

    [Header("天井")]
    [Tooltip("速度加算＝回転速度÷この値")]
    [SerializeField, Min(0.01f)] private float ceilingSpeedDivisor = 7.5f;

    [Tooltip("ボーナス時のダッシュ加算速度")]
    [SerializeField, Min(0f)] private float ceilingDashPower = 7.5f;


    [Tooltip("ボーナス時の回避時間（秒）")]
    [SerializeField, Min(0f)] private float ceilingEvadeTime = 0.35f;

    [Header("敵：消費は攻撃1回ごと")]
    [Tooltip("連続攻撃の間隔（秒）")]
    [SerializeField, Min(0.01f)] private float enemyAttackInterval = 0.1f;

    [Tooltip("通常の攻撃1回の消費量")]
    [SerializeField, Min(0f)] private float enemyConsumeAmount = 1f;

    [Tooltip("ボーナス時の攻撃1回の消費量")]
    [SerializeField, Min(0f)] private float bonusEnemyConsumeAmount = 2f;

    [Tooltip("PowerRatioが0のときのダメージ倍率")]
    [SerializeField, Min(0f)] private float minDamageMultiplier = 0.9f;

    [Tooltip("PowerRatioが1のときのダメージ倍率")]
    [SerializeField, Min(0f)] private float maxDamageMultiplier = 1.25f;

    [Tooltip("ボーナス時に追加で掛ける倍率")]
    [SerializeField, Min(0f)] private float bonusDamageMultiplier = 1.5f;

    private float wallContactLostTime;
    private float ceilingBonusRamp;

    public bool IsRequested { get; private set; }

    public bool IsDigging =>
        Surface != ChainsawSurface.None;

    public ChainsawSurface Surface { get; private set; }

    public bool HasBonus { get; private set; }

    public bool IsEvading =>
        isActiveAndEnabled &&
        Time.time < evadeUntil;

    public bool SuppressGravity =>
        IsDigging &&
        (
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
                  Mathf.Max(0.01f, floorSpeedDivisor)
                : Surface == ChainsawSurface.Ceiling
                    ? accelerator.CurrentSpeed /
                      Mathf.Max(0.01f, floorSpeedDivisor)
                    : 0f;

    private ChainsawContact contact;
    private float timer;
    private float evadeUntil;
    private bool holdBlocked;
    private float pendingDash;

    [Header("床から壁への切り替え")]
    [Tooltip("壁を検出する直前に押していれば、壁登りになる猶予時間（秒）")]
    [SerializeField, Min(0f)]
    private float wallClimbInputWindow = 0.2f;

    [Tooltip("床Colliderの切り替え時に一瞬だけ発生するWall判定を無視する時間")]
    [SerializeField, Min(0f)]
    private float floorTransitionWallGraceTime = 0.05f;

    private float lastPressTime = float.NegativeInfinity;

    private bool hasPendingBounce;
    private float pendingBounceDirection;

    // Floor → Wallの切り替え直後に一時的に保持するWall候補。
    private Collider pendingWallCollider;
    private float pendingWallTime;

    private void Awake()
    {
        if (detector == null)
        {
            detector = GetComponent<ChainsawContactDetector>();
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

    // PlayerControllerのUpdateから呼ぶ。
    public void HandleInput(
        bool pressed,
        bool held,
        bool attacking)
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

        if (inputMode == InputMode.Toggle)
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
                BeginRequest();
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

            if (requested && !IsRequested)
            {
                BeginRequest();
            }
        }
    }

    private void BeginRequest()
    {
        // 接触していなくても、入力時点でアニメーションを開始。
        // 移動ボーナスや攻撃は、接触が成立してから開始する。
        if (!playerAnimator.StartDiggingAnimation())
        {
            Cancel(true);
            return;
        }

        IsRequested = true;
    }

    // PlayerControllerのFixedUpdateから呼ぶ。
    public void Tick(
        float deltaTime,
        float facingDirection = 0f)
    {
        if (!isActiveAndEnabled ||
            !IsRequested)
        {
            return;
        }

        bool foundContact =
            detector.TryGetContact(
                contact.Collider,
                out ChainsawContact next,
                Surface,
                facingDirection);

        if (Surface == ChainsawSurface.Wall)
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

                if (wallContactLostTime >=
                    wallContactGraceTime)
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
                if (IsDigging)
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
                // 進行方向の前方にある壁かどうか。
                bool isFrontWall =
                    facingDirection *
                    next.Normal.x < -0.1f;

                bool timedPress =
                    Time.time -
                    lastPressTime <=
                    wallClimbInputWindow;

                if (isFrontWall && !timedPress)
                {
                    // ------------------------------------------------
                    // Ground1 → Ground2など、床Colliderが切り替わる
                    // 継ぎ目で一瞬だけWallが検出される場合がある。
                    //
                    // その瞬間に即座にBounceさせず、同じWallが
                    // 一定時間継続した場合のみ本物の壁として扱う。
                    // ------------------------------------------------

                    if (pendingWallCollider !=
                        next.Collider)
                    {
                        pendingWallCollider =
                            next.Collider;

                        pendingWallTime =
                            Time.time;
                    }

                    float wallElapsedTime =
                        Time.time -
                        pendingWallTime;

                    if (wallElapsedTime <
                        floorTransitionWallGraceTime)
                    {
                        // 一瞬だけ発生したWall判定なので、
                        // 現在のFloor状態を維持する。
                        return;
                    }

                    // 一定時間同じWallが続いたため、
                    // 本物の壁として後退処理を行う。
                    float bounceDirection =
                        Mathf.Sign(next.Normal.x);

                    // 食い込みを解除する。
                    // Cancel後にBounce予約を設定する。
                    Cancel(true);

                    pendingBounceDirection =
                        bounceDirection;

                    hasPendingBounce = true;

                    pendingWallCollider = null;
                    pendingWallTime = 0f;

                    return;
                }

                // 壁登り入力がある場合は、
                // そのまま壁へ切り替える。
                contact = next;

                Surface =
                    ChainsawSurface.None;

                timer = 0f;

                // 地面用の未使用ダッシュを壁へ持ち越さない。
                pendingDash = 0f;

                // 保留中のWall判定をリセット。
                pendingWallCollider = null;
                pendingWallTime = 0f;

                // 壁用のアニメーションと
                // ボーナス判定を開始する。
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
            else
            {
                // 今回変更するのは地面から壁への切り替え。
                // それ以外は既存の解除処理を使う。
                pendingWallCollider = null;
                pendingWallTime = 0f;

                Cancel(false);
                return;
            }
        }
        else
        {
            // Wall候補がなくなった場合は、
            // 保留中のWall判定をリセットする。
            if (next.Surface !=
                ChainsawSurface.Wall)
            {
                pendingWallCollider = null;
                pendingWallTime = 0f;
            }
        }

        // 同じ種類の地形の継ぎ目は、
        // 食い込み状態を維持して更新。
        contact = next;

        if (!IsDigging &&
            !BeginDigging())
        {
            return;
        }

        timer += deltaTime;

        if (Surface ==
            ChainsawSurface.Wall)
        {
            float interval =
                Mathf.Max(
                    0.01f,
                    wallConsumeInterval);

            while (timer >= interval)
            {
                timer -= interval;

                if (!accelerator.TryConsume(
                    wallConsumeAmount))
                {
                    Cancel(false);
                    return;
                }
            }
        }
    }

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

        // 接触成立時の回転速度で判定し、
        // 終了まで保持。
        HasBonus =
            accelerator.SpeedRatio >=
            bonusThreshold;

        Surface =
            contact.Surface;

        timer = 0f;
        ceilingBonusRamp = 0f;

        // 床では入力時に開始した
        // アニメーションを継続。
        bool keepCurrentAnimation =
            Surface ==
            ChainsawSurface.Ceiling &&
            playerAnimator.IsDiggingAnimationActive;

        bool keepFloorAnimation =
            Surface ==
            ChainsawSurface.Floor &&
            playerAnimator.IsDiggingAnimationActive;

        if (!keepCurrentAnimation &&
            !keepFloorAnimation &&
            !playerAnimator.StartDiggingAnimation(
                Surface))
        {
            Cancel(true);
            return false;
        }

        if (Surface ==
            ChainsawSurface.Ceiling &&
            keepCurrentAnimation)
        {
            playerAnimator
                .HoldDiggingAnimationImmediately();
        }

        if (Surface ==
            ChainsawSurface.Enemy)
        {
            chainsawAttack.BeginDigging();
        }

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
                    floorEvadeTime);

                pendingDash =
                    floorDashPower;
            }
        }

        if (HasBonus &&
            Surface ==
            ChainsawSurface.Ceiling)
        {
            GrantEvade(
                ceilingEvadeTime);

            pendingDash =
                ceilingDashPower;
        }

        LogDiggingContact();

        return true;
    }

    public bool TryTakeDash(
        out float power)
    {
        power =
            pendingDash;

        pendingDash = 0f;

        hasPendingBounce = false;
        pendingBounceDirection = 0f;

        pendingWallCollider = null;
        pendingWallTime = 0f;

        return power > 0f;
    }

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
                ? bonusWallJumpPower
                : wallJumpPower;

        if (HasBonus)
        {
            GrantEvade(
                wallEvadeTime);
        }

        return true;
    }

    private void GrantEvade(
        float duration)
    {
        evadeUntil =
            Mathf.Max(
                evadeUntil,
                Time.time + duration);
    }

    public void Cancel(
        bool immediate)
    {
        IsRequested = false;
        Surface =
            ChainsawSurface.None;

        HasBonus = false;
        contact = default;

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

        // 回避時間は解除後も指定時間まで維持。
    }

    private void OnDisable()
    {
        Cancel(true);
        evadeUntil = 0f;
    }
}
