using UnityEngine;

namespace SignVR.Demo
{
    [DisallowMultipleComponent]
    public sealed class FireplaceLightFlicker : MonoBehaviour
    {
        [SerializeField] private Light[] targetLights = new Light[0];
        [SerializeField] private ParticleSystem[] fireSystems = new ParticleSystem[0];
        [SerializeField, Range(0f, 0.8f)] private float amplitude = 0.22f;
        [SerializeField, Min(0.1f)] private float speed = 7.5f;
        [SerializeField] private float noiseOffset = 13.7f;

        private float[] baseIntensities;
        private float[] lightNoiseOffsets;

        public void Configure(
            Light[] lights,
            ParticleSystem[] particles,
            float intensityAmplitude,
            float flickerSpeed)
        {
            targetLights = lights;
            fireSystems = particles;
            amplitude = intensityAmplitude;
            speed = flickerSpeed;
            CaptureBaseIntensities();
        }

        private void Awake()
        {
            CaptureBaseIntensities();
        }

        private void OnEnable()
        {
            CaptureBaseIntensities();
            if (Application.isPlaying)
            {
                for (int index = 0; index < fireSystems.Length; index++)
                {
                    fireSystems[index].Play(true);
                }
            }
        }

        private void Update()
        {
            for (int index = 0; index < targetLights.Length; index++)
            {
                float slow = Mathf.PerlinNoise(
                    lightNoiseOffsets[index],
                    Time.time * speed * 0.24f
                );
                float fast = Mathf.PerlinNoise(
                    lightNoiseOffsets[index] + 31.7f,
                    Time.time * speed
                );
                float sample = slow * 0.68f + fast * 0.32f;
                float perLightAmplitude = amplitude * (0.82f + index * 0.12f);
                float multiplier = 1f + (sample * 2f - 1f) * perLightAmplitude;
                targetLights[index].intensity = baseIntensities[index] * multiplier;
            }
        }

        private void CaptureBaseIntensities()
        {
            baseIntensities = new float[targetLights.Length];
            lightNoiseOffsets = new float[targetLights.Length];
            for (int index = 0; index < targetLights.Length; index++)
            {
                baseIntensities[index] = targetLights[index].intensity;
                lightNoiseOffsets[index] = noiseOffset + index * 17.31f;
            }
        }
    }
}
