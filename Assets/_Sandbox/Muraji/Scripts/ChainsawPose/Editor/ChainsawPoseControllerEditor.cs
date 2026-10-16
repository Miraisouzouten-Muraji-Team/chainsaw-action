using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(ChainsawPoseController))]
public sealed class ChainsawPoseControllerEditor : Editor
{
    // Playerでは参照だけを設定し、姿勢の編集先をSOへ一本化します。
    public override void OnInspectorGUI()
    {
        ChainsawPoseController controller = (ChainsawPoseController)target;
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        DrawReference("player", "プレイヤー制御");
        DrawReference("digging", "食い込み制御");
        DrawReference("playerRoot", "Playerのルート");
        DrawReference("poseTarget", "チェーンソーのルート");
        DrawReference("poseData", "姿勢データ（SO）");
        DrawReference("leftFacingEuler", "左向きへの変換角度");
        DrawReference("moveThreshold", "移動姿勢にする速度");
        if (EditorGUI.EndChangeCheck()) ChainsawPosePreview.StopPreview();
        serializedObject.ApplyModifiedProperties();

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("現在の状態：" + ChainsawPoseEditorLabels.GetStateLabel(controller.CurrentState) +
                " / " + controller.CurrentPoseName, MessageType.Info);
            return;
        }
        EditorGUILayout.HelpBox("チェーンソーはPlayerの直接の子にします。SOでPlayer基準の開始・終了姿勢と動作時間を設定します。", MessageType.Info);
        if (!controller.IsTargetValid())
        {
            EditorGUILayout.HelpBox("Playerのルートと、その直接の子になっている武器ルートを指定してください。手の子から外してください。", MessageType.Warning);
            return;
        }
        if (controller.PoseData == null)
        {
            if (GUILayout.Button("現在のPlayer基準の配置からSOを新規作成")) CreateData(controller);
        }
        else if (GUILayout.Button("このプレイヤーを対象にSOを開く"))
        {
            ChainsawPosePreview.SetController(controller);
            Selection.activeObject = controller.PoseData;
        }
    }

    // 日本語の項目名で既存のシリアライズ設定を表示します。
    private void DrawReference(string propertyName, string label)
    {
        EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName), new GUIContent(label));
    }

    // 現在のPlayer基準の配置から全状態を生成し、Playerへ割り当てます。
    private void CreateData(ChainsawPoseController controller)
    {
        string path = EditorUtility.SaveFilePanelInProject("姿勢SOの保存先", "ChainsawPlayerMotionData", "asset", "保存先を選択してください。");
        if (string.IsNullOrEmpty(path)) return;
        ChainsawPoseData data = CreateInstance<ChainsawPoseData>();
        ChainsawPoseEditorLabels.AddInitialPoses(data, controller.PoseTarget);
        AssetDatabase.CreateAsset(data, path);
        serializedObject.Update();
        serializedObject.FindProperty("poseData").objectReferenceValue = data;
        serializedObject.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        ChainsawPosePreview.SetController(controller);
        Selection.activeObject = data;
    }
}

[CustomEditor(typeof(ChainsawPoseData))]
public sealed class ChainsawPoseDataEditor : Editor
{
    private int selectedIndex;
    private bool editingEnd;

    // 別のSOを開いた場合は、以前のSOのプレビューを終了します。
    private void OnEnable()
    {
        if (ChainsawPosePreview.Data != null && ChainsawPosePreview.Data != target)
            ChainsawPosePreview.StopPreview();
    }

    // SOの編集を終了したときに、一時的な姿勢を元に戻します。
    private void OnDisable()
    {
        if (ChainsawPosePreview.Data == target) ChainsawPosePreview.StopPreview();
    }

    // SO上でPlayer基準の開始・終了姿勢と、Prefabモードのプレビューを編集します。
    public override void OnInspectorGUI()
    {
        ChainsawPoseData data = (ChainsawPoseData)target;
        EditorGUILayout.HelpBox("開始・終了は右向きのPlayer基準です。開始→終了を再生する場合はONにしてください。手基準の旧SOからは新規作成し直してください。", MessageType.Info);
        if (!Application.isPlaying) DrawPreviewTarget(data);

        if (data.Poses == null || data.Poses.Count == 0)
        {
            EditorGUILayout.HelpBox("姿勢がありません。対象Playerを指定して現在の配置から初期化してください。", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying || !IsControllerReady(data)))
            {
                if (GUILayout.Button("現在のPlayer基準の配置で13状態を作成"))
                    ChainsawPoseEditorLabels.AddInitialPoses(data, ChainsawPosePreview.Controller.PoseTarget);
            }
            return;
        }

