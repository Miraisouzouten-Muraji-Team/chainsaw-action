
using System.Collections.Generic;
using UnityEngine;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Point LightとSpot Lightを体積照明の対象として登録する。
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

        private Light _light;

        public static IEnumerable<VolumetricPointLightSource> Sources => SOURCES;

        public float VolumetricIntensity => _volumetricIntensity;

        public bool CastVolumetricShadows => _castVolumetricShadows;

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
        /// 体積照明に使用できるライトか判定する。
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
                   sourceLight.range > 0f &&
                   _volumetricIntensity > 0f;
        }

        private void OnValidate()
        {
            _volumetricIntensity = Mathf.Max(
                0f, _volumetricIntensity);
        }
    }
}
