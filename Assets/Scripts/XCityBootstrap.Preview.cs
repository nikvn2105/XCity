using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private string _previewOutput;

        private static string GetPreviewOutputArg()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-xcityPreview" && !string.IsNullOrEmpty(args[i + 1]))
                    return args[i + 1];
            }
            return null;
        }

        private IEnumerator CapturePreviewShots()
        {
            Debug.Log("XCITY_PREVIEW_START");
            Directory.CreateDirectory(_previewOutput);

            _cityRenderer.RebuildDirtyChunks();
            _buildingRenderer.RebuildDirtyChunks();

            var shots = new (string Name, Vector3 Target, float Distance, float Pitch, float Yaw)[]
            {
                ("overview", new Vector3(64f, 0f, 64f), 95f, 50f, 45f),
                ("residential", new Vector3(34f, 0f, 34f), 30f, 35f, 30f),
                ("commercial", new Vector3(96f, 0f, 34f), 32f, 30f, 210f),
                ("industry", new Vector3(34f, 0f, 96f), 30f, 40f, 135f),
                ("street", new Vector3(26f, 0f, 26f), 16f, 22f, 60f)
            };

            const int width = 1280;
            const int height = 720;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture prevCamTarget = _cam.targetTexture;

            try
            {
                _cam.targetTexture = rt;
                for (int i = 0; i < shots.Length; i++)
                {
                    var shot = shots[i];
                    _camTarget = shot.Target;
                    _camDistance = shot.Distance;
                    _camPitch = shot.Pitch;
                    _camYaw = shot.Yaw;
                    UpdateCameraTransform();

                    _cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();

                    string path = Path.Combine(_previewOutput, $"{i:00}-{shot.Name}.png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Debug.Log($"XCITY_PREVIEW_SHOT {path}");
                    yield return null;
                }
            }
            finally
            {
                _cam.targetTexture = prevCamTarget;
                RenderTexture.active = prevActive;
                Destroy(rt);
                Destroy(tex);
            }

            Debug.Log("XCITY_PREVIEW_DONE");
            Application.Quit();
        }
    }
}