        string[] names = BuildNames(data);
        selectedIndex = Mathf.Clamp(selectedIndex, 0, names.Length - 1);
        EditorGUI.BeginChangeCheck();
        selectedIndex = EditorGUILayout.Popup("調整する状態", selectedIndex, names);
        if (EditorGUI.EndChangeCheck()) { editingEnd = false; ChainsawPosePreview.ShowPose(selectedIndex, 0f); }
        if (ChainsawPosePreview.IsActive && ChainsawPosePreview.Data == data)
            editingEnd = data.Poses[selectedIndex].PlayMotion && ChainsawPosePreview.Progress >= 1f;

        serializedObject.Update();
        SerializedProperty entry = serializedObject.FindProperty("Poses").GetArrayElementAtIndex(selectedIndex);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(entry.FindPropertyRelative("DisplayName"), new GUIContent("姿勢の名前"));
        SerializedProperty state = entry.FindPropertyRelative("State");
        state.intValue = EditorGUILayout.Popup(new GUIContent("使用する状態"), state.intValue, ChainsawPoseEditorLabels.STATE_LABELS);
        if (state.intValue == (int)ChainsawPoseState.GroundAttack || state.intValue == (int)ChainsawPoseState.AirAttack)
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("AttackFilter"), new GUIContent("対象の攻撃データ", "空欄は共通姿勢。段別にする場合は既存ControllerのSlash1～3と同じAttackDataを指定します。"));
        EditorGUILayout.PropertyField(entry.FindPropertyRelative("LocalPosition"), new GUIContent("開始位置（Player基準）"));
        EditorGUILayout.PropertyField(entry.FindPropertyRelative("LocalEulerAngles"), new GUIContent("開始角度（度）"));
        EditorGUILayout.PropertyField(entry.FindPropertyRelative("BlendSeconds"), new GUIContent("直前の姿勢→開始姿勢の秒数"));
        SerializedProperty playMotion = entry.FindPropertyRelative("PlayMotion");
        EditorGUILayout.PropertyField(playMotion, new GUIContent("開始→終了を再生する"));
        if (playMotion.boolValue)
        {
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("EndLocalPosition"), new GUIContent("終了位置（Player基準）"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("EndLocalEulerAngles"), new GUIContent("終了角度（度）"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("MotionSeconds"), new GUIContent("開始→終了の秒数"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("ProgressCurve"), new GUIContent("進み方のカーブ"));
        }
        else editingEnd = false;
        bool changed = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();
        if (changed) ChainsawPosePreview.ShowPose(selectedIndex, editingEnd ? 1f : 0f);

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("この姿勢を複製して追加")) DuplicatePose(data);
            if (GUILayout.Button("この姿勢を削除"))
            {
                ChainsawPosePreview.StopPreview();
                Undo.RecordObject(data, "チェーンソー姿勢を削除");
                data.Poses.RemoveAt(selectedIndex);
                EditorUtility.SetDirty(data);
                return;
            }
            using (new EditorGUI.DisabledScope(!IsControllerReady(data) || ChainsawPosePreview.IsActive))
            {
                if (GUILayout.Button(editingEnd ? "武器の現在のTransformを終了姿勢に取り込む" : "武器の現在のTransformを開始姿勢に取り込む"))
                {
                    Undo.RecordObject(data, "現在のPlayer基準姿勢を取り込む");
                    Transform weapon = ChainsawPosePreview.Controller.PoseTarget;
                    if (editingEnd)
                    {
                        data.Poses[selectedIndex].EndLocalPosition = weapon.localPosition;
                        data.Poses[selectedIndex].EndLocalEulerAngles = weapon.localEulerAngles;
                    }
                    else
                    {
                        data.Poses[selectedIndex].LocalPosition = weapon.localPosition;
                        data.Poses[selectedIndex].LocalEulerAngles = weapon.localEulerAngles;
                    }
                    EditorUtility.SetDirty(data);
                }
            }
            if (GUILayout.Button("SOを保存")) AssetDatabase.SaveAssets();
            DrawPreviewControls(data);
        }
        ShowDuplicateWarning(data);
    }

    // SOに保存しない対象Player参照を受け取り、他のSOとの取り違えを知らせます。
    private void DrawPreviewTarget(ChainsawPoseData data)
    {
        ChainsawPoseController current = ChainsawPosePreview.Controller;
        ChainsawPoseController next = EditorGUILayout.ObjectField(new GUIContent("確認するPlayer", "シーンまたはPrefabモードのPlayerにあるChainsawPoseControllerを指定します。SOには保存しません。"),
            current, typeof(ChainsawPoseController), true) as ChainsawPoseController;
        if (next != current) ChainsawPosePreview.SetController(next);
        if (next != null && next.PoseData != data)
            EditorGUILayout.HelpBox("指定Playerの姿勢データに、このSOを割り当ててください。", MessageType.Warning);
        if (!string.IsNullOrEmpty(ChainsawPosePreview.ErrorMessage))
            EditorGUILayout.HelpBox(ChainsawPosePreview.ErrorMessage, MessageType.Warning);
    }

    // 編集中SOが対象Playerに割り当てられ、武器の参照が有効か確認します。
    private bool IsControllerReady(ChainsawPoseData data)
    {
        ChainsawPoseController controller = ChainsawPosePreview.Controller;
        return controller != null && controller.PoseData == data && controller.IsTargetValid();
    }

    // 開始・終了の編集先と、状態内のモーション再生を操作します。
    private void DrawPreviewControls(ChainsawPoseData data)
    {
        EditorGUILayout.Space();
        ChainsawPoseEntry pose = data.Poses[selectedIndex];
        if (pose.PlayMotion)
        {
            EditorGUI.BeginChangeCheck();
            editingEnd = GUILayout.Toolbar(editingEnd ? 1 : 0, new[] { "開始姿勢を調整", "終了姿勢を調整" }) == 1;
            if (EditorGUI.EndChangeCheck()) ChainsawPosePreview.ShowPose(selectedIndex, editingEnd ? 1f : 0f);
        }
        if (!ChainsawPosePreview.IsActive || ChainsawPosePreview.Data != data)
        {
            using (new EditorGUI.DisabledScope(!IsControllerReady(data)))
            {
                if (GUILayout.Button("この状態を武器にプレビュー"))
                {
                    ChainsawPosePreview.BeginPreview(data, selectedIndex);
                    ChainsawPosePreview.ShowPose(selectedIndex, editingEnd ? 1f : 0f);
                }
            }
            return;
        }
        EditorGUILayout.HelpBox("武器ルート自体に姿勢を表示しています。W移動・E回転の専用ハンドルはSOを直接編集します。終了時は元のTransformへ戻ります。", MessageType.Info);
        if (GUILayout.Button("プレビュー終了・元の配置へ戻す")) ChainsawPosePreview.StopPreview();
        if (!ChainsawPosePreview.IsActive) return;
        EditorGUI.BeginChangeCheck();
        bool faceLeft = EditorGUILayout.Toggle("左向きを確認", ChainsawPosePreview.FaceLeft);
        if (EditorGUI.EndChangeCheck()) ChainsawPosePreview.SetFacing(faceLeft);
        if (!pose.PlayMotion) return;
        EditorGUI.BeginChangeCheck();
        float amount = EditorGUILayout.Slider("開始→終了の進行度", ChainsawPosePreview.Progress, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            editingEnd = amount >= 1f;
            ChainsawPosePreview.ShowPose(selectedIndex, amount);
        }
        if (GUILayout.Button("開始→終了を設定時間で再生")) ChainsawPosePreview.PlayMotion(selectedIndex);
        if (ChainsawPosePreview.Progress > 0f && ChainsawPosePreview.Progress < 1f)
            EditorGUILayout.HelpBox("途中の姿勢は確認用です。ハンドル編集は開始0または終了1で行います。", MessageType.None);
    }

    // 姿勢選択用の一覧を作ります。
    private string[] BuildNames(ChainsawPoseData data)
    {
        string[] names = new string[data.Poses.Count];
        for (int i = 0; i < names.Length; i++) names[i] = i + ": " + data.Poses[i].DisplayName;
        return names;
    }

    // 選択した姿勢を複製し、状態や攻撃段別の設定を追加できるようにします。
    private void DuplicatePose(ChainsawPoseData data)
    {
        ChainsawPosePreview.StopPreview();
        Undo.RecordObject(data, "チェーンソー姿勢を複製");
        ChainsawPoseEntry source = data.Poses[selectedIndex];
        data.Poses.Add(new ChainsawPoseEntry
        {
            DisplayName = source.DisplayName + " コピー", State = source.State, AttackFilter = source.AttackFilter,
            LocalPosition = source.LocalPosition, LocalEulerAngles = source.LocalEulerAngles, BlendSeconds = source.BlendSeconds,
            PlayMotion = source.PlayMotion, EndLocalPosition = source.EndLocalPosition, EndLocalEulerAngles = source.EndLocalEulerAngles,
            MotionSeconds = source.MotionSeconds,
            ProgressCurve = source.ProgressCurve != null ? new AnimationCurve(source.ProgressCurve.keys)
                { preWrapMode = source.ProgressCurve.preWrapMode, postWrapMode = source.ProgressCurve.postWrapMode } : null
        });
        selectedIndex = data.Poses.Count - 1;
        EditorUtility.SetDirty(data);
    }

    // 同じ選択条件の重複で設定が使われなくなることを知らせます。
    private void ShowDuplicateWarning(ChainsawPoseData data)
    {
        for (int i = 0; i < data.Poses.Count; i++)
        {
            for (int j = i + 1; j < data.Poses.Count; j++)
            {
                ChainsawPoseEntry a = data.Poses[i];
                ChainsawPoseEntry b = data.Poses[j];
                bool isAttack = a.State == ChainsawPoseState.GroundAttack || a.State == ChainsawPoseState.AirAttack;
                if (a.State == b.State && (!isAttack || a.AttackFilter == b.AttackFilter))
                {
                    EditorGUILayout.HelpBox("同じ状態・攻撃データが重複しています。実行時は先に登録した姿勢を使います。複製後は条件を変更してください。", MessageType.Warning);
                    return;
                }
            }
        }
    }
}

