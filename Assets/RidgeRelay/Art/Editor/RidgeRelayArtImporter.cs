using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace RidgeRelay.Editor
{
    internal sealed class RidgeRelayArtImporter : AssetPostprocessor
    {
        private const string ArtPath = "Assets/RidgeRelay/Art/Resources/RidgeRelay/";
        private const string RunnerPath = ArtPath + "Jerboa.fbx";
        private const string AnimatorPath = ArtPath + "RunnerAnimator.controller";
        private static readonly string[] RunnerStates =
        {
            "Idle", "Run", "Jump", "Fall", "Land", "Stumble", "Finish", "Gadget"
        };

        public override uint GetVersion() => 3;
        public override int GetPostprocessOrder() => 1000;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ArtPath, StringComparison.Ordinal)) return;
            var model = (ModelImporter)assetImporter;
            bool runner = assetPath == RunnerPath;
            model.bakeAxisConversion = true;
            model.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            model.importCameras = false;
            model.importLights = false;
            model.importVisibility = false;
            model.importAnimation = runner;
            model.preserveHierarchy = runner || assetPath.EndsWith("/Windmill.fbx", StringComparison.Ordinal);
            if (!runner) return;

            model.animationType = ModelImporterAnimationType.Generic;
            model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }

        private void OnPreprocessAnimation()
        {
            if (assetPath != RunnerPath) return;
            var model = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = model.defaultClipAnimations;
            if (clips.Length == 0) return;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = StateName(clip.name);
                clip.loopTime = clip.name == "Idle" || clip.name == "Run";
            }
            model.clipAnimations = clips;
        }

        private void OnPreprocessTexture()
        {
            if (assetPath != ArtPath + "JerboaPalette.png") return;
            var texture = (TextureImporter)assetImporter;
            texture.mipmapEnabled = false;
            texture.filterMode = FilterMode.Point;
            texture.textureCompression = TextureImporterCompression.Uncompressed;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.sRGBTexture = true;
        }

        private void OnPreprocessMaterialDescription(
            MaterialDescription description, Material material, AnimationClip[] animations)
        {
            if (!assetPath.StartsWith(ArtPath, StringComparison.Ordinal)) return;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return;
            material.shader = lit;
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", 0.18f);
            if (description.TryGetProperty("DiffuseColor", out TexturePropertyDescription map) &&
                map.texture != null)
            {
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BaseMap", map.texture);
            }
            else if (description.TryGetProperty("DiffuseColor", out Vector4 diffuse))
            {
                material.SetColor("_BaseColor", new Color(diffuse.x, diffuse.y, diffuse.z, 1).gamma);
            }
        }

        private static string StateName(string name)
        {
            const string prefix = "Courier_";
            int split = name.LastIndexOf('|');
            if (split >= 0) return name.Substring(split + 1);
            return name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths, bool didDomainReload)
        {
            if (didDomainReload || importedAssets.Contains(RunnerPath))
                EnsureRunnerController();
        }

        internal static void EnsureRunnerController()
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(RunnerPath)
                .OfType<AnimationClip>()
                .Where(clip => RunnerStates.Contains(StateName(clip.name)))
                .ToArray();
            if (clips.Length == 0) return;
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorPath);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            bool changed = false;
            for (int i = 0; i < RunnerStates.Length; i++)
            {
                string name = RunnerStates[i];
                AnimationClip clip = clips.FirstOrDefault(candidate => StateName(candidate.name) == name);
                if (clip == null)
                {
                    Debug.LogWarning($"Ridge Relay runner model is missing the {name} animation.");
                    continue;
                }

                AnimatorState state = stateMachine.states.FirstOrDefault(child => child.state.name == name).state;
                if (state == null)
                {
                    state = stateMachine.AddState(name, new Vector3(260 + 185 * (i % 2), 55 + 60 * (i / 2)));
                    changed = true;
                }
                if (state.motion != clip)
                {
                    state.motion = clip;
                    EditorUtility.SetDirty(state);
                    changed = true;
                }
                if (name == "Idle" && stateMachine.defaultState != state)
                {
                    stateMachine.defaultState = state;
                    changed = true;
                }
            }
            if (!changed) return;
            EditorUtility.SetDirty(stateMachine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
        }
    }
}
