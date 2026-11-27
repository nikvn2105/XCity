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

            Shader.SetGlobalFloat("_XCityTime", 12.5f);
            _construction?.SeedPreviewSites();
            var previewCenter = Vector3.one * ((CitySimulation.LegacyOffset + 16) * CellSize);
            _camTarget = new Vector3(previewCenter.x, 0f, previewCenter.z);
            UpdateCameraTransform();
            RefreshSceneDetail(int.MaxValue);
            _industrialSmoke?.Prewarm(15f);
            _construction?.Update(0.05f, true, LocalView);
            _vehicles?.Prewarm(20f);
            _helicopters?.Prewarm(14f);
            _watercraft?.Prewarm(8f);
            _citizens?.Prewarm(18f);
            RefreshWorldMap();
            File.WriteAllBytes(Path.Combine(_previewOutput, "world-map.png"), _worldMapTexture.EncodeToPNG());

            var shots = new (string Name, Vector3 Target, float Distance, float Pitch, float Yaw)[]
            {
                ("catalog", new Vector3(38f, 0f, 34f), 29f, 48f, 180f),
                ("overview", new Vector3(54f, 0f, 54f), 136f, 54f, 45f),
                ("residential", new Vector3(34f, 0f, 34f), 30f, 35f, 30f),
                ("high-rises", new Vector3(74f, 2.3f, 41f), 20.0f, 19f, 180f),
                ("commercial", new Vector3(85f, 0.6f, 48f), 34f, 48f, 210f),
                ("public-services", new Vector3(109f, 0.6f, 49f), 40f, 52f, 155f),
                ("industry", new Vector3(34f, 0f, 94f), 25f, 36f, 135f),
                ("street", new Vector3(50f, 0.8f, 38f), 13.5f, 20f, 0f),
                ("waterfront", new Vector3(842f, 0.2f, 511f), 54f, 49f, 210f),
                ("bridge-clearance", new Vector3(850f, 0.45f, 518f), 29f, 14f, 90f),
                ("coast", new Vector3(-708f, 0f, -566f), 139f, 57f, 225f)
            };

            const int width = 1280;
            const int height = 720;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture prevCamTarget = _cam.targetTexture;
            WorldView lastPrewarmed = LocalView;

            try
            {
                _cam.targetTexture = rt;
                for (int i = 0; i < shots.Length; i++)
                {
                    var shot = shots[i];
                    float offset = CitySimulation.LegacyOffset * CellSize;
                    _camTarget = shot.Target + new Vector3(offset, 0f, offset);
                    _camDistance = shot.Distance;
                    _camPitch = shot.Pitch;
                    _camYaw = shot.Yaw;
                    UpdateCameraTransform();
                    RefreshSceneDetail(int.MaxValue);
                    if (_chunkScheduler.ShowEffects && !LocalView.SameAnchor(lastPrewarmed))
                    {
                        lastPrewarmed = LocalView;
                        _vehicles?.Prewarm(8f);
                        _watercraft?.Prewarm(5f);
                        _citizens?.Prewarm(6f);
                    }
                    var counts = _chunkScheduler.GetChunkCounts();
                    Debug.Log($"XCITY_PREVIEW_LOD {shot.Name} full={counts.Full} overview={counts.Overview} hidden={counts.Hidden} effects={_chunkScheduler.ShowEffects}");

                    _cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();

                    string path = Path.Combine(_previewOutput, $"{i:00}-{shot.Name}.png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Debug.Log($"XCITY_PREVIEW_SHOT {path}");
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
            yield break;
        }
    }
}
