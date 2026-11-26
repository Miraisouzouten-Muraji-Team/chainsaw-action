using UnityEngine;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// CPU Particle Systemの粒子を代表するPoint Lightを1個だけ更新する。
    /// 各粒子にLightを生成しないため、光源数と影のコストを抑えられる。
    /// VFX GraphのGPU粒子には対応しない。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class ParticleEmissionLightSource : MonoBehaviour
    {
        private const int MAX_PARTICLE_LIMIT = 256;

        [Header("References")]
        [SerializeField] private ParticleSystem _particleSystem;
        [SerializeField] private Light _sourceLight;

        [Header("Light")]
        [SerializeField, Min(0f)] private float _intensityPerParticle = 0.035f;
        [SerializeField, Min(0f)] private float _maxIntensity = 3f;
        [SerializeField, Min(0.1f)] private float _range = 4f;
        [SerializeField] private Vector3 _positionOffset = new(0f, 0.15f, 0f);
        [SerializeField] private Color _fallbackColor = new(1f, 0.35f, 0.08f);

        [Header("Performance")]
        [SerializeField, Range(1, MAX_PARTICLE_LIMIT)] private int _maxAnalyzedParticles = 128;
        [SerializeField, Range(1, 8)] private int _updateEveryFrames = 2;

        private ParticleSystem.Particle[] _particles;

        private void Reset()
        {
            _particleSystem = GetComponent<ParticleSystem>();
            _sourceLight = GetComponentInChildren<Light>(true);
        }

        private void OnEnable()
        {
            if (_particleSystem == null) _particleSystem = GetComponent<ParticleSystem>();
            _particles = new ParticleSystem.Particle[_maxAnalyzedParticles];

            if (_sourceLight != null)
            {
                _sourceLight.type = LightType.Point;
                _sourceLight.shadows = LightShadows.None;
                _sourceLight.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (_sourceLight == null || _particleSystem == null) return;
            if (Time.frameCount % _updateEveryFrames != 0) return;

            // OnValidate等で上限を変えた場合は必要な時だけ再確保する。
            if (_particles == null || _particles.Length != _maxAnalyzedParticles)
            {
                _particles = new ParticleSystem.Particle[_maxAnalyzedParticles];
            }

            int count = _particleSystem.GetParticles(_particles);
            if (count == 0)
            {
                _sourceLight.enabled = false;
                return;
            }

            ParticleSystem.MainModule main = _particleSystem.main;
            Vector3 weightedPosition = Vector3.zero;
            Vector3 weightedColor = Vector3.zero;
            float totalWeight = 0f;

            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle particle = _particles[i];
                Color32 particleColor = particle.GetCurrentColor(_particleSystem);
                float weight = particleColor.a / 255f;
                if (weight <= 0.001f) continue;

                Vector3 position = particle.position;
                switch (main.simulationSpace)
                {
                    case ParticleSystemSimulationSpace.Local:
                        position = _particleSystem.transform.TransformPoint(position);
                        break;
                    case ParticleSystemSimulationSpace.Custom:
                        if (main.customSimulationSpace != null)
                        {
                            position = main.customSimulationSpace.TransformPoint(position);
                        }
                        break;
                }

                weightedPosition += position * weight;
                weightedColor += new Vector3(particleColor.r, particleColor.g, particleColor.b) * (weight / 255f);
                totalWeight += weight;
            }

            if (totalWeight <= 0.001f)
            {
                _sourceLight.enabled = false;
                return;
            }

            float intensity = Mathf.Min(totalWeight * _intensityPerParticle, _maxIntensity);
            if (intensity <= 0.001f)
            {
                _sourceLight.enabled = false;
                return;
            }

            _sourceLight.transform.position = weightedPosition / totalWeight + _positionOffset;
            Vector3 color = weightedColor / totalWeight;
            _sourceLight.color = color.sqrMagnitude > 0.0001f
                ? new Color(color.x, color.y, color.z, 1f)
                : _fallbackColor;
            _sourceLight.range = _range;
            _sourceLight.intensity = intensity;
            _sourceLight.enabled = true;
        }

        private void OnDisable()
        {
            if (_sourceLight != null) _sourceLight.enabled = false;
        }

        private void OnValidate()
        {
            _maxAnalyzedParticles = Mathf.Clamp(_maxAnalyzedParticles, 1, MAX_PARTICLE_LIMIT);
            _updateEveryFrames = Mathf.Clamp(_updateEveryFrames, 1, 8);
            _range = Mathf.Max(0.1f, _range);
            _maxIntensity = Mathf.Max(0f, _maxIntensity);
            _intensityPerParticle = Mathf.Max(0f, _intensityPerParticle);
        }
    }
}
