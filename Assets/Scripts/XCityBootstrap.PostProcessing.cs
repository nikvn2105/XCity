using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#endif

namespace XCity
{
    public partial class XCityBootstrap
    {
        internal const string PostProcessResourcesName = "XCityPostProcessResources";
        internal const string PostProcessPackageAsset =
            "Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset";

        private PostProcessLayer _postProcessLayer;
        private PostProcessVolume _postProcessVolume;
        private PostProcessProfile _postProcessProfile;
        private Bloom _bloom;
        private Vignette _vignette;
        private AmbientOcclusion _ambientOcclusion;
        private bool _originalHdr;
        private bool _originalMsaa;
        private DepthTextureMode _originalDepthTexture;
        private Scene _postProcessScene;

        partial void ApplyPostProcessing(int level)
        {
            // Screenshot capture renders to its own LDR target and keeps its baseline appearance.
            if (_previewOutput != null) return;

            if (level == 0)
            {
                if (_postProcessLayer == null) return;
                _postProcessLayer.enabled = false;
                _postProcessVolume.enabled = false;
                _cam.allowHDR = _originalHdr;
                _cam.allowMSAA = _originalMsaa;
                _cam.depthTextureMode = _originalDepthTexture;
                return;
            }

            // Startup reads display settings before it creates the runtime camera.
            if (_cam == null)
            {
                StartCoroutine(ApplyPostProcessingAfterCameraSetup());
                return;
            }

            if (_postProcessLayer == null && !CreatePostProcessing()) return;

            _bloom.enabled.value = level >= 2;
            _vignette.enabled.value = level >= 2;
            _ambientOcclusion.enabled.value = level >= 3;
            bool useOcclusion = _ambientOcclusion.IsEnabledAndSupported(null);
            _cam.allowHDR = true;
            // Keep 4x MSAA at High. Built-in MSVO consumes single-sample depth at Ultra.
            _cam.allowMSAA = _originalMsaa && !useOcclusion;
            if (!useOcclusion) _cam.depthTextureMode = _originalDepthTexture;

            _postProcessVolume.enabled = true;
            _postProcessLayer.enabled = true;
        }

        private IEnumerator ApplyPostProcessingAfterCameraSetup()
        {
            yield return null;
            ApplyPostProcessing(_graphicsLevel);
        }

        private bool CreatePostProcessing()
        {
#if UNITY_EDITOR
            var resources = AssetDatabase.LoadAssetAtPath<PostProcessResources>(PostProcessPackageAsset);
#else
            var resources = Resources.Load<PostProcessResources>(PostProcessResourcesName);
#endif
            if (resources == null)
            {
                Debug.LogError("XCity could not load PostProcessResources.", this);
                return false;
            }

            _originalHdr = _cam.allowHDR;
            _originalMsaa = _cam.allowMSAA;
            _originalDepthTexture = _cam.depthTextureMode;
            _postProcessScene = _cam.gameObject.scene;

            _postProcessProfile = ScriptableObject.CreateInstance<PostProcessProfile>();
            var grading = _postProcessProfile.AddSettings<ColorGrading>();
            grading.gradingMode.Override(GradingMode.HighDefinitionRange);
            grading.tonemapper.Override(Tonemapper.ACES);
            grading.saturation.Override(4f);
            grading.contrast.Override(6f);

            _bloom = _postProcessProfile.AddSettings<Bloom>();
            _bloom.threshold.Override(0.85f);
            _bloom.intensity.Override(2.5f);
            _bloom.fastMode.Override(true);

            _vignette = _postProcessProfile.AddSettings<Vignette>();
            _vignette.intensity.Override(0.12f);
            _vignette.smoothness.Override(0.45f);

            _ambientOcclusion = _postProcessProfile.AddSettings<AmbientOcclusion>();
            _ambientOcclusion.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance);
            _ambientOcclusion.intensity.Override(0.45f);
            _ambientOcclusion.ambientOnly.Override(false);

            _postProcessVolume = _cam.gameObject.AddComponent<PostProcessVolume>();
            _postProcessVolume.isGlobal = true;
            _postProcessVolume.sharedProfile = _postProcessProfile;

            _postProcessLayer = _cam.gameObject.AddComponent<PostProcessLayer>();
            _postProcessLayer.Init(resources);
            _postProcessLayer.volumeLayer = 1 << _postProcessVolume.gameObject.layer;
            _postProcessLayer.volumeTrigger = _cam.transform;
            _postProcessLayer.antialiasingMode = PostProcessLayer.Antialiasing.None;

            SceneManager.sceneUnloaded += ReleasePostProcessing;
            return true;
        }

        private void ReleasePostProcessing(Scene scene)
        {
            if (scene != _postProcessScene) return;
            SceneManager.sceneUnloaded -= ReleasePostProcessing;
            RuntimeUtilities.DestroyProfile(_postProcessProfile, true);
        }
    }

#if UNITY_EDITOR
    internal sealed class XCityPostProcessBuildResources : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string ResourceAsset = "Assets/Resources/" + XCityBootstrap.PostProcessResourcesName + ".asset";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var source = AssetDatabase.LoadAssetAtPath<PostProcessResources>(XCityBootstrap.PostProcessPackageAsset);
            if (source == null || source.shaders == null || source.computeShaders == null ||
                source.shaders.uber == null || source.shaders.bloom == null ||
                source.shaders.lut2DBaker == null || source.shaders.multiScaleAO == null ||
                source.computeShaders.multiScaleAODownsample1 == null ||
                source.computeShaders.multiScaleAODownsample2 == null ||
                source.computeShaders.multiScaleAORender == null ||
                source.computeShaders.multiScaleAOUpsample == null)
                throw new BuildFailedException("XCity post-processing package resources are missing or incomplete.");

            // Serialize the package references so Unity collects the shaders and compute shaders for the Player.
            // Replace any generated copy left behind by an interrupted build.
            if (AssetDatabase.LoadMainAssetAtPath(ResourceAsset) != null)
                AssetDatabase.DeleteAsset(ResourceAsset);
            var copy = Object.Instantiate(source);
            copy.name = XCityBootstrap.PostProcessResourcesName;
            AssetDatabase.CreateAsset(copy, ResourceAsset);
            AssetDatabase.SaveAssets();
        }

        public void OnPostprocessBuild(BuildReport report) => AssetDatabase.DeleteAsset(ResourceAsset);
    }
#endif
}
