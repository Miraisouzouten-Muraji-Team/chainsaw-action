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
        internal static readonly List<TutorialZone> Active = new List<TutorialZone>();
        private readonly Dictionary<Collider, TutorialPlayer> contacts = new Dictionary<Collider, TutorialPlayer>();
        private readonly List<Collider> stale = new List<Collider>();
        public Vector3 AnchorPosition => signAnchor != null ? signAnchor.position : transform.position + anchorOffset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Active.Clear(); }
        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        private void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            TutorialPresenter.EnsureExists();
        }
        private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        private void OnDisable() { Active.Remove(this); contacts.Clear(); }
        private void OnTriggerEnter(Collider other) { Track(other); }
        private void OnTriggerStay(Collider other) { Track(other); }
        private void Track(Collider other)
        {
            var player = other.GetComponentInParent<TutorialPlayer>();
            if (player != null && player.isActiveAndEnabled) contacts[other] = player;
        }
        private void OnTriggerExit(Collider other) { contacts.Remove(other); }

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
