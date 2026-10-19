using System;
using System.Collections.Generic;
using UnityEngine;

// Animatorの指定レイヤーにあるステートに応じて、登録したBoxColliderの形を切り替える。
// 接地判定より先に形を反映する。Animatorの評価自体を早めるものではない。
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class MotionBoxColliderController : MonoBehaviour
{
    [Serializable]
    private sealed class ColliderShape
    {
        [Tooltip("このモーション中に形を変えるBoxColliderを指定してください。")]
        public BoxCollider targetCollider;

        [Tooltip("指定したBoxColliderのローカル座標での大きさです。すべての軸を0より大きくしてください。")]
        public Vector3 size = Vector3.one;

        [Tooltip("指定したBoxColliderのローカル座標での中心位置です。Transformの位置は変更しません。")]
        public Vector3 center;
    }

    [Serializable]
    private sealed class MotionShape
    {
        [Tooltip("Animatorのレイヤー名から始まるステートの完全な名前を入力してください。例：Base Layer.FloorDigging。クリップ名ではありません。")]
        public string stateFullPath;

        [Tooltip("このステート中に変更するBoxColliderと、その大きさ・中心位置を登録してください。複数登録できます。")]
        public ColliderShape[] colliders = new ColliderShape[0];
    }

    private struct OriginalShape
    {
        public Vector3 Size;
        public Vector3 Center;
    }

    [Header("監視するアニメーション")]
    [Tooltip("実際にモーションを再生するAnimatorを指定してください。未設定なら同じGameObjectから取得します。")]
    [SerializeField] private Animator targetAnimator;

    [Tooltip("体の姿勢を決めるAnimatorレイヤーの番号です。通常はBase Layerの0を指定します。")]
    [SerializeField, Min(0)] private int animatorLayer;

    [Header("モーションごとのBoxCollider設定")]
    [Tooltip("ステートごとに変更するColliderの形を登録してください。未登録のステートでは再生開始時の形へ戻します。設定は再生前に行ってください。")]
    [SerializeField] private MotionShape[] motionShapes = new MotionShape[0];

    [Header("キャンセル時の復元")]
    [Tooltip("食い込みキャンセル後、元の大きさと中心位置へ戻るまでの秒数です。0なら即座に戻します。ゲーム内時間で進みます。")]
    [SerializeField, Min(0f)] private float cancelRestoreDuration = 0.5f;

    private readonly Dictionary<BoxCollider, OriginalShape> originalShapes =
        new Dictionary<BoxCollider, OriginalShape>();

    private readonly Dictionary<int, MotionShape> shapesByState =
        new Dictionary<int, MotionShape>();

    private MotionShape activeShape;
    private bool initialized;
    private readonly HashSet<int> cancelledStateHashes = new HashSet<int>();
    private readonly Dictionary<BoxCollider, OriginalShape> restoreStartShapes =
        new Dictionary<BoxCollider, OriginalShape>();
    private bool isRestoring;
    private float restoreElapsed;

    // 参照と設定を確認し、変更対象のColliderの元の形を一度だけ保存する。
    private void Awake()
    {
        if (targetAnimator == null)
        {
            targetAnimator = GetComponent<Animator>();
        }

        if (targetAnimator == null)
        {
            Debug.LogError("モーション別Collider：Animatorを設定してください。", this);
            enabled = false;
            return;
        }

        if (!CacheShapes())
        {
            enabled = false;
            return;
        }

        initialized = true;
    }

    // ステート名・対象・大きさを検証し、検索用の設定と復元用の形を保存する。
    private bool CacheShapes()
    {
        if (motionShapes == null)
        {
            return true;
        }

        foreach (MotionShape motion in motionShapes)
        {
            if (motion == null || string.IsNullOrWhiteSpace(motion.stateFullPath))
            {
                Debug.LogError("モーション別Collider：ステートの完全な名前を設定してください。", this);
                return false;
            }

            int stateHash = Animator.StringToHash(motion.stateFullPath);
            if (shapesByState.ContainsKey(stateHash))
            {
                Debug.LogError("モーション別Collider：ステート名が重複しています：" + motion.stateFullPath, this);
                return false;
            }

            HashSet<BoxCollider> usedColliders = new HashSet<BoxCollider>();
            if (motion.colliders != null)
            {
                foreach (ColliderShape shape in motion.colliders)
                {
                    if (shape == null || shape.targetCollider == null ||
                        !IsValidSize(shape.size) || !IsFiniteVector(shape.center))
                    {
                        Debug.LogError("モーション別Collider：対象のBoxCollider、大きさ、中心位置を確認してください：" + motion.stateFullPath, this);
                        return false;
                    }

                    if (!usedColliders.Add(shape.targetCollider))
                    {
                        Debug.LogError("モーション別Collider：同じステート内でColliderが重複しています：" + motion.stateFullPath, this);
                        return false;
                    }

                    if (!originalShapes.ContainsKey(shape.targetCollider))
                    {
                        originalShapes.Add(shape.targetCollider, new OriginalShape
                        {
                            Size = shape.targetCollider.size,
                            Center = shape.targetCollider.center
                        });
                    }
                }
            }

            shapesByState.Add(stateHash, motion);
        }

        return true;
    }

    // Animatorを物理更新ごとに確認し、ステートが変わったときだけ形を切り替える。
    private void FixedUpdate()
    {
        if (!initialized)
        {
            return;
        }

        // Animatorが停止・切り替え中でも、開始済みの復元は物理更新で進める。
        TickCancelRestore(Time.fixedDeltaTime);
        // Idleなどの登録があっても、復元途中で形を上書きしない。
        if (isRestoring)
        {
            return;
        }

        if (targetAnimator == null || !targetAnimator.isActiveAndEnabled ||
            !targetAnimator.isInitialized || animatorLayer < 0 ||
            animatorLayer >= targetAnimator.layerCount)
        {
            RestoreOriginalShapes();
            return;
        }

        // ブレンド中は遷移先の形を採用する。ループやアニメーション停止中も維持する。
        AnimatorStateInfo state = targetAnimator.IsInTransition(animatorLayer)
            ? targetAnimator.GetNextAnimatorStateInfo(animatorLayer)
            : targetAnimator.GetCurrentAnimatorStateInfo(animatorLayer);

        // キャンセル後に同じモーションの続きを再生しても、食い込み用の形を再適用しない。
        if (cancelledStateHashes.Contains(state.fullPathHash))
        {
            return;
        }

        cancelledStateHashes.Clear();
        shapesByState.TryGetValue(state.fullPathHash, out MotionShape nextShape);
        if (activeShape == nextShape)
        {
            return;
        }

        // 前のモーションだけが使用したColliderも、ここで元の形へ戻す。
        RestoreOriginalShapes();
        if (nextShape == null)
        {
            return;
        }

        if (nextShape.colliders != null)
        {
            foreach (ColliderShape shape in nextShape.colliders)
            {
                if (shape.targetCollider == null)
                {
                    continue;
                }

                shape.targetCollider.size = shape.size;
                shape.targetCollider.center = shape.center;
            }
        }

        activeShape = nextShape;
    }

    // キャンセルされたモーションの再適用を止め、元の大きさと中心へ戻す補間を開始する。
    public void RestoreCancelledMotion()
    {
        if (!initialized)
        {
            return;
        }

        if (activeShape != null)
        {
            cancelledStateHashes.Add(Animator.StringToHash(activeShape.stateFullPath));
        }

        if (targetAnimator != null && targetAnimator.isInitialized &&
            animatorLayer >= 0 && animatorLayer < targetAnimator.layerCount)
        {
            cancelledStateHashes.Add(
                targetAnimator.GetCurrentAnimatorStateInfo(animatorLayer).fullPathHash);

            if (targetAnimator.IsInTransition(animatorLayer))
            {
                cancelledStateHashes.Add(
                    targetAnimator.GetNextAnimatorStateInfo(animatorLayer).fullPathHash);
            }
        }

        BeginCancelRestore();
    }

    // 現在の形を始点に保存する。同じキャンセルの繰り返しでは復元時間を延長しない。
    private void BeginCancelRestore()
    {
        if (isRestoring || activeShape == null)
        {
            return;
        }

        if (cancelRestoreDuration <= 0f || !isActiveAndEnabled)
        {
            RestoreOriginalShapes();
            return;
        }

        restoreStartShapes.Clear();
        foreach (KeyValuePair<BoxCollider, OriginalShape> entry in originalShapes)
        {
            if (entry.Key == null)
            {
                continue;
            }

            restoreStartShapes.Add(entry.Key, new OriginalShape
            {
                Size = entry.Key.size,
                Center = entry.Key.center
            });
        }

        restoreElapsed = 0f;
        isRestoring = true;
        activeShape = null;
    }

    // 保存したキャンセル時の形から元の形へ、指定時間で直線的に補間する。
    private void TickCancelRestore(float deltaTime)
    {
        if (!isRestoring)
        {
            return;
        }

        restoreElapsed += deltaTime;
        float progress = cancelRestoreDuration > 0f
            ? Mathf.Clamp01(restoreElapsed / cancelRestoreDuration)
            : 1f;

        foreach (KeyValuePair<BoxCollider, OriginalShape> entry in restoreStartShapes)
        {
            if (entry.Key == null)
            {
                continue;
            }

            OriginalShape original = originalShapes[entry.Key];
            entry.Key.size = Vector3.Lerp(entry.Value.Size, original.Size, progress);
            entry.Key.center = Vector3.Lerp(entry.Value.Center, original.Center, progress);
        }

        if (progress >= 1f)
        {
            RestoreOriginalShapes();
        }
    }

    // 次の食い込みを要求したら復元を終了し、新しいモーションの形を適用可能にする。
    public void AllowMotionShapes()
    {
        if (isRestoring)
        {
            RestoreOriginalShapes();
        }

        cancelledStateHashes.Clear();
    }

    // 無効化後は物理更新できないため、途中の形を残さず即座に元へ戻す。
    private void OnDisable()
    {
        RestoreOriginalShapes();
    }

    // 最初に保存した大きさと中心位置を復元し、適用中のモーションを解除する。
    private void RestoreOriginalShapes()
    {
        if (activeShape == null && !isRestoring)
        {
            return;
        }

        foreach (KeyValuePair<BoxCollider, OriginalShape> entry in originalShapes)
        {
            if (entry.Key == null)
            {
                continue;
            }

            entry.Key.size = entry.Value.Size;
            entry.Key.center = entry.Value.Center;
        }

        activeShape = null;
        isRestoring = false;
        restoreElapsed = 0f;
        restoreStartShapes.Clear();
    }

    // Colliderの大きさとして使用できる、有限かつ正の値かを確認する。
    private static bool IsValidSize(Vector3 size)
    {
        return IsFiniteVector(size) && size.x > 0f && size.y > 0f && size.z > 0f;
    }

    // 座標の各成分に、計算不能な値や無限大が含まれていないかを確認する。
    private static bool IsFiniteVector(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
