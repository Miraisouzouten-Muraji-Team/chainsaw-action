using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Debug/Runtime Debug Settings")]
public sealed class RuntimeDebugSettings : ScriptableObject
{
    [Tooltip("OFFなら通常ビルドではメニューを起動しません。")]
    public bool allowReleaseBuild = true;
    public bool showFPS = true;
    [Min(0.1f)] public float fpsInterval = 0.5f;
    public EnemyEntry[] enemies = new EnemyEntry[0];
    [Tooltip("グループ名は可視化コンポーネントのGroupと一致させる。")]
    public string[] groups = { "Player", "Enemy", "Sensor", "Stage" };
    [Tooltip("付属のRuntimeDebugLines.shaderから作ったMaterialを指定。")]
    public Material lineMaterial;
    [Serializable]
    public sealed class EnemyEntry
    {
        public string label;
        public GameObject prefab;
    }
}
