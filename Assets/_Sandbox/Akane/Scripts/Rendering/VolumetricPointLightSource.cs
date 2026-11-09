using System.Collections.Generic;
using UnityEngine;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Point / Spot Lightを体積照明へ登録するための設定。
    /// Priorityが高いライトほど先に計算し、同じ優先度ではInstanceIDで順序を固定する。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class VolumetricPointLightSource : MonoBehaviour
    {
        private static readonly HashSet<VolumetricPointLightSource> SOURCES = new();

        [SerializeField, Min(0f)]
        private float _volumetricIntensity = 1f;

        [SerializeField]
        private bool _castVolumetricShadows = true;

        [SerializeField, Range(-100, 100)]
        private int _priority = 0;

        private Light _light;

        public static IEnumerable<VolumetricPointLightSource> Sources => SOURCES;
        public float VolumetricIntensity => _volumetricIntensity;
        public bool CastVolumetricShadows => _castVolumetricShadows;
        public int Priority => _priority;

        public Light SourceLight
        {
            get
            {
                if (_light == null)
                {
                    _light = GetComponent<Light>();
                }

                return _light;
            }
        }

        private void OnEnable()
        {
            _light = GetComponent<Light>();
            SOURCES.Add(this);
        }

        private void OnDisable()
        {
            SOURCES.Remove(this);
        }

        /// <summary>
        /// 現在のカメラで使用する候補として有効な状態かを判定する。
        /// カメラから見えるかどうかはRenderer Feature側で別途判定する。
        /// </summary>
        public bool IsValid()
        {
            Light sourceLight = SourceLight;
            if (!isActiveAndEnabled ||
                sourceLight == null ||
                !sourceLight.isActiveAndEnabled)
            {
                return false;
            }

            bool supportedType =
                sourceLight.type == LightType.Point ||
                sourceLight.type == LightType.Spot;

            return supportedType &&
                   sourceLight.intensity > 0f &&
                   sourceLight.range > 0f &&
                   _volumetricIntensity > 0f;
        }

        private void OnValidate()
        {
            _volumetricIntensity = Mathf.Max(0f, _volumetricIntensity);
        }
    }
}
