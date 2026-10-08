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

    public bool HasBonus { get; private set; }

    public bool IsEvading =>
        isActiveAndEnabled &&
        Time.time < evadeUntil;

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

    private SensorChainsawContact contact;
    private float timer;
    private float evadeUntil;
    private bool holdBlocked;
    private float pendingDash;

    private float lastPressTime = float.NegativeInfinity;

    private bool hasPendingBounce;
    private float pendingBounceDirection;

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
    public void HandleInput(
        bool pressed,
        bool held,
        bool attacking,
        float facingDirection = 1f)
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
                BeginRequest(facingDirection);
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
                BeginRequest(facingDirection);
            }
        }
    }

    // 食い込み判定に使用するプレイヤーの向きを更新する。
    public void SetFacingDirection(float direction)
    {
        if (detector != null) detector.SetFacingDirection(direction);
    }

    // 入力時の接触を確認して食い込み要求を開始する。
    private void BeginRequest(float facingDirection)
    {
        // 押した時点で3つの判定範囲に対象がある場合だけ要求を開始する。
        // 空振りした入力を保持して、後から自動で食い込むことはしない。
        if (!detector.TryGetContact(null, out SensorChainsawContact initialContact,
                ChainsawSurface.None, facingDirection))
        {
            holdBlocked = true;
            return;
        }
        if (!playerAnimator.StartDiggingAnimation(initialContact.Surface))
        {
            Cancel(true);
            return;
        }
        contact = initialContact;
        IsRequested = true;
        wallContactLostTime = 0f;
    }

    // PlayerControllerのFixedUpdateから呼び、接触状態の更新と消費処理を行う。
    public void Tick(
    float deltaTime,
    float facingDirection = 0f,
    float wallImpactMinimumSpeed = float.PositiveInfinity,
    float floorMoveSpeed = 0f,
    int wallImpactLayers = 0)
    {
        if (!isActiveAndEnabled || !IsRequested)
        {
            return;
        }

        bool foundContact = detector.TryGetContact(
            contact.Collider,
            out SensorChainsawContact next,
            Surface,
            facingDirection);

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
                Cancel(false);
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

        // 接触成立時の回転速度で判定し、
        // 終了まで保持。
        HasBonus =
            accelerator.SpeedRatio >=
            diggingParameter.bonusThreshold;

        Surface =
            contact.Surface;

        timer = 0f;
        ceilingBonusRamp = 0f;

        // 新規開始は、判定されたSurfaceのアニメーションを使う。
        // 既に再生中のときの継続・天井停止処理は維持する。
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

    // 食い込みの状態を解除し、アニメーションと攻撃状態を終了する。
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

    // コンポーネント停止時に食い込みと回避状態を解除する。
    private void OnDisable()
    {
        Cancel(true);
        evadeUntil = 0f;
    }
}
