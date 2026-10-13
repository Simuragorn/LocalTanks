using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Turns on the two things a bright effect needs before it can look like anything: a tone mapper that
    /// can hold values above white, and bloom, so a muzzle flash and a fire glow bleed instead of clipping.
    /// </summary>
    /// <remarks>
    /// The project already had both components in <c>Assets/Settings/DefaultVolumeProfile.asset</c>, and both
    /// were inert: bloom's intensity was zero and tonemapping was set to <c>None</c>. HDR itself was already
    /// on in the URP asset. The missing piece on top of the profile is per camera: URP's
    /// <c>UniversalAdditionalCameraData</c> defaults to <c>m_RenderPostProcessing = false</c>, and while a
    /// scene has no such component the post-processing stack never runs, so nothing glows and no HDR value
    /// is ever resolved.
    /// <para>
    /// This is a project-wide look change, not a local effect tweak, which is why it lives behind its own
    /// command and is recorded in the decisions log rather than being folded into the asset builder.
    /// </para>
    /// </remarks>
    public static class CombatVfxGlowSetup
    {
        public const string ProfilePath = "Assets/Settings/DefaultVolumeProfile.asset";

        /// <summary>
        /// Bloom threshold, kept just above ordinary white on purpose. Everything the game draws normally
        /// stays below one, so a threshold under one makes the light-grey hulls and the track overlay glow
        /// as well; at this value only the effect layers, which are authored above white, bloom.
        /// </summary>
        public const float BloomThreshold = 1.05f;

        public const float BloomIntensity = 0.85f;
        public const float BloomScatter = 0.55f;

        /// <summary>Name of the root object that carries the scene's global light.</summary>
        public const string GlobalLightName = "VfxGlobalLight";

        [MenuItem("Local Tanks/Diagnostics/Enable Vfx Glow")]
        public static void Apply()
        {
            ApplyProfile();
            int cameras = ApplyToScenes();
            int lights = ApplyGlobalLightToScenes();
            Debug.Log($"[vfxglow] bloom {BloomIntensity} at threshold {BloomThreshold}, neutral tonemapping, " +
                      $"post processing enabled on {cameras} camera(s) and a global light added to {lights} " +
                      "battle scene(s).");
        }

        /// <summary>Sets the bloom and tonemapping values the bright effects are authored against.</summary>
        public static void ApplyProfile()
        {
            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(ProfilePath);
            bool bloomDone = false;
            bool tonemappingDone = false;
            foreach (UnityEngine.Object asset in subAssets)
            {
                SerializedObject serialized = new SerializedObject(asset);
                SerializedProperty name = serialized.FindProperty("m_Name");
                if (name == null)
                {
                    continue;
                }

                if (name.stringValue == "Bloom")
                {
                    SetFloat(serialized, "intensity", BloomIntensity);
                    SetFloat(serialized, "threshold", BloomThreshold);
                    SetFloat(serialized, "scatter", BloomScatter);
                    bloomDone = Save(serialized);
                }
                else if (name.stringValue == "Tonemapping")
                {
                    // 1 is Neutral: it keeps the colours of a warm flash instead of crushing them the way
                    // the default curve-free mode does once the values go above white.
                    SetInt(serialized, "mode", 1);
                    tonemappingDone = Save(serialized);
                }
            }

            if (!bloomDone || !tonemappingDone)
            {
                Debug.LogWarning($"[vfxglow] {ProfilePath} did not contain both Bloom and Tonemapping; " +
                                 $"bloom found: {bloomDone}, tonemapping found: {tonemappingDone}.");
            }
        }

        /// <summary>
        /// Enables post-processing on every camera of every battle scene. A camera without the URP data
        /// component never runs the post-processing stack, so without this the profile above would be a
        /// setting that nothing reads.
        /// </summary>
        public static int ApplyToScenes()
        {
            int changed = 0;
            foreach (string path in BattleScenePaths())
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool dirty = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                    {
                        if (EnsurePostProcessing(camera.gameObject))
                        {
                            dirty = true;
                        }
                    }
                }

                if (dirty)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    changed++;
                }
            }

            AssetDatabase.SaveAssets();
            return changed;
        }

        /// <summary>
        /// Adds a white global light to every battle scene. It multiplies the scene by one, so it changes
        /// nothing by itself; it exists so that the point lights of a flash and a fire have a lit scene to
        /// add to, instead of depending on the renderer's implicit fallback.
        /// </summary>
        public static int ApplyGlobalLightToScenes()
        {
            int changed = 0;
            foreach (string path in BattleScenePaths())
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (FindGlobalLight(scene) == null)
                {
                    CreateGlobalLight();
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    changed++;
                }
            }

            AssetDatabase.SaveAssets();
            return changed;
        }

        /// <summary>Creates the global light in the scene that is currently open.</summary>
        public static Light2D CreateGlobalLight()
        {
            GameObject root = new GameObject(GlobalLightName);
            Light2D light = root.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = Color.white;
            light.intensity = 1f;
            light.blendStyleIndex = 0;   // the multiply style: this is the scene's ambient light
            light.shadowsEnabled = false;
            return light;
        }

        /// <summary>Makes sure the current scene has a global light, and returns it.</summary>
        public static Light2D EnsureGlobalLight()
        {
            Light2D existing = FindGlobalLight(SceneManager.GetActiveScene());
            return existing != null ? existing : CreateGlobalLight();
        }

        private static Light2D FindGlobalLight(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Light2D light = root.GetComponentInChildren<Light2D>(true);
                if (light != null)
                {
                    return light;
                }
            }

            return null;
        }

        /// <summary>
        /// Makes sure this camera runs post-processing. Returns true when the flag had to be turned on.
        /// </summary>
        public static bool EnsurePostProcessing(GameObject cameraObject)
        {
            UniversalAdditionalCameraData data = cameraObject.GetComponent<UniversalAdditionalCameraData>();
            if (data == null)
            {
                data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            }

            if (data.renderPostProcessing)
            {
                return false;
            }

            data.renderPostProcessing = true;
            EditorUtility.SetDirty(data);
            return true;
        }

        private static IEnumerable<string> BattleScenePaths()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("Battle_") || name == "SampleScene")
                {
                    yield return path;
                }
            }
        }

        private static void SetFloat(SerializedObject serialized, string propertyName, float value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            SerializedProperty target = property?.FindPropertyRelative("m_Value") ?? property;
            if (target != null)
            {
                target.floatValue = value;
            }
        }

        private static void SetInt(SerializedObject serialized, string propertyName, int value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            SerializedProperty target = property?.FindPropertyRelative("m_Value") ?? property;
            if (target != null)
            {
                target.intValue = value;
            }
        }

        private static bool Save(SerializedObject serialized)
        {
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(serialized.targetObject);
            return true;
        }
    }
}
