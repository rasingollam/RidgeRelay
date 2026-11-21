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
            "Idle", "Run", "Jump", "Fall", "Land", "Stumble", "Finish", "Gadget",
            "Sprint", "Uphill", "Downhill", "CastMagnet", "CastRocket", "CastShock"
        };

        public override uint GetVersion() => 4;
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
            if (!runner)
                model.isReadable = assetPath.EndsWith("/RiverShrub.fbx", StringComparison.Ordinal) ||
                    assetPath.EndsWith("/LavenderPatch.fbx", StringComparison.Ordinal);
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
                clip.loopTime = clip.name == "Idle" || clip.name == "Run" || clip.name == "Finish" ||
                    clip.name == "Sprint" || clip.name == "Uphill" || clip.name == "Downhill";
            }
            model.clipAnimations = clips;
        }

        private void OnPreprocessTexture()
        {
            if(assetPath==ArtPath+"JerboaFurNormal.png")
            {
                var fur=(TextureImporter)assetImporter;
                fur.textureType=TextureImporterType.NormalMap;
                fur.sRGBTexture=false;
                fur.mipmapEnabled=true;
                fur.wrapMode=TextureWrapMode.Clamp;
                fur.filterMode=FilterMode.Trilinear;
                fur.textureCompression=TextureImporterCompression.CompressedHQ;
                fur.maxTextureSize=512;
                return;
            }
            if (assetPath == ArtPath + "GadgetIcons.png")
            {
                var icons = (TextureImporter)assetImporter;
                icons.maxTextureSize = 1024;
                icons.mipmapEnabled = false;
                icons.alphaIsTransparency = true;
                icons.filterMode = FilterMode.Bilinear;
                icons.wrapMode = TextureWrapMode.Clamp;
                icons.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            if (assetPath.StartsWith(ArtPath + "Surfaces/", StringComparison.Ordinal))
            {
                var surface = (TextureImporter)assetImporter;
                bool sky=assetPath.EndsWith("/DaySky.png",StringComparison.Ordinal);
                surface.maxTextureSize = sky?2048:1024;
                surface.mipmapEnabled = !sky;
                surface.filterMode = FilterMode.Trilinear;
                surface.anisoLevel = 4;
                surface.wrapMode = TextureWrapMode.Repeat;
                if(sky)surface.wrapModeV=TextureWrapMode.Clamp;
                surface.textureCompression = TextureImporterCompression.CompressedHQ;
                bool normal = assetPath.EndsWith("Normal.png", StringComparison.Ordinal);
                bool mask = assetPath.EndsWith("Mask.png", StringComparison.Ordinal);
                surface.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                surface.sRGBTexture = !normal && !mask;
                surface.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Android", overridden = true, maxTextureSize = sky?2048:1024,
                    format = TextureImporterFormat.ASTC_6x6, compressionQuality = 80
                });
                return;
            }
            if (assetPath != ArtPath + "JerboaPalette.png") return;
            var texture = (TextureImporter)assetImporter;
            texture.mipmapEnabled = true;
            texture.filterMode = FilterMode.Bilinear;
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
            if(assetPath.EndsWith("/CanyonTree.fbx",StringComparison.Ordinal))
            {
                material.SetFloat("_Cull",0);
                material.enableInstancing=true;
            }
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
            string[] specialized = { "Sprint", "Uphill", "Downhill", "CastMagnet", "CastRocket", "CastShock" };
            foreach (ChildAnimatorState child in stateMachine.states)
                if (specialized.Contains(child.state.name))
                {
                    stateMachine.RemoveState(child.state);
                    changed = true;
                }
            for (int i = 0; i < RunnerStates.Length; i++)
            {
                string name = RunnerStates[i];
                if (specialized.Contains(name)) continue;
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
            AnimationClip run = clips.FirstOrDefault(clip => StateName(clip.name) == "Run");
            AnimationClip sprint = clips.FirstOrDefault(clip => StateName(clip.name) == "Sprint");
            AnimationClip uphill = clips.FirstOrDefault(clip => StateName(clip.name) == "Uphill");
            AnimationClip downhill = clips.FirstOrDefault(clip => StateName(clip.name) == "Downhill");
            if (run != null && sprint != null && uphill != null && downhill != null)
            {
                foreach (string parameter in new[] { "Speed", "Grade" })
                    if (!controller.parameters.Any(item => item.name == parameter))
                        controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
                AnimatorState running = stateMachine.states.First(child => child.state.name == "Run").state;
                BlendTree tree = AssetDatabase.LoadAllAssetsAtPath(AnimatorPath).OfType<BlendTree>()
                    .FirstOrDefault(item => item.name == "Terrain Running");
                if (tree == null)
                {
                    tree = new BlendTree { name = "Terrain Running" };
                    AssetDatabase.AddObjectToAsset(tree, controller);
                }
                tree.blendType = BlendTreeType.FreeformCartesian2D;
                tree.blendParameter = "Speed";
                tree.blendParameterY = "Grade";
                tree.children = new[]
                {
                    new ChildMotion { motion = run, position = new Vector2(1,0), timeScale = 1 },
                    new ChildMotion { motion = sprint, position = new Vector2(1.35f,0), timeScale = 1 },
                    new ChildMotion { motion = uphill, position = new Vector2(1,0.35f), timeScale = 1 },
                    new ChildMotion { motion = downhill, position = new Vector2(1,-0.35f), timeScale = 1 }
                };
                running.motion = tree;
                EditorUtility.SetDirty(tree);
                EditorUtility.SetDirty(running);
                changed = true;
            }
            EnsurePowerLayer(controller, clips);
            if (!changed) return;
            EditorUtility.SetDirty(stateMachine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
        }

        private static void EnsurePowerLayer(AnimatorController controller, AnimationClip[] clips)
        {
            const string layerName = "Upper Body Power";
            AnimatorControllerLayer layer = controller.layers.FirstOrDefault(item => item.name == layerName);
            if (layer == null)
            {
                var machine = new AnimatorStateMachine { name = layerName };
                AssetDatabase.AddObjectToAsset(machine, controller);
                layer = new AnimatorControllerLayer
                {
                    name = layerName, defaultWeight = 0, stateMachine = machine,
                    blendingMode = AnimatorLayerBlendingMode.Override
                };
                controller.AddLayer(layer);
            }
            if (!controller.parameters.Any(item => item.name == "PowerTempo"))
                controller.AddParameter("PowerTempo", AnimatorControllerParameterType.Float);
            AvatarMask mask = AssetDatabase.LoadAllAssetsAtPath(AnimatorPath).OfType<AvatarMask>().FirstOrDefault();
            if (mask == null)
            {
                mask = new AvatarMask { name = "Power upper body" };
                AssetDatabase.AddObjectToAsset(mask, controller);
            }
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerPath);
            Transform[] transforms = model.GetComponentsInChildren<Transform>();
            mask.transformCount = transforms.Length;
            for (int i = 0; i < transforms.Length; i++)
            {
                string path = AnimationUtility.CalculateTransformPath(transforms[i], model.transform);
                mask.SetTransformPath(i, path);
                mask.SetTransformActive(i, path.Split('/').Contains("spine"));
            }
            AnimatorControllerLayer[] layers = controller.layers;
            int index = System.Array.FindIndex(layers, item => item.name == layerName);
            layers[index].avatarMask = mask;
            controller.layers = layers;
            foreach (string name in new[] { "Gadget", "CastMagnet", "CastRocket", "CastShock" })
            {
                AnimationClip clip = clips.FirstOrDefault(item => StateName(item.name) == name);
                if (clip == null) continue;
                string stateName = name == "Gadget" ? "PowerAction" : name;
                AnimatorState state = layer.stateMachine.states.FirstOrDefault(item => item.state.name == stateName).state;
                if (state == null) state = layer.stateMachine.AddState(stateName);
                state.motion = clip;
                state.speedParameter = "PowerTempo";
                state.speedParameterActive = true;
                EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(mask);
            EditorUtility.SetDirty(layer.stateMachine);
            EditorUtility.SetDirty(controller);
        }
    }
}
