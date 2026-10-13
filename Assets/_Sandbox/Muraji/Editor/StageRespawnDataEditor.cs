using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StageRespawnData))]
public sealed class StageRespawnDataEditor : Editor
{
    // Transformは編集画面で一時的に使い、SOには保存しない。
    private Transform sourceTransform;
    private int areaNumber = 1;

    // 通常の設定欄と、Transformから座標を取り込む操作欄を表示する。
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Transformから復活座標を設定",
            EditorStyles.boldLabel);

        areaNumber = Mathf.Max(
            1,
            EditorGUILayout.IntField(
                new GUIContent(
                    "設定先のエリア番号",
                    "座標を変更する、SOに登録済みのエリア番号です。"),
                areaNumber));

        sourceTransform = (Transform)EditorGUILayout.ObjectField(
            new GUIContent(
                "復活地点のTransform",
                "シーン上に配置した復活地点を指定してください。"
                + "ワールド座標だけをSOへコピーします。"),
            sourceTransform,
            typeof(Transform),
            true);

        using (new EditorGUI.DisabledScope(
            sourceTransform == null || EditorApplication.isPlaying))
        {
            if (GUILayout.Button("この位置をSOにコピー"))
            {
                CopyPosition();
            }
        }

        EditorGUILayout.HelpBox(
            "座標はボタンを押した時点でコピーされます。"
            + "復活地点を動かした場合は、もう一度コピーしてください。"
            + "復活時の左右の向きはSO側で設定します。",
            MessageType.Info);
    }

    // 指定エリアの座標だけを変更する。Undoにも対応する。
    private void CopyPosition()
    {
        serializedObject.Update();

        SerializedProperty points =
            serializedObject.FindProperty("respawnPoints");

        if (points == null || !points.isArray)
        {
            Debug.LogError(
                "StageRespawnDataのrespawnPointsが見つかりません。",
                target);
            return;
        }

        SerializedProperty matchedPoint = null;

        for (int pointIndex = 0;
             pointIndex < points.arraySize;
             pointIndex++)
        {
            SerializedProperty point =
                points.GetArrayElementAtIndex(pointIndex);

            SerializedProperty number =
                point.FindPropertyRelative("areaNumber");

            if (number == null || number.intValue != areaNumber)
                continue;

            if (matchedPoint != null)
            {
                Debug.LogError(
                    $"エリア番号{areaNumber}が重複しています。",
                    target);
                return;
            }

            matchedPoint = point;
        }

        if (matchedPoint == null)
        {
            Debug.LogError(
                $"エリア番号{areaNumber}を先にSOへ登録してください。",
                target);
            return;
        }

        SerializedProperty position =
            matchedPoint.FindPropertyRelative("position");

        if (position == null)
        {
            Debug.LogError("復活座標のpositionが見つかりません。", target);
            return;
        }

        position.vector3Value = sourceTransform.position;
        serializedObject.ApplyModifiedProperties();
    }
}
