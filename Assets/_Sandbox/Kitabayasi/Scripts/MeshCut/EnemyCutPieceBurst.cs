using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Enemyのメッシュ分割後に生成された全切断片へ、
/// 死亡させた攻撃のノックバック方向を使って飛散Impulseと回転Impulseを与える。
/// </summary>
/// <remarks>
/// 飛散方向には死亡攻撃のノックバック方向だけを使用し、飛散強度は専用値として管理する。
/// 分割直後に与えた飛散速度成分は、設定した減衰時間とカーブに従って徐々に弱める。
/// 重力や衝突によって生じる速度まで一括で減衰させないため、Rigidbody.linearDampingは変更しない。
/// 回転方向は死亡攻撃の攻撃元左右、回転強度は命中時のチェンソー回転率から決定する。
/// Mass設定、Mesh分割、Rigidbody生成、死亡判定、State遷移、縮小、Enemy本体の破棄は担当しない。
/// </remarks>
public sealed class EnemyCutPieceBurst : MonoBehaviour
{
    private const float DIRECTION_EPSILON_SQR =
        0.000001f;

    private const float SIDE_DIRECTION_EPSILON =
        0.001f;

    private const float MIN_BURST_DECAY_DURATION =
        0.01f;

    [Header("吹き飛ばし")]

    [Tooltip(
        "死亡させた攻撃のノックバック方向へ分割直後に与えるImpulse強度。" +
        "全切断片へ同じImpulseを与え、Mass差によって飛びやすさを変える。")]
    [SerializeField]
    [Min(0f)]
    [FormerlySerializedAs("burstSpeed")]
    private float burstImpulse = 8f;

    [Tooltip(
        "分割直後に与えた飛散速度成分を弱め切るまでの時間。" +
        "重力による落下速度や衝突で生じた速度を直接減衰させる値ではない。")]
    [SerializeField]
    [Min(MIN_BURST_DECAY_DURATION)]
    private float burstVelocityDecayDuration = 0.8f;

    [Tooltip(
        "飛散速度成分の残量カーブ。横軸0が分割直後、1が減衰終了時。" +
        "縦軸1で初期飛散速度を100%、0で飛散速度成分を0%まで弱める。" +
        "値が途中で増えていても、実行時は前回値より大きくならないよう制限する。")]
    [SerializeField]
    private AnimationCurve burstVelocityDecayCurve =
        AnimationCurve.EaseInOut(
            0f,
            1f,
            1f,
            0f);

    [Header("回転")]

    [Tooltip(
        "チェンソーが最低回転のときに切断片へ与える回転Impulse強度。")]
    [SerializeField]
    [Min(0f)]
    private float minimumTorqueImpulse = 0.1f;

    [Tooltip(
        "チェンソーが最大回転のときに切断片へ与える回転Impulse強度。")]
    [SerializeField]
    [Min(0f)]
    private float maximumTorqueImpulse = 0.4f;

    // 各切断片に最初に与えたImpulse由来の速度成分だけを追跡する。
    // Rigidbody全体の速度を直接スケールすると、重力や衝突による速度まで弱めてしまうため分離して保持する。
    private readonly List<Rigidbody>
        decayingPieceRigidbodies =
            new List<Rigidbody>();

    private readonly List<Vector3>
        initialBurstVelocityChanges =
            new List<Vector3>();

    private float burstDecayElapsedTime;
    private float previousBurstVelocityMultiplier = 1f;
    private bool isBurstDecayActive;

    private void FixedUpdate()
    {
        UpdateBurstVelocityDecay();
    }

