using UnityEngine;

// 既存Controllerの物理更新後に、公開済みの状態を読み取ります。
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class ChainsawPoseController : MonoBehaviour
{
    [Tooltip("既存のSensorPlayerControllerです。未指定なら同じオブジェクトから取得します。")]
    [SerializeField] private SensorPlayerController player;
    [Tooltip("既存のSensorChainsawDiggingです。未指定なら同じオブジェクトから取得します。")]
    [SerializeField] private SensorChainsawDigging digging;
    [Tooltip("既存ControllerとRigidbodyがあるPlayerです。武器の位置・回転の基準にも使用します。武器はこの直接の子にしてください。")]
    [SerializeField] private Transform playerRoot;
    [Tooltip("Playerの直接の子になっているチェーンソーのルートです。手のボーンの子から外し、武器全体の親を指定してください。")]
    [SerializeField] private Transform poseTarget;
    [Tooltip("状態ごとの姿勢データです。")]
    [SerializeField] private ChainsawPoseData poseData;
    [Tooltip("左向きに変換する回転です。通常はYを180度にします。位置にも同じ変換を適用し、向きの切り替え自体は即時反映します。")]
    [SerializeField] private Vector3 leftFacingEuler = new Vector3(0f, 180f, 0f);
    [Tooltip("この横速度を超えると移動姿勢にします。既存Rigidbodyの速度だけを参照します。")]
    [SerializeField, Min(0.001f)] private float moveThreshold = 0.05f;

    public Transform PlayerRoot => playerRoot;
    public Transform PoseTarget => poseTarget;
    public Transform PoseParent => playerRoot;
    public ChainsawPoseData PoseData => poseData;
    public ChainsawPoseState CurrentState { get; private set; }
    public string CurrentPoseName => activePose != null ? activePose.DisplayName : "開始時の配置（未登録）";

    private Rigidbody body;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 currentPosition;
    private Quaternion currentRotation;
    private Vector3 fromPosition;
    private Quaternion fromRotation;
    private ChainsawPoseEntry activePose;
    private bool initialized;
    private bool hasState;
    private float elapsed;
    private ChainsawPoseState previousState;
    private AttackData previousAttack;

    // 必要な参照を確認し、未登録状態で使う開始時の配置を保存します。
    private void Awake()
    {
        if (player == null) player = GetComponent<SensorPlayerController>();
        if (digging == null) digging = GetComponent<SensorChainsawDigging>();
        if (playerRoot == null && player != null) playerRoot = player.transform;
        if (playerRoot != null) body = playerRoot.GetComponent<Rigidbody>();
        if (player == null || digging == null || body == null || poseData == null ||
            !IsTargetValid() || player.transform != playerRoot || digging.transform != playerRoot)
        {
            Debug.LogError("姿勢制御：Player、食い込み、Rigidbody、姿勢データと、Playerの直接の子になっている武器ルートを設定してください。", this);
            enabled = false;
            return;
        }
        initialPosition = poseTarget.localPosition;
        initialRotation = poseTarget.localRotation;
        currentPosition = initialPosition;
        currentRotation = initialRotation;
        initialized = true;
    }

    // 既存状態から姿勢を選び、物理時間で補間します。ヒットストップ中は進みません。
    private void FixedUpdate()
    {
        if (!initialized || !IsTargetValid() || !player.isActiveAndEnabled || poseData == null) return;
        CurrentState = ResolveState();
        ChainsawPoseEntry nextPose = poseData.FindPose(CurrentState, player.CurrentAttackData);
        if (!hasState || nextPose != activePose || CurrentState != previousState || player.CurrentAttackData != previousAttack)
        {
            fromPosition = currentPosition;
            fromRotation = currentRotation;
            activePose = nextPose;
            elapsed = 0f;
            hasState = true;
        }
        previousState = CurrentState;
        previousAttack = player.CurrentAttackData;
        float duration = activePose != null ? activePose.BlendSeconds : 0f;
        if (activePose == null)
        {
            currentPosition = initialPosition;
            currentRotation = initialRotation;
        }
        else if (duration > 0f && elapsed < duration)
        {
            float amount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            currentPosition = Vector3.Lerp(fromPosition, activePose.LocalPosition, amount);
            currentRotation = Quaternion.Slerp(fromRotation, Quaternion.Euler(activePose.LocalEulerAngles), amount);
        }
        else
        {
            float motionTime = Mathf.Max(0f, elapsed - Mathf.Max(0f, duration));
            float progress = activePose.MotionSeconds > 0f ? motionTime / activePose.MotionSeconds : 1f;
            activePose.Evaluate(progress, out currentPosition, out currentRotation);
        }
        GetFacingPose(currentPosition, currentRotation, player.FacingDirection < 0f,
            out Vector3 position, out Quaternion rotation);
        poseTarget.SetLocalPositionAndRotation(position, rotation);
        elapsed += Time.fixedDeltaTime;
    }

    // 公開済みのゲーム状態を姿勢用に分類します。入力・接触判定は追加しません。
    private ChainsawPoseState ResolveState()
    {
        if (player.IsImpactStunned) return ChainsawPoseState.ImpactStun;
        if (digging.IsEvading) return ChainsawPoseState.Evade;
        if (player.IsAttacking)
            return player.IsGroundedForCamera ? ChainsawPoseState.GroundAttack : ChainsawPoseState.AirAttack;
        if (digging.isActiveAndEnabled)
        {
            switch (digging.Surface)
            {
                case ChainsawSurface.Floor: return ChainsawPoseState.DiggingFloor;
                case ChainsawSurface.Wall: return ChainsawPoseState.DiggingWall;
                case ChainsawSurface.Ceiling: return ChainsawPoseState.DiggingCeiling;
                case ChainsawSurface.Enemy: return ChainsawPoseState.DiggingEnemy;
            }
            if (digging.IsRequested) return ChainsawPoseState.DiggingReady;
        }
        if (!player.IsGroundedForCamera)
            return body.linearVelocity.y > 0f ? ChainsawPoseState.Rise : ChainsawPoseState.Fall;
        return Mathf.Abs(body.linearVelocity.x) > moveThreshold ? ChainsawPoseState.Move : ChainsawPoseState.Idle;
    }

    // アニメーションする手から切り離されたPlayer直下の武器だけを許可します。
    public bool IsTargetValid()
    {
        return playerRoot != null && poseTarget != null && poseTarget != playerRoot && poseTarget.parent == playerRoot;
    }

    // 右向きのPlayer基準姿勢を左向きへ変換します。
    public void GetFacingPose(Vector3 position, Quaternion rotation, bool faceLeft,
        out Vector3 resultPosition, out Quaternion resultRotation)
    {
        Quaternion facing = faceLeft ? Quaternion.Euler(leftFacingEuler) : Quaternion.identity;
        resultPosition = facing * position;
        resultRotation = facing * rotation;
    }

    // 左向きの編集結果を保存用の右向き姿勢へ戻します。
    public void RemoveFacingPose(Vector3 position, Quaternion rotation, bool faceLeft,
        out Vector3 resultPosition, out Quaternion resultRotation)
    {
        Quaternion inverse = faceLeft ? Quaternion.Inverse(Quaternion.Euler(leftFacingEuler)) : Quaternion.identity;
        resultPosition = inverse * position;
        resultRotation = inverse * rotation;
    }

    // 無効化時は初期配置に戻し、再有効化時の補間をリセットします。
    private void OnDisable()
    {
        if (!initialized) return;
        if (IsTargetValid()) poseTarget.SetLocalPositionAndRotation(initialPosition, initialRotation);
        currentPosition = initialPosition;
        currentRotation = initialRotation;
        activePose = null;
        hasState = false;
    }
}
