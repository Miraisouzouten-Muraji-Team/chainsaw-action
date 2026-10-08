using UnityEngine;

/// <summary>
/// チェーンソーの接触判定に使用する調整値をまとめて管理する。
/// シーン固有のTransformやCollider、実行中の検出結果は保持しない。
/// </summary>
[CreateAssetMenu(fileName = "ContactDetectorParameter", menuName = "Player/Contact Detector Parameter")]
public class ContactDetectorParameter : ScriptableObject
{
    [Header("判定対象レイヤー")]
    [Tooltip("床、壁、天井として判定するオブジェクトのレイヤー")]
    [SerializeField] private LayerMask terrainLayers;

    [Tooltip("食い込み可能な敵のレイヤー")]
    [SerializeField] private LayerMask enemyLayers;

    [Header("床・天井の判定")]
    [Tooltip("面の法線のY成分がこの値以上なら床、マイナスで以下なら天井と判定します。0.7なら約45度までが対象です")]
    [SerializeField, Range(0.1f, 0.95f)] private float surfaceNormalThreshold = 0.7f;

    [Tooltip("接触面の法線を調べるRayの開始位置を、判定範囲の外側へ戻す距離です。接触可能な範囲自体は広がりません")]
    [SerializeField, Min(0.01f)] private float normalRayBackoff = 0.25f;

    [Header("壁の判定")]
    [Tooltip("プレイヤー前方に出す壁判定Rayの長さです。壁Colliderと重なっていなくても検出できます")]
    [SerializeField, Min(0.01f)] private float frontRayDistance = 1f;

    public LayerMask TerrainLayers => terrainLayers;
    public LayerMask EnemyLayers => enemyLayers;
    public float SurfaceNormalThreshold => surfaceNormalThreshold;
    public float NormalRayBackoff => normalRayBackoff;
    public float FrontRayDistance => frontRayDistance;
}