internal static class ChainsawPoseEditorLabels
{
    internal static readonly string[] STATE_LABELS =
    {
        "待機", "移動", "空中・上昇", "空中・下降", "地上攻撃", "空中攻撃",
        "食い込み要求・接触待ち", "床への食い込み", "壁への食い込み",
        "天井への食い込み", "敵への食い込み", "回避", "食い込み強衝突の硬直"
    };

    // 姿勢分類を日本語へ変換します。
    internal static string GetStateLabel(ChainsawPoseState state)
    {
        int index = (int)state;
        return index >= 0 && index < STATE_LABELS.Length ? STATE_LABELS[index] : state.ToString();
    }

    // Player基準の現在配置を開始と終了の両方へ登録します。
    internal static void AddInitialPoses(ChainsawPoseData data, Transform weapon)
    {
        Undo.RecordObject(data, "Player基準の初期姿勢を作成");
        if (data.Poses == null) data.Poses = new List<ChainsawPoseEntry>();
        foreach (ChainsawPoseState state in Enum.GetValues(typeof(ChainsawPoseState)))
            data.Poses.Add(new ChainsawPoseEntry
            {
                State = state, DisplayName = GetStateLabel(state),
                LocalPosition = weapon.localPosition, LocalEulerAngles = weapon.localEulerAngles,
                EndLocalPosition = weapon.localPosition, EndLocalEulerAngles = weapon.localEulerAngles
            });
        EditorUtility.SetDirty(data);
    }
}

