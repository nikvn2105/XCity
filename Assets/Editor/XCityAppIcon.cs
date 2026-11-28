using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace XCity.EditorTools
{
    // Sets the default app icon from the logo before every build, so CI builds pick it up too.
    // Unity derives every iOS icon size from the default icon when no per-platform icons are set.
    internal sealed class XCityAppIcon : IPreprocessBuildWithReport
    {
        private const string IconPath = "Assets/Resources/XCityLogo.png";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogWarning($"XCity app icon missing at {IconPath}; keeping the current icon.");
                return;
            }
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
        }
    }
}
