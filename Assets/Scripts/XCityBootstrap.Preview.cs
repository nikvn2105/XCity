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
            Directory.CreateDirectory(_previewOutput);
            yield return null;
            yield return null;

            var shots = new (string Name, Vector3 Target, float Distance, float Pitch, float Yaw)[]
            {
                ("overview", new Vector3(64f, 0f, 64f), 95f, 50f, 45f),
                ("residential", new Vector3(34f, 0f, 34f), 30f, 35f, 30f),
                ("commercial", new Vector3(96f, 0f, 34f), 32f, 30f, 210f),
                ("industry", new Vector3(34f, 0f, 96f), 30f, 40f, 135f),
                ("street", new Vector3(26f, 0f, 26f), 16f, 22f, 60f)
            };

            for (int i = 0; i < shots.Length; i++)
            {
                var shot = shots[i];
                _camTarget = shot.Target;
                _camDistance = shot.Distance;
                _camPitch = shot.Pitch;
                _camYaw = shot.Yaw;
                UpdateCameraTransform();

                yield return null;
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();

                string path = Path.Combine(_previewOutput, $"{i:00}-{shot.Name}.png");
                ScreenCapture.CaptureScreenshot(path);

                yield return null;
                yield return null;
            }

            yield return new WaitForSecondsRealtime(1f);
            Debug.Log("XCITY_PREVIEW_DONE");
            Application.Quit();
        }
    }
}
