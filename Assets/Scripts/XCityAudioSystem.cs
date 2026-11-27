using System;
using UnityEngine;

namespace XCity
{
    internal sealed class XCityAudioSystem : IDisposable
    {
        private const int SampleRate = 22050;

        private readonly GameObject _go;
        private readonly AudioSource _ambient;
        private readonly AudioSource _sfx;
        private readonly AudioClip _ambientClip;
        private readonly AudioClip _buildClip;
        private readonly AudioClip _bridgeClip;
        private readonly AudioClip _bulldozeClip;
        private readonly AudioClip _undoClip;
        private readonly AudioClip _alertClip;

        public bool Muted { get; private set; }

        public XCityAudioSystem(Transform parent, bool batchPreview)
        {
            _go = new GameObject("CityAudio");
            _go.transform.SetParent(parent, false);

            _ambient = _go.AddComponent<AudioSource>();
            _ambient.loop = true;
            _ambient.volume = 0.06f;
            _ambient.playOnAwake = false;

            _sfx = _go.AddComponent<AudioSource>();
            _sfx.loop = false;
            _sfx.volume = 0.35f;
            _sfx.playOnAwake = false;

            _ambientClip = CreateSurfLoop();
            _buildClip = CreateTone(540f, 760f, 0.11f, 0.32f);
            _bridgeClip = CreateTone(320f, 480f, 0.18f, 0.36f);
            _bulldozeClip = CreateNoiseBurst(0.14f, 0.38f);
            _undoClip = CreateTone(620f, 410f, 0.12f, 0.28f);
            _alertClip = CreateAlertChime();

            if (!batchPreview && !Application.isBatchMode)
            {
                _ambient.clip = _ambientClip;
                _ambient.Play();
            }
        }

        public void ToggleMute()
        {
            Muted = !Muted;
            _ambient.mute = Muted;
            _sfx.mute = Muted;
        }

        public void PlayBuild() => PlayClip(_buildClip);
        public void PlayBridge() => PlayClip(_bridgeClip);
        public void PlayBulldoze() => PlayClip(_bulldozeClip);
        public void PlayUndo() => PlayClip(_undoClip);
        public void PlayAlert() => PlayClip(_alertClip);

        private void PlayClip(AudioClip clip)
        {
            if (Muted || clip == null || Application.isBatchMode) return;
            _sfx.PlayOneShot(clip);
        }

        private static AudioClip CreateSurfLoop()
        {
            const int length = SampleRate * 4;
            var samples = new float[length];
            uint state = 13579u;
            float smooth = 0f;
            for (int i = 0; i < length; i++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                float white = ((state & 65535u) / 32768f) - 1f;
                smooth = smooth * 0.96f + white * 0.04f;
                float wave = 0.55f + 0.45f * Mathf.Sin(i * (Mathf.PI * 2f / length) * 2f);
                samples[i] = smooth * wave * 0.7f;
            }
            var clip = AudioClip.Create("XCitySurf", length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateTone(float fStart, float fEnd, float duration, float gain)
        {
            int length = Mathf.Max(256, Mathf.RoundToInt(SampleRate * duration));
            var samples = new float[length];
            float phase = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)length;
                float freq = Mathf.Lerp(fStart, fEnd, t);
                phase += freq * Mathf.PI * 2f / SampleRate;
                float env = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.4f);
                samples[i] = Mathf.Sin(phase) * env * gain;
            }
            var clip = AudioClip.Create("XCityTone", length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateNoiseBurst(float duration, float gain)
        {
            int length = Mathf.Max(256, Mathf.RoundToInt(SampleRate * duration));
            var samples = new float[length];
            uint state = 24680u;
            float lp = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)length;
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                float white = ((state & 65535u) / 32768f) - 1f;
                lp = lp * 0.82f + white * 0.18f;
                samples[i] = lp * (1f - t) * gain;
            }
            var clip = AudioClip.Create("XCityImpact", length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateAlertChime()
        {
            const float duration = 0.34f;
            int length = Mathf.RoundToInt(SampleRate * duration);
            var samples = new float[length];
            float phase = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)length;
                float freq = (t < 0.5f) ? 660f : 880f;
                phase += freq * Mathf.PI * 2f / SampleRate;
                float sub = Mathf.Repeat(t * 2f, 1f);
                float env = Mathf.Sin(sub * Mathf.PI) * (1f - sub * 0.35f);
                samples[i] = Mathf.Sin(phase) * env * 0.28f;
            }
            var clip = AudioClip.Create("XCityAlert", length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public void Dispose()
        {
            if (_ambientClip != null) UnityEngine.Object.Destroy(_ambientClip);
            if (_buildClip != null) UnityEngine.Object.Destroy(_buildClip);
            if (_bridgeClip != null) UnityEngine.Object.Destroy(_bridgeClip);
            if (_bulldozeClip != null) UnityEngine.Object.Destroy(_bulldozeClip);
            if (_undoClip != null) UnityEngine.Object.Destroy(_undoClip);
            if (_alertClip != null) UnityEngine.Object.Destroy(_alertClip);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }
}
