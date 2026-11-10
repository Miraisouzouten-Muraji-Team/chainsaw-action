using System;
using UnityEngine;

namespace Project.Tutorial
{
    public enum TutorialMode { Sign, InteractiveSlides }
    // 既存SOのenum値を維持するため、追加は末尾。
    public enum TutorialDemo { Move, Jump, DoubleJump, Custom, FloorDig, WallDig, WallKick, CeilingDig, Accelerator, FloorBonus, WeakCombo, EnemyDig, AirEnemyDig }

    [CreateAssetMenu(menuName = "Tutorial/Definition", fileName = "TutorialDefinition")]
    public sealed class TutorialDefinition : ScriptableObject
    {
        public TutorialMode mode;
        [Min(0.01f)] public float fadeSeconds = 0.5f;
        public TutorialPage[] pages = { new TutorialPage() };
    }

    [Serializable]
    public sealed class TutorialPage
    {
        public string title = "ジャンプ";
        [TextArea(2, 6)] public string description = "Aボタンでジャンプ";
        public TutorialDemo demo = TutorialDemo.Jump;
        public string buttonLabel = "A";
        [Header("攻撃・アクセル説明用（ゲーム本体のパラメータとは独立）")]
        [Range(0f, 1f)] public float thirdPress = 0.55f;
        [Min(1f)] public float demoMaxRPM = 50f;
        [Min(0f)] public float demoMinRPM = 5f;
        [Min(0f)] public float demoBonusThreshold = 40f;
        [Min(0f)] public float demoAfterBonusRPM = 35f;
        [Range(0.01f, 0.2f)] public float demoBonusTravelDuration = 0.04f;
        [Tooltip("敵デモのノックバック方向・量。図内の正規化座標。")]
        public Vector2 demoKnockback = new Vector2(0.18f, 0.1f);
        [Tooltip("説明用の図。未指定なら簡易デモ。連番がある場合は連番を優先。")]
        public Sprite illustration;
        public Sprite[] animationFrames = new Sprite[0];
        [Tooltip("WallDigの2周目（ジャンプから食い込み）用連番。通常連番と交互に再生。")]
        public Sprite[] alternateAnimationFrames = new Sprite[0];
        [Tooltip("トグル操作の押下表示時間。1周期に対する割合。離した後もデモは継続。")]
        [Range(0.01f, 0.3f)] public float triggerPressDuration = 0.08f;
        [Min(0.2f)] public float cycleSeconds = 2.4f;
        [Tooltip("同じ周期内の入力を0～1で指定。連番の該当フレームと同期する。")]
        [Range(0f, 1f)] public float firstPress = 0.15f;
        [Range(0f, 1f)] public float secondPress = 0.38f;
        [Range(0f, 1f)] public float landing = 0.75f;
        [Tooltip("Custom用：この区間はボタンを押した表示。")]
        [Range(0f, 1f)] public float customRelease = 0.7f;
    }

    // 入力表示とデモの双方が同じ時刻から評価する。別々のAnimatorでずれない。
    public static class TutorialTimeline
    {
        public static bool IsDigDemo(TutorialDemo demo)
        {
            return demo == TutorialDemo.FloorDig || demo == TutorialDemo.WallDig ||
                demo == TutorialDemo.WallKick || demo == TutorialDemo.CeilingDig;
        }

        // x/yは図内の正規化位置。トグルの押下は1周期に1回だけ。
        public static void EvaluateDig(TutorialPage page, float phase, bool airborne,
            out float x, out float y, out float tilt, out float blade, out bool pressed, out bool attached)
        {
            float first = Mathf.Clamp(page.firstPress, 0.05f, 0.8f);
            float end = Mathf.Clamp(page.landing, first + 0.1f, 0.98f);
            float t = Mathf.InverseLerp(first, end, phase);
            pressed = phase >= first && phase < first + Mathf.Min(Mathf.Max(.01f, page.triggerPressDuration), (end - first) * .5f);
            attached = phase >= first;
            tilt = 0f; blade = 0f;
            x = .72f; y = .1f;
            switch (page.demo)
            {
                case TutorialDemo.FloorDig:
                    x = Mathf.Lerp(.25f, .7f, t);
                    tilt = attached ? -16f : 0f;
                    blade = -65f;
                    break;
                case TutorialDemo.WallDig:
                    if (airborne && phase < first)
                    {
                        float jump = phase / first;
                        x = Mathf.Lerp(.3f, .72f, jump);
                        y = Mathf.Lerp(.1f, .35f, jump) + .13f * Mathf.Sin(jump * Mathf.PI);
                    }
                    else y = Mathf.Lerp(airborne ? .35f : .1f, .55f, t);
                    break;
                case TutorialDemo.WallKick:
                    // 開始時点で壁に食い込んでいる。Aを押した瞬間に壁から離れる。
                    attached = phase < first;
                    x = Mathf.Lerp(.72f, .3f, t);
                    y = Mathf.Lerp(.28f, .45f, t) + .18f * Mathf.Sin(t * Mathf.PI);
                    blade = attached ? 0f : 150f;
                    break;
                case TutorialDemo.CeilingDig:
                    blade = 65f;
                    if (phase < first)
                    {
                        float jump = phase / first;
                        x = Mathf.Lerp(.25f, .55f, jump);
                        y = Mathf.Lerp(.1f, .57f, Mathf.Sin(jump * Mathf.PI * .5f));
                    }
                    else { x = Mathf.Lerp(.55f, .8f, t); y = .57f; }
                    break;
            }
        }

        public static void Evaluate(TutorialPage page, float phase,
            out float x, out float y, out float stick, out bool pressed)
        {
            x = y = stick = 0f;
            pressed = false;
            float first = Mathf.Clamp(page.firstPress, 0f, 0.9f);
            float land = Mathf.Clamp(page.landing, first + 0.05f, 1f);
            float second = Mathf.Clamp(page.secondPress, first + 0.02f, land - 0.02f);
            if (page.demo == TutorialDemo.Move)
            {
                x = Mathf.Sin(phase * Mathf.PI * 2f);
                stick = Mathf.Cos(phase * Mathf.PI * 2f);
                return;
            }
            if (page.demo == TutorialDemo.Custom)
            {
                pressed = phase >= first && phase < Mathf.Max(first, page.customRelease);
                return;
            }
            if (phase < first || phase >= land) return;
            if (page.demo == TutorialDemo.DoubleJump)
            {
                // 2回目の直前に短い離し区間。2回目から着地までは押下を維持。
                pressed = phase < second - Mathf.Min(0.045f, (second - first) * 0.35f)
                    || phase >= second;
                if (phase < second)
                {
                    float t = Mathf.InverseLerp(first, second, phase);
                    y = 0.55f * Mathf.Sin(t * Mathf.PI * 0.7f);
                }
                else
                {
                    float t = Mathf.InverseLerp(second, land, phase);
                    float start = 0.55f * Mathf.Sin(Mathf.PI * 0.7f);
                    y = start * (1f - t) + 0.8f * 4f * t * (1f - t);
                }
            }
            else
            {
                pressed = true;
                y = Mathf.Sin(Mathf.InverseLerp(first, land, phase) * Mathf.PI);
            }
        }
    }
}
