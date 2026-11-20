using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RidgeRelay.Editor
{
    public static class ProjectSetup
    {
        private const string ScenePath = "Assets/RidgeRelay/Race.unity";
        private const string PipelinePath = "Assets/RidgeRelay/Mobile URP.asset";

        [MenuItem("Ridge Relay/Prepare Project")]
        public static void Prepare()
        {
            PlayerSettings.productName = "Ridge Relay";
            PlayerSettings.companyName = "Ridge Relay";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.ridgerelay.game");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            RidgeRelayArtImporter.EnsureRunnerController();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/RidgeRelay/Art/Brand/RidgeRelayIcon.png");
            if (icon != null)
            {
                int count = PlayerSettings.GetIconSizes(NamedBuildTarget.Unknown, IconKind.Any).Length;
                var icons = new Texture2D[count];
                for (int i = 0; i < count; i++) icons[i] = icon;
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, icons, IconKind.Any);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, "Assets/RidgeRelay/Mobile Renderer.asset");
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                EditorUtility.SetDirty(renderer);
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                pipeline.shadowDistance = 38;
                pipeline.msaaSampleCount = 2;
                pipeline.supportsHDR = false;
            }

            const string litMaterialPath = "Assets/RidgeRelay/Track Surface.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(litMaterialPath) == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new System.InvalidOperationException("URP Lit shader is unavailable.");
                AssetDatabase.CreateAsset(new Material(shader), litMaterialPath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            if (!File.Exists(ScenePath))
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Ridge Relay").AddComponent<RaceGame>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            IncludeTrackShader();
            AssetDatabase.SaveAssets();
        }

        private static void IncludeTrackShader()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settings.Length == 0) return;
            var serialized = new SerializedObject(settings[0]);
            var shaders = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (shaders == null) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            for (int i = 0; i < shaders.arraySize; i++)
                if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == lit) return;
            int index = shaders.arraySize;
            shaders.InsertArrayElementAtIndex(index);
            shaders.GetArrayElementAtIndex(index).objectReferenceValue = lit;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Ridge Relay/Build Windows")]
        public static void BuildWindows() => Build("Builds/Windows/Ridge Relay.exe", BuildTarget.StandaloneWindows64);

        [MenuItem("Ridge Relay/Build Android APK")]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            Build("Builds/Android/Ridge Relay.apk", BuildTarget.Android);
        }

        private static void Build(string path, BuildTarget target)
        {
            Prepare();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            BuildReport report = BuildPipeline.BuildPlayer(new[] { ScenePath }, path, target, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.InvalidOperationException("Ridge Relay build failed: " + report.summary.result);
        }
    }
}