    /// <summary>
    /// 指定された全切断片へ、死亡攻撃に対応する飛散Impulseと回転Impulseを与え、
    /// 飛散Impulse由来の速度成分の時間減衰を開始する。
    /// </summary>
    /// <param name="cutPieces">
    /// EnemyMeshCutterが今回のMesh分割で生成した全切断片。
    /// </param>
    /// <param name="resolvedKnockbackVelocity">
    /// 攻撃元の左右判定まで反映済みのX/Yノックバック初速度。
    /// 強さは使用せず、吹き飛ばす方向だけに使用する。
    /// </param>
    /// <param name="hitSourceDirectionSign">
    /// Enemyから見た死亡攻撃元の左右方向。-1は左側、+1は右側。
    /// 左側からの攻撃は時計回り、右側からの攻撃は反時計回りとなるよう回転方向へ変換する。
    /// </param>
    /// <param name="chainsawRotationRatio">
    /// 死亡攻撃が命中した瞬間のチェンソー回転率。最低回転を0、最大回転を1とする。
    /// </param>
    public bool BeginBurst(
        IReadOnlyList<Transform> cutPieces,
        Vector2 resolvedKnockbackVelocity,
        float hitSourceDirectionSign,
        float chainsawRotationRatio)
    {
        ResetBurstVelocityDecayState();

        if (cutPieces == null ||
            cutPieces.Count == 0)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceBurst)}: " +
                "吹き飛ばす切断片がありません。",
                this);

            return false;
        }

        Vector3 burstDirection =
            new Vector3(
                resolvedKnockbackVelocity.x,
                resolvedKnockbackVelocity.y,
                0f);

        if (burstDirection.sqrMagnitude <=
            DIRECTION_EPSILON_SQR)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceBurst)}: " +
                "死亡攻撃のノックバックベクトルが0のため、" +
                "切断片の吹き飛ばし方向を決定できません。",
                this);

            return false;
        }

        burstDirection.Normalize();

        Vector3 resolvedBurstImpulse =
            burstDirection *
            Mathf.Max(
                0f,
                burstImpulse);

        float minimumTorque =
            Mathf.Max(
                0f,
                minimumTorqueImpulse);

        float maximumTorque =
            Mathf.Max(
                minimumTorque,
                maximumTorqueImpulse);

        float resolvedTorqueImpulse =
            Mathf.Lerp(
                minimumTorque,
                maximumTorque,
                Mathf.Clamp01(
                    chainsawRotationRatio));

        bool hasRotationDirection =
            Mathf.Abs(hitSourceDirectionSign) >
            SIDE_DIRECTION_EPSILON;

        if (!hasRotationDirection &&
            resolvedTorqueImpulse > 0f)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceBurst)}: " +
                "死亡攻撃元の左右情報が0のため、" +
                "切断片への回転Impulseは適用しません。",
                this);
        }

        Vector3 resolvedTorque =
            Vector3.zero;

        if (hasRotationDirection &&
            resolvedTorqueImpulse > 0f)
        {
            float rotationDirectionSign =
                hitSourceDirectionSign < 0f
                    ? -1f
                    : 1f;

            // 現在のゲームプレイ平面はX/Y、奥行き方向はZで固定されている。
            // 左側からの攻撃(-1)を時計回り、右側(+1)を反時計回りにするため、
            // 既存の画面向きでは攻撃元SignをそのままZ軸Torqueの符号へ使用する。
            resolvedTorque =
                Vector3.forward *
                (resolvedTorqueImpulse *
                 rotationDirectionSign);
        }

        int appliedPieceCount = 0;

        for (int i = 0;
             i < cutPieces.Count;
             i++)
        {
            Transform cutPiece =
                cutPieces[i];

            if (cutPiece == null)
            {
                continue;
            }

            Rigidbody pieceRigidbody =
                cutPiece.GetComponent<Rigidbody>();

            if (pieceRigidbody == null ||
                pieceRigidbody.isKinematic)
            {
                Debug.LogWarning(
                    $"{nameof(EnemyCutPieceBurst)}: " +
                    $"切断片 '{cutPiece.name}' に有効なRigidbodyがないため、" +
                    "吹き飛ばしを適用できません。",
                    cutPiece);

                continue;
            }

            if (resolvedBurstImpulse.sqrMagnitude > 0f)
            {
                // 初速は従来どおりImpulse / Massで決まる。
                // その後の減衰では、このImpulseが作った速度成分だけを追跡して弱める。
                pieceRigidbody.AddForce(
                    resolvedBurstImpulse,
                    ForceMode.Impulse);

                decayingPieceRigidbodies.Add(
                    pieceRigidbody);

                initialBurstVelocityChanges.Add(
                    resolvedBurstImpulse /
                    Mathf.Max(
                        pieceRigidbody.mass,
                        Mathf.Epsilon));
            }

            if (resolvedTorque.sqrMagnitude > 0f)
            {
                pieceRigidbody.AddTorque(
                    resolvedTorque,
                    ForceMode.Impulse);
            }

            appliedPieceCount++;
        }

        if (appliedPieceCount == 0)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceBurst)}: " +
                "吹き飛ばしを適用できる切断片がありませんでした。",
                this);

            ResetBurstVelocityDecayState();
            return false;
        }

        if (decayingPieceRigidbodies.Count > 0)
        {
            burstDecayElapsedTime = 0f;
            previousBurstVelocityMultiplier = 1f;
            isBurstDecayActive = true;
        }

        return true;
    }

    /// <summary>
    /// 最初のImpulseが作った速度成分だけを、物理更新ごとに減衰カーブへ近づける。
    /// </summary>
    /// <remarks>
    /// Rigidbody全体のlinearVelocityを倍率変更すると、Physics.gravityやCollider衝突によって
    /// 生じた速度まで弱めてしまう。そこでImpulse / Massで求めた初期飛散速度成分を別管理し、
    /// 前回倍率との差分を減速量として使用する。現在速度を初期飛散方向へ射影し、
    /// 今回の減速で残速を使い切る場合は適用せずPhysicsへ任せることで、衝突後の停止や反射と競合しない。
    /// </remarks>
    private void UpdateBurstVelocityDecay()
    {
        if (!isBurstDecayActive)
        {
            return;
        }

        float decayDuration =
            Mathf.Max(
                MIN_BURST_DECAY_DURATION,
                burstVelocityDecayDuration);

        burstDecayElapsedTime =
            Mathf.Min(
                burstDecayElapsedTime +
                Time.fixedDeltaTime,
                decayDuration);

        float normalizedTime =
            Mathf.Clamp01(
                burstDecayElapsedTime /
                decayDuration);

        float evaluatedMultiplier =
            burstVelocityDecayCurve != null &&
            burstVelocityDecayCurve.length > 0
                ? burstVelocityDecayCurve.Evaluate(
                    normalizedTime)
                : 1f - normalizedTime;

        float nextMultiplier =
            Mathf.Clamp01(
                evaluatedMultiplier);

        // 「時間経過で弱くなる」という責務から外れないよう、
        // Curveが途中で上昇していても飛散速度を再加速させない。
        nextMultiplier =
            Mathf.Min(
                previousBurstVelocityMultiplier,
                nextMultiplier);

        if (normalizedTime >= 1f)
        {
            nextMultiplier = 0f;
        }

        float multiplierDelta =
            nextMultiplier -
            previousBurstVelocityMultiplier;

        if (!Mathf.Approximately(
                multiplierDelta,
                0f))
        {
            for (int i = decayingPieceRigidbodies.Count - 1;
                 i >= 0;
                 i--)
            {
                Rigidbody pieceRigidbody =
                    decayingPieceRigidbodies[i];

                if (pieceRigidbody == null ||
                    pieceRigidbody.isKinematic)
                {
                    continue;
                }

                Vector3 initialBurstVelocityChange =
                    initialBurstVelocityChanges[i];

                if (initialBurstVelocityChange.sqrMagnitude <=
                    DIRECTION_EPSILON_SQR)
                {
                    decayingPieceRigidbodies.RemoveAt(i);
                    initialBurstVelocityChanges.RemoveAt(i);
                    continue;
                }

                float initialBurstSpeed =
                    initialBurstVelocityChange.magnitude;

                Vector3 initialBurstDirection =
                    initialBurstVelocityChange /
                    initialBurstSpeed;

                float currentBurstDirectionSpeed =
                    Vector3.Dot(
                        pieceRigidbody.linearVelocity,
                        initialBurstDirection);

                // 衝突などですでに停止・反転しているPieceへ減速を追加すると、
                // 飛散処理自身が逆向き速度を作るため、この時点で減衰対象から外す。
                if (currentBurstDirectionSpeed <= 0f)
                {
                    decayingPieceRigidbodies.RemoveAt(i);
                    initialBurstVelocityChanges.RemoveAt(i);
                    continue;
                }

                float requestedDecelerationSpeed =
                    initialBurstSpeed *
                    -multiplierDelta;

                if (requestedDecelerationSpeed <= 0f)
                {
                    continue;
                }

                // 現在残っている飛散方向速度を今回の減衰だけで使い切る状況では、
                // VelocityChangeを適用しない。衝突・摩擦などでPhysics側がすでに
                // 速度を落としている可能性があるため、以降は通常のPhysicsへ任せる。
                if (requestedDecelerationSpeed >=
                    currentBurstDirectionSpeed)
                {
                    decayingPieceRigidbodies.RemoveAt(i);
                    initialBurstVelocityChanges.RemoveAt(i);
                    continue;
                }

                Vector3 burstVelocityChange =
                    -initialBurstDirection *
                    requestedDecelerationSpeed;

                pieceRigidbody.AddForce(
                    burstVelocityChange,
                    ForceMode.VelocityChange);
            }
        }

        if (decayingPieceRigidbodies.Count == 0)
        {
            ResetBurstVelocityDecayState();
            return;
        }

        previousBurstVelocityMultiplier =
            nextMultiplier;

        if (normalizedTime >= 1f)
        {
            ResetBurstVelocityDecayState();
        }
    }

    private void ResetBurstVelocityDecayState()
    {
        decayingPieceRigidbodies.Clear();
        initialBurstVelocityChanges.Clear();
        burstDecayElapsedTime = 0f;
        previousBurstVelocityMultiplier = 1f;
        isBurstDecayActive = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        burstImpulse =
            Mathf.Max(
                0f,
                burstImpulse);

        burstVelocityDecayDuration =
            Mathf.Max(
                MIN_BURST_DECAY_DURATION,
                burstVelocityDecayDuration);

        if (burstVelocityDecayCurve == null ||
            burstVelocityDecayCurve.length == 0)
        {
            burstVelocityDecayCurve =
                AnimationCurve.EaseInOut(
                    0f,
                    1f,
                    1f,
                    0f);
        }

        minimumTorqueImpulse =
            Mathf.Max(
                0f,
                minimumTorqueImpulse);

        maximumTorqueImpulse =
            Mathf.Max(
                minimumTorqueImpulse,
                maximumTorqueImpulse);
    }
#endif
}
