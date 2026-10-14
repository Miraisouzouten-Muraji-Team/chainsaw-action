using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "StageRespawnData",
    menuName = "Game/ステージのリスポーン設定")]
public sealed class StageRespawnData : ScriptableObject
{
    [Serializable]
    public sealed class RespawnPoint
    {
        [Tooltip(
            "対応するエリア番号です。Camera AreasのElement 0が1、"
            + "Element 1が2に対応します。")]
        [SerializeField, Min(1)]
        private int areaNumber = 1;

        [Tooltip(
            "復活時のプレイヤー本体のワールド座標です。"
            + "足元ではなく、Rigidbodyが付いている本体の座標を指定します。")]
        [SerializeField]
        private Vector3 position;

        [Tooltip("オンなら右向き、オフなら左向きで復活します。")]
        [SerializeField]
        private bool faceRight = true;

        public int AreaNumber => areaNumber;
        public Vector3 Position => position;
        public bool FaceRight => faceRight;
    }

    [Tooltip("各エリアの復活地点です。同じエリア番号を重複させないでください。")]
    [SerializeField]
    private RespawnPoint[] respawnPoints = Array.Empty<RespawnPoint>();

    [Tooltip(
        "一度もエリアに入っていない状態で死亡した場合の復活先です。"
        + "通常は最初のエリア番号を指定します。")]
    [SerializeField, Min(1)]
    private int fallbackAreaNumber = 1;

    [Tooltip(
        "死亡モーションを見せてから暗転を開始するまでの秒数です。"
        + "死亡クリップの長さに合わせて設定してください。")]
    [SerializeField, Min(0f)]
    private float deathMotionSeconds = 0.8f;

    [Tooltip("画面が完全に黒くなるまでの秒数です。")]
    [SerializeField, Min(0f)]
    private float fadeOutSeconds = 0.4f;

    [Tooltip("黒い画面から通常の画面へ戻るまでの秒数です。")]
    [SerializeField, Min(0f)]
    private float fadeInSeconds = 0.4f;

    [Tooltip(
        "復活モーション開始から操作を再開できるまでの秒数です。"
        + "フェードインが終わっていない場合は、その終了も待ちます。")]
    [SerializeField, Min(0f)]
    private float respawnMotionSeconds = 1f;

    public int FallbackAreaNumber => fallbackAreaNumber;
    public float DeathMotionSeconds => deathMotionSeconds;
    public float FadeOutSeconds => fadeOutSeconds;
    public float FadeInSeconds => fadeInSeconds;
    public float RespawnMotionSeconds => respawnMotionSeconds;

    // 指定エリアの復活地点を取得する。番号が重複していた場合も失敗にする。
    public bool TryGetPoint(int areaNumber, out RespawnPoint point)
    {
        point = null;

        if (respawnPoints == null)
            return false;

        foreach (RespawnPoint candidate in respawnPoints)
        {
            if (candidate == null || candidate.AreaNumber != areaNumber)
                continue;

            if (point != null)
            {
                point = null;
                return false;
            }

            point = candidate;
        }

        return point != null;
    }

    // エリア番号の範囲・重複・座標・演出時間の設定を確認する。
    public bool ValidateSettings(int areaCount, out string error)
    {
        if (areaCount <= 0 ||
            respawnPoints == null ||
            respawnPoints.Length != areaCount)
        {
            error = "Camera Areasの数と、SOの復活地点の数を一致させてください。";
            return false;
        }

        for (int areaNumber = 1; areaNumber <= areaCount; areaNumber++)
        {
            if (!TryGetPoint(areaNumber, out RespawnPoint point))
            {
                error = $"エリア{areaNumber}の復活地点が未設定、または番号が重複しています。";
                return false;
            }

            Vector3 position = point.Position;

            if (!IsFinite(position.x) ||
                !IsFinite(position.y) ||
                !IsFinite(position.z))
            {
                error = $"エリア{areaNumber}の復活座標が不正です。";
                return false;
            }
        }

        if (!TryGetPoint(fallbackAreaNumber, out _))
        {
            error = "エリア未確定時の復活先が設定されていません。";
            return false;
        }

        if (!IsValidDuration(deathMotionSeconds) ||
            !IsValidDuration(fadeOutSeconds) ||
            !IsValidDuration(fadeInSeconds) ||
            !IsValidDuration(respawnMotionSeconds))
        {
            error = "演出時間には0以上の有限の値を指定してください。";
            return false;
        }

        error = null;
        return true;
    }

    // 演出時間として使用できる数値か確認する。
    private static bool IsValidDuration(float value)
    {
        return IsFinite(value) && value >= 0f;
    }

    // NaNと無限大を除外する。
    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
