using System.Collections.Generic;
using UnityEngine;

namespace Project.Tutorial
{
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class TutorialZone : MonoBehaviour
    {
        public TutorialDefinition definition;
        [Tooltip("画面投影に使う実際の3Dカメラ。未指定ならCamera.main。")]
        public Camera projectionCamera;
        public Transform signAnchor;
        public Vector3 anchorOffset = new Vector3(0, 2.5f, 0);
        public int priority;
        [Tooltip("3D画像を画面の一部に合成する場合のみ指定。左下原点0～1。")]
        public Rect outputViewport = new Rect(0, 0, 1, 1);
        [Header("看板より手前に見せるステージ")]
        [Tooltip("看板を隠すステージのRendererを登録します。背景のRendererは登録しないでください。")]
        public Renderer[] stageOccluders = new Renderer[0];
        [Tooltip("子をまとめて登録する場合の親Transformです。床・壁などだけを含む親を指定してください。背景を含むシーン全体の親は指定しないでください。")]
        public Transform[] stageOccluderRoots = new Transform[0];
        internal static readonly List<TutorialZone> Active = new List<TutorialZone>();
        private readonly Dictionary<Collider, TutorialPlayer> contacts = new Dictionary<Collider, TutorialPlayer>();
        private readonly List<Collider> stale = new List<Collider>();
        public Vector3 AnchorPosition => signAnchor != null ? signAnchor.position : transform.position + anchorOffset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        // プレイ開始時にZoneの登録情報を初期化する。
        private static void ResetStatics() { Active.Clear(); }
        // コンポーネント追加時に判定をトリガーへ設定する。
        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        // トリガーを設定し、表示管理を準備する。
        private void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            TutorialPresenter.EnsureExists();
        }
        // 有効なZoneとして登録する。
        private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        // Zoneの登録と接触情報を解除する。
        private void OnDisable() { Active.Remove(this); contacts.Clear(); }
        // 範囲に入ったプレイヤーを登録する。
        private void OnTriggerEnter(Collider other) { Track(other); }
        // 範囲内のプレイヤーを継続して登録する。
        private void OnTriggerStay(Collider other) { Track(other); }
        // Colliderの親からプレイヤーを取得する。
        private void Track(Collider other)
        {
            var player = other.GetComponentInParent<TutorialPlayer>();
            if (player != null && player.isActiveAndEnabled) contacts[other] = player;
        }
        // 範囲から出たColliderを解除する。
        private void OnTriggerExit(Collider other) { contacts.Remove(other); }

        // 無効な接触を除去し、最も近いプレイヤーを返す。
        internal TutorialPlayer FindPlayer()
        {
            stale.Clear();
            TutorialPlayer nearest = null;
            float best = float.PositiveInfinity;
            foreach (var pair in contacts)
            {
                if (pair.Key == null || !pair.Key.enabled || !pair.Key.gameObject.activeInHierarchy ||
                    pair.Value == null || !pair.Value.isActiveAndEnabled)
                { stale.Add(pair.Key); continue; }
                float distance = (pair.Value.transform.position - transform.position).sqrMagnitude;
                if (distance < best) { best = distance; nearest = pair.Value; }
            }
            foreach (var key in stale) contacts.Remove(key);
            return nearest;
        }
    }
}
