//using UnityEngine;

///// <summary>
///// Cubemap表示の検証用に、カメラを前方向へ移動させる。
///// 移動距離に応じてShaderの表示方向も補正する。
///// </summary>
//public class CubemapForwardTest : MonoBehaviour
//{
//    [Header("移動設定")]

//    [SerializeField]
//    [Tooltip("カメラの移動速度")]
//    private float moveSpeed = 2.0f;

//    [SerializeField]
//    [Tooltip("最大移動距離")]
//    private float maxTravelDistance = 10.0f;

//    [Header("Cubemap設定")]

//    [SerializeField]
//    [Tooltip("CubemapSphereのRenderer")]
//    private Renderer sphereRenderer;

//    [SerializeField]
//    [Tooltip("Cubemapの補正強度")]
//    private float parallaxStrength = 0.02f;

//    private static readonly int ForwardOffsetID =
//        Shader.PropertyToID("_ForwardOffset");

//    private MaterialPropertyBlock propertyBlock;

//    private Vector3 startPosition;
//    private float traveledDistance;

//    private void Start()
//    {
//        startPosition = transform.position;

//        propertyBlock = new MaterialPropertyBlock();
//    }

//    private void Update()
//    {
//        MoveForward();
//        UpdateShaderOffset();
//    }

//    /// <summary>
//    /// カメラを前方向へ移動させる。
//    /// </summary>
//    private void MoveForward()
//    {
//        if (traveledDistance >= maxTravelDistance)
//        {
//            return;
//        }

//        float moveDistance =
//            moveSpeed * Time.deltaTime;

//        moveDistance = Mathf.Min(
//            moveDistance,
//            maxTravelDistance - traveledDistance
//        );

//        transform.position +=
//            transform.forward * moveDistance;

//        traveledDistance += moveDistance;
//    }

//    /// <summary>
//    /// カメラの移動距離をShaderへ渡す。
//    /// </summary>
//    private void UpdateShaderOffset()
//    {
//        if (sphereRenderer == null)
//        {
//            return;
//        }

//        sphereRenderer.GetPropertyBlock(propertyBlock);

//        float offset =
//            traveledDistance * parallaxStrength;

//        propertyBlock.SetFloat(
//            ForwardOffsetID,
//            offset
//        );

//        sphereRenderer.SetPropertyBlock(propertyBlock);
//    }
//}



using UnityEngine;

/// <summary>
/// 深度Cubemapを使用した視差表現を検証するため、
/// カメラを前方向へ移動させる。
/// </summary>
public class CubemapForwardTest : MonoBehaviour
{
    [Header("移動設定")]

    [SerializeField]
    [Tooltip("カメラの移動速度")]
    private float moveSpeed = 2.0f;

    [SerializeField]
    [Tooltip("最大移動距離")]
    private float maxTravelDistance = 10.0f;

    [Header("深度Cubemap設定")]

    [SerializeField]
    [Tooltip("CubemapSphereのRenderer")]
    private Renderer sphereRenderer;

    [SerializeField]
    [Tooltip("生成した深度Cubemap")]
    private Cubemap depthCubemap;

    [SerializeField]
    [Tooltip("Cubemap撮影時のカメラのワールド座標")]
    private Vector3 capturePosition = new Vector3(0, 0, -5);

    [SerializeField]
    [Tooltip("撮影時のFar Clip Plane")]
    private float depthFar = 1000.0f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("視差補正の強さ")]
    private float parallaxStrength = 1.0f;

    private static readonly int DepthCubeID =
        Shader.PropertyToID("_DepthCube");

    private static readonly int CapturePositionID =
        Shader.PropertyToID("_CapturePosition");

    private static readonly int DepthFarID =
        Shader.PropertyToID("_DepthFar");

    private static readonly int ParallaxStrengthID =
        Shader.PropertyToID("_ParallaxStrength");

    private MaterialPropertyBlock propertyBlock;

    private float traveledDistance;

    private void Start()
    {
        propertyBlock = new MaterialPropertyBlock();

        if (sphereRenderer == null)
        {
            Debug.LogError("Sphere Rendererが設定されていません。");
            enabled = false;
            return;
        }

        if (depthCubemap == null)
        {
            Debug.LogError("Depth Cubemapが設定されていません。");
            enabled = false;
            return;
        }

        // Shaderに深度Cubemapと撮影情報を渡す
        sphereRenderer.GetPropertyBlock(propertyBlock);

        propertyBlock.SetTexture(
            DepthCubeID,
            depthCubemap
        );

        propertyBlock.SetVector(
            CapturePositionID,
            capturePosition
        );

        propertyBlock.SetFloat(
            DepthFarID,
            depthFar
        );

        propertyBlock.SetFloat(
            ParallaxStrengthID,
            parallaxStrength
        );

        sphereRenderer.SetPropertyBlock(propertyBlock);
    }

    private void Update()
    {
        MoveForward();
    }

    /// <summary>
    /// カメラを前方向へ移動させる。
    /// </summary>
    private void MoveForward()
    {
        if (traveledDistance >= maxTravelDistance)
        {
            return;
        }

        float moveDistance =
            moveSpeed * Time.deltaTime;

        moveDistance = Mathf.Min(
            moveDistance,
            maxTravelDistance - traveledDistance
        );

        transform.position +=
            transform.forward * moveDistance;

        traveledDistance += moveDistance;
    }
}