// 実際の武器をAnimationModeで一時表示し、Prefab保存時も元の配置へ復元します。
[InitializeOnLoad]
internal static class ChainsawPosePreview
{
    internal static ChainsawPoseController Controller { get; private set; }
    internal static ChainsawPoseData Data { get; private set; }
    internal static bool IsActive => ownsAnimationMode && AnimationMode.InAnimationMode();
    internal static float Progress { get; private set; }
    internal static bool FaceLeft { get; private set; }
    internal static string ErrorMessage { get; private set; }

    private static Transform weapon;
    private static Transform originalParent;
    private static AnimationClip clip;
    private static bool ownsAnimationMode;
    private static bool toolsWereHidden;
    private static bool playing;
    private static int selectedIndex;
    private static double playStarted;

    // Editorの終了経路とScene操作をまとめて登録します。
    static ChainsawPosePreview()
    {
        SceneView.duringSceneGui += DrawSceneHandles;
        EditorApplication.update += TickPreview;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        EditorApplication.quitting += StopPreview;
        AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
        EditorSceneManager.sceneSaving += HandleSceneSaving;
        PrefabStage.prefabSaving += HandlePrefabSaving;
        PrefabStage.prefabStageClosing += HandlePrefabClosing;
        Undo.undoRedoPerformed += ApplyPreview;
    }

