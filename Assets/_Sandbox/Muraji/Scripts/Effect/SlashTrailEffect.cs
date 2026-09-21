using UnityEngine;

[RequireComponent(typeof(Animator))]
public class SlashTrailEffect : MonoBehaviour
{
    [SerializeField] TrailRenderer trail;

    [Header("エフェクト用Animatorのステート名")]
    [SerializeField] string effect1State = "Base Layer.SlashEffect1";
    [SerializeField] string effect2State = "Base Layer.SlashEffect2";
    [SerializeField] string effect3State = "Base Layer.SlashEffect3";

    Animator effectAnimator;

    void Awake()
    {
        effectAnimator = GetComponent<Animator>();
        ClearTrail();
    }

    public void PlayEffect(int step)
    {
        if (trail == null || !isActiveAndEnabled)
        {
            return;
        }

        if (effectAnimator == null)
        {
            effectAnimator = GetComponent<Animator>();
        }

        if (!effectAnimator.isActiveAndEnabled)
        {
            return;
        }

        if (step < 1 || step > 3)
        {
            return;
        }

        string stateName =
            step == 1 ? effect1State :
            step == 2 ? effect2State :
            effect3State;

        int stateHash = Animator.StringToHash(stateName);

        if (!effectAnimator.HasState(0, stateHash))
        {
            Debug.LogError(
                "トレイル用ステートが見つかりません：" + stateName,
                this
            );
            return;
        }

        // 始点へ戻す間は軌跡を出さない。
        trail.emitting = false;

        // 対応するエフェクトを先頭から再生。
        effectAnimator.Play(stateHash, 0, 0f);

        // エフェクト専用Animatorの始点を反映する。
        effectAnimator.Update(0f);

        // 前回の軌跡を消してから、新しく生成する。
        trail.Clear();
        trail.emitting = true;
    }

    // 新しい軌跡の生成を止める。
    // 残っている軌跡はTrail RendererのTimeで自然に消える。
    public void StopTrail()
    {
        if (trail != null)
        {
            trail.emitting = false;
        }
    }

    // 中断・無効化・次段開始用。
    public void ClearTrail()
    {
        if (trail != null)
        {
            trail.emitting = false;
            trail.Clear();
        }
    }

    void OnDisable()
    {
        ClearTrail();
    }
}