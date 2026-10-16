using System;
using System.Collections.Generic;
using UnityEngine;

// 姿勢選択用の分類です。ゲーム本体の状態や判定は変更しません。
public enum ChainsawPoseState
{
    Idle, Move, Rise, Fall, GroundAttack, AirAttack,
    DiggingReady, DiggingFloor, DiggingWall, DiggingCeiling, DiggingEnemy,
    Evade, ImpactStun
}

[Serializable]
public sealed class ChainsawPoseEntry
{
    [Tooltip("編集画面に表示する姿勢の名前です。")]
    public string DisplayName = "新しい姿勢";
    [Tooltip("この姿勢を使用するプレイヤーの状態です。")]
    public ChainsawPoseState State;
    [Tooltip("通常攻撃の段ごとに分ける場合、既存ControllerのSlash1～3と同じAttackDataを指定します。空欄ならその攻撃状態の共通姿勢です。")]
    public AttackData AttackFilter;
    [Tooltip("Player基準の開始位置です。右向き時の武器のLocal Positionと同じ値です。")]
    public Vector3 LocalPosition;
    [Tooltip("Player基準の開始角度です。右向き時の武器のLocal Rotationと同じ値で、単位は度です。")]
    public Vector3 LocalEulerAngles;
    [Tooltip("直前の姿勢から開始姿勢へ移る秒数です。この移行が終わってから開始→終了の動きを再生します。0なら開始姿勢へ即時移動します。")]
    [Min(0f)] public float BlendSeconds = 0.1f;

    [Tooltip("ONなら、この状態へ入ったときに開始姿勢から終了姿勢へ一度だけ動きます。OFFなら開始姿勢を保持します。")]
    public bool PlayMotion;
    [Tooltip("Player基準の終了位置です。位置の軌道は開始と終了を結ぶ直線です。")]
    public Vector3 EndLocalPosition;
    [Tooltip("Player基準の終了角度です。回転は開始から最短方向で補間します。")]
    public Vector3 EndLocalEulerAngles;
    [Tooltip("開始から終了へ動く秒数です。状態が途中で変わったら中断します。0なら終了姿勢へ即時移動します。")]
    [Min(0f)] public float MotionSeconds = 0.3f;
    [Tooltip("横軸は時間0～1、縦軸は開始から終了への進み具合0～1です。範囲外の値は0～1に制限します。開始値0・終了値1で設定してください。")]
    public AnimationCurve ProgressCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    // 0～1の進行度からPlayer基準の姿勢を計算します。実行時と編集プレビューで共有します。
    public void Evaluate(float progress, out Vector3 position, out Quaternion rotation)
    {
        if (!PlayMotion)
        {
            position = LocalPosition;
            rotation = Quaternion.Euler(LocalEulerAngles);
            return;
        }
        float time = Mathf.Clamp01(progress);
        float amount = time <= 0f ? 0f : time >= 1f ? 1f :
            Mathf.Clamp01(ProgressCurve != null && ProgressCurve.length > 0 ? ProgressCurve.Evaluate(time) : time);
        position = Vector3.Lerp(LocalPosition, EndLocalPosition, amount);
        rotation = Quaternion.Slerp(Quaternion.Euler(LocalEulerAngles), Quaternion.Euler(EndLocalEulerAngles), amount);
    }
}

[CreateAssetMenu(fileName = "ChainsawPoseData", menuName = "チェーンソー/状態別の姿勢データ")]
public sealed class ChainsawPoseData : ScriptableObject
{
    [Tooltip("上から順に検索します。同じ状態・攻撃データの重複は避けてください。未登録の状態ではゲーム開始時の配置に戻ります。")]
    public List<ChainsawPoseEntry> Poses = new List<ChainsawPoseEntry>();

    // 状態と攻撃データが一致する専用姿勢を優先し、なければ共通姿勢を返します。
    public ChainsawPoseEntry FindPose(ChainsawPoseState state, AttackData attack)
    {
        ChainsawPoseEntry fallback = null;
        if (Poses == null) return null;
        bool isAttack = state == ChainsawPoseState.GroundAttack || state == ChainsawPoseState.AirAttack;
        foreach (ChainsawPoseEntry entry in Poses)
        {
            if (entry == null || entry.State != state) continue;
            if (isAttack && attack != null && entry.AttackFilter == attack) return entry;
            if ((!isAttack || entry.AttackFilter == null) && fallback == null) fallback = entry;
        }
        return fallback;
    }
}