    // 表示対象を変更し、以前の一時姿勢を終了します。
    internal static void SetController(ChainsawPoseController controller)
    {
        StopPreview();
        Controller = controller;
        ErrorMessage = null;
    }

    // 通常SceneとPrefabモードの実体だけを対象に、復元可能な一時表示を開始します。
    internal static void BeginPreview(ChainsawPoseData data, int index)
    {
        StopPreview();
        ErrorMessage = null;
        if (Application.isPlaying || Controller == null || !Controller.IsTargetValid() || Controller.PoseData != data) return;
        if (EditorUtility.IsPersistent(Controller) || !Controller.gameObject.scene.IsValid() || !Controller.gameObject.scene.isLoaded)
        {
            ErrorMessage = "Prefabアセットは開いてPrefabモードのPlayerを指定してください。通常SceneのPlayerも指定できます。";
            return;
        }
        if (AnimationMode.InAnimationMode())
        {
            ErrorMessage = "Animationウィンドウなど、別のアニメーションプレビューを終了してから開始してください。";
            return;
        }
        weapon = Controller.PoseTarget;
        Animator animator = weapon.GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController == null)
        {
            ErrorMessage = "武器ルートにController未設定のAnimatorがあります。不要なら外すか、正しいAnimator Controllerを設定してから確認してください。";
            weapon = null;
            return;
        }
        originalParent = weapon.parent;
        Data = data;
        clip = new AnimationClip { hideFlags = HideFlags.HideAndDontSave };
        AnimationMode.StartAnimationMode();
        ownsAnimationMode = true;
        toolsWereHidden = Tools.hidden;
        Tools.hidden = true;
        ShowPose(index);
    }

    // 一時表示を解除します。AnimationModeが武器の元のTransformを復元します。
    internal static void StopPreview()
    {
        playing = false;
        if (ownsAnimationMode)
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            Tools.hidden = toolsWereHidden;
        }
        ownsAnimationMode = false;
        if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
        clip = null;
        weapon = null;
        originalParent = null;
        Data = null;
        SceneView.RepaintAll();
    }

    // 指定した状態内の進行度で姿勢を表示します。
    internal static void ShowPose(int index, float progress = 0f)
    {
        playing = false;
        selectedIndex = index;
        Progress = Mathf.Clamp01(progress);
        ApplyPreview();
    }

    // SOを変更せず、Player基準の姿勢を左向きへ切り替えて確認します。
    internal static void SetFacing(bool faceLeft)
    {
        FaceLeft = faceLeft;
        ApplyPreview();
    }

    // 状態内の開始から終了までを設定秒数で一度再生します。
    internal static void PlayMotion(int index)
    {
        ShowPose(index, 0f);
        playStarted = EditorApplication.timeSinceStartup;
        playing = true;
    }

    // 参照と姿勢番号が引き続き有効かを確認します。
    private static bool CanPreview()
    {
        return IsActive && Controller != null && Controller.IsTargetValid() && Controller.PoseData == Data &&
            weapon != null && weapon == Controller.PoseTarget && weapon.parent == originalParent && Data != null &&
            Data.Poses != null && selectedIndex >= 0 && selectedIndex < Data.Poses.Count;
    }

    // 一時Clipに位置・回転だけを設定し、武器ルート自体のPivotへ反映します。
    private static void ApplyPreview()
    {
        if (!ownsAnimationMode) return;
        if (!CanPreview()) { StopPreview(); return; }
        Data.Poses[selectedIndex].Evaluate(Progress, out Vector3 basePosition, out Quaternion baseRotation);
        Controller.GetFacingPose(basePosition, baseRotation, FaceLeft, out Vector3 position, out Quaternion rotation);
        SetCurve("m_LocalPosition.x", position.x);
        SetCurve("m_LocalPosition.y", position.y);
        SetCurve("m_LocalPosition.z", position.z);
        SetCurve("m_LocalRotation.x", rotation.x);
        SetCurve("m_LocalRotation.y", rotation.y);
        SetCurve("m_LocalRotation.z", rotation.z);
        SetCurve("m_LocalRotation.w", rotation.w);
        AnimationMode.BeginSampling();
        try { AnimationMode.SampleAnimationClip(weapon.gameObject, clip, 0f); }
        finally { AnimationMode.EndSampling(); }
        SceneView.RepaintAll();
    }

    // 武器ルートのTransform用の定数カーブを作ります。
    private static void SetCurve(string property, float value)
    {
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), property),
            AnimationCurve.Constant(0f, 1f, value));
    }

    // Editorの時計で補間を進め、参照切れや外部プレビュー終了も検知します。
    private static void TickPreview()
    {
        if (!ownsAnimationMode) return;
        if (!CanPreview()) { StopPreview(); return; }
        if (!playing) return;
        float duration = Data.Poses[selectedIndex].MotionSeconds;
        Progress = duration > 0f ? Mathf.Clamp01((float)(EditorApplication.timeSinceStartup - playStarted) / duration) : 1f;
        if (Progress >= 1f) playing = false;
        ApplyPreview();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    // Playerを基準に開始または終了の武器姿勢を操作し、SOへ保存します。
    private static void DrawSceneHandles(SceneView sceneView)
    {
        if (!CanPreview() || playing) return;
        ChainsawPoseEntry pose = Data.Poses[selectedIndex];
        if (pose.PlayMotion && Progress > 0f && Progress < 1f) return;
        bool editEnd = pose.PlayMotion && Progress >= 1f;
        Handles.Label(weapon.position, "Player基準：" + pose.DisplayName + (editEnd ? " 終了" : " 開始") + "（W移動 / E回転）");
        Vector3 position = weapon.position;
        Quaternion rotation = weapon.rotation;
        EditorGUI.BeginChangeCheck();
        if (Tools.current == Tool.Rotate) rotation = Handles.RotationHandle(rotation, position);
        else position = Handles.PositionHandle(position, Tools.pivotRotation == PivotRotation.Local ? rotation : Quaternion.identity);
        if (!EditorGUI.EndChangeCheck()) return;
        Undo.RecordObject(Data, "Player基準の武器姿勢を調整");
        Controller.RemoveFacingPose(originalParent.InverseTransformPoint(position),
            Quaternion.Inverse(originalParent.rotation) * rotation, FaceLeft,
            out Vector3 localPosition, out Quaternion localRotation);
        if (editEnd)
        {
            pose.EndLocalPosition = localPosition;
            pose.EndLocalEulerAngles = localRotation.eulerAngles;
        }
        else
        {
            pose.LocalPosition = localPosition;
            pose.LocalEulerAngles = localRotation.eulerAngles;
        }
        EditorUtility.SetDirty(Data);
        ApplyPreview();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    // Scene保存前に元の武器配置へ戻します。
    private static void HandleSceneSaving(Scene scene, string path)
    {
        StopPreview();
    }

    // Prefabの自動保存・手動保存前に一時姿勢を解除します。
    private static void HandlePrefabSaving(GameObject root)
    {
        StopPreview();
    }

    // Prefabモードを閉じる前に一時姿勢を解除します。
    private static void HandlePrefabClosing(PrefabStage stage)
    {
        StopPreview();
    }

    // ゲーム実行前などに一時姿勢を解除します。
    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        StopPreview();
    }
}
