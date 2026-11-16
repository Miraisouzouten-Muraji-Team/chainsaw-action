using UnityEngine;

namespace Project.InGame.Boss
{
    public enum BossHitboxDebugScope
    {
        None,
        All,
        ActiveOnly
    }

    public enum BossHitboxDebugTiming
    {
        Always,
        DuringAttackWindow,
        OnActivation,
        OnActualHit
    }

    /// <summary>
    /// 当たり判定をいつ、どの範囲まで表示するか設定する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossHitboxDebugSettings : MonoBehaviour
    {
        [Header("表示対象")]
        [SerializeField]
        private BossHitboxDebugScope scope =
            BossHitboxDebugScope.ActiveOnly;

        [Header("表示タイミング")]
        [SerializeField]
        private BossHitboxDebugTiming timing =
            BossHitboxDebugTiming.DuringAttackWindow;

        [Header("表示内容")]
        [SerializeField] private bool showVerticalSlashPreview = true;
        [SerializeField] private bool showInSceneView = true;
        [SerializeField] private bool showInGameView = true;

        [Header("表示時間と線の太さ")]
        [SerializeField, Min(0.01f)] private float momentDuration = 0.2f;
        [SerializeField, Range(1f, 5f)] private float lineWidth = 2f;

        [Header("色")]
        [SerializeField] private Color activeColor = Color.red;
        [SerializeField] private Color inactiveColor = Color.yellow;
        [SerializeField] private Color previewColor = Color.cyan;

        public BossHitboxDebugScope Scope => scope;
        public BossHitboxDebugTiming Timing => timing;
        public bool ShowVerticalSlashPreview => showVerticalSlashPreview;
        public bool ShowInSceneView => showInSceneView;
        public bool ShowInGameView => showInGameView;
        public float MomentDuration => momentDuration;
        public float LineWidth => lineWidth;
        public Color ActiveColor => activeColor;
        public Color InactiveColor => inactiveColor;
        public Color PreviewColor => previewColor;
    }
}
