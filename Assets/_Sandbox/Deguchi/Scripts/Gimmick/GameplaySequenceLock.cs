using System.Collections.Generic;
using UnityEngine;

/// 扉演出やイベントシーン中など、
/// プレイヤー操作を一時的に停止するためのクラス。
public class GameplaySequenceLock : MonoBehaviour
{
    /// 現在イベント演出によってゲーム操作がロックされているか。
    ///
    /// 他のシステムから
    /// GameplaySequenceLock.IsSequenceLocked
    /// で確認することもできます。
    public static bool IsSequenceLocked { get; private set; }

    [Header("演出中に無効化するスクリプト")]
    [Tooltip(
        "Player操作、Camera操作、PauseMenuなどを登録してください。"
    )]
    [SerializeField]
    private List<Behaviour> behavioursToDisable
        = new List<Behaviour>();

    private readonly Dictionary<Behaviour, bool> previousStates
        = new Dictionary<Behaviour, bool>();

    private float previousTimeScale = 1f;
    private bool isLocked;

    /// ゲームプレイを停止します。
    public void LockGameplay()
    {
        if (isLocked)
        {
            return;
        }

        isLocked = true;
        IsSequenceLocked = true;

        previousStates.Clear();

        // 各スクリプトが元々有効だったか保存する。
        foreach (Behaviour behaviour in behavioursToDisable)
        {
            if (behaviour == null)
            {
                continue;
            }

            previousStates[behaviour] = behaviour.enabled;

            behaviour.enabled = false;
        }

        // 元のTimeScaleを保存してから停止。
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
    }

    /// 停止していたゲームプレイを元に戻します。
    public void UnlockGameplay()
    {
        if (!isLocked)
        {
            return;
        }

        // スクリプトを演出前の状態へ戻す。
        foreach (KeyValuePair<Behaviour, bool> pair in previousStates)
        {
            if (pair.Key == null)
            {
                continue;
            }

            pair.Key.enabled = pair.Value;
        }

        previousStates.Clear();

        // TimeScaleも演出前へ戻す。
        Time.timeScale = previousTimeScale;

        IsSequenceLocked = false;
        isLocked = false;
    }
}
