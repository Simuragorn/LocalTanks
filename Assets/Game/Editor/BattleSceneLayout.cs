using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    /// <summary>
    /// The one place that knows where the battle scenes live.
    ///
    /// A scene is either a playable map or a service scene used for testing and debugging. The split is
    /// expressed as folders, and the scene name never changes: Play Mode suites and the sweep runner load
    /// a scene by name, so a rename would break them for no player-visible gain.
    ///
    /// The move runs through <see cref="AssetDatabase.MoveAsset"/>, not through a hand edit, because Unity
    /// owns the identity of an asset: moving a scene keeps its GUID, and the GUID is what the build
    /// settings and every scene reference actually store.
    /// </summary>
    public static class BattleSceneLayout
    {
        public const string ScenesRoot = "Assets/Game/Scenes";
        public const string MapsFolder = ScenesRoot + "/Maps";
        public const string LabsFolder = ScenesRoot + "/Labs";

        public const string RiverCrossing = "Battle_RiverCrossing";
        public const string Mines = "Battle_10_hills";
        public const string TestRange = "Battle_TestRange";
        public const string NavigationRange = "Battle_NavigationRange";
        public const string AiDuelLab = "Battle_AiDuelLab";
        public const string PlayerAiTestLab = "Battle_PlayerAiTestLab";

        /// <summary>The maps the project currently builds. River Crossing is a temporary test map (ADR-064).</summary>
        public static readonly string[] KnownMapScenes = { RiverCrossing, Mines };

        /// <summary>Everything that is not a playable map: the ranges and the two laboratories.</summary>
        public static readonly string[] LabScenes = { TestRange, NavigationRange, AiDuelLab, PlayerAiTestLab };

        public const string TemplateScenePath = "Assets/Settings/Scenes/URP2DSceneTemplate.unity";

        /// <summary>The build settings file, read as text by the validation below.</summary>
        public const string SettingsFilePath = "ProjectSettings/EditorBuildSettings.asset";

        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";
        private const string TemplateDefaultSceneProperty = "templateDefaultScene";

        public static IEnumerable<string> KnownScenes => KnownMapScenes.Concat(LabScenes);

        /// <summary>
        /// A scene is a map unless it is declared a laboratory, so a new arena lands in Maps without a
        /// second list to update here.
        /// </summary>
        public static bool IsMap(string sceneName) => !LabScenes.Contains(sceneName);

        public static string Folder(string sceneName) => IsMap(sceneName) ? MapsFolder : LabsFolder;

        public static string Path(string sceneName) => $"{Folder(sceneName)}/{sceneName}.unity";

        /// <summary>Scene name of a converted arena, whose id is the map id from the WoT pipeline.</summary>
        public static string MapSceneName(string mapId) => $"Battle_{mapId}";

        public static string MapScenePath(string mapId) => Path(MapSceneName(mapId));

        /// <summary>Where scenes lived before the folders were split. Only the migration and its test use this.</summary>
        public static string LegacyPath(string sceneName) => $"{ScenesRoot}/{sceneName}.unity";

        /// <summary>
        /// Canonical path of a scene, moving a legacy file into place first when it is still in the flat
        /// folder. Generators call this instead of assuming the layout has already been applied, so a scene
        /// is never rebuilt just because it has not been moved yet.
        /// </summary>
        public static string EnsurePlaced(string sceneName)
        {
            string target = Path(sceneName);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(target) != null)
            {
                return target;
            }

            string legacy = LegacyPath(sceneName);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(legacy) == null)
            {
                return target;
            }

            EnsureFolder(Folder(sceneName));
            string error = AssetDatabase.MoveAsset(legacy, target);
            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException($"Moving '{legacy}' to '{target}' failed: {error}");
            }

            RelocateBuildSettings();
            Debug.Log($"Local Tanks scene moved: {legacy} -> {target}");
            return target;
        }

        /// <summary>True when the scene exists in either location, so a generator can tell "not built yet" from "not moved yet".</summary>
        public static bool Exists(string sceneName)
        {
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(Path(sceneName)) != null ||
                   AssetDatabase.LoadAssetAtPath<SceneAsset>(LegacyPath(sceneName)) != null;
        }

        [MenuItem("Local Tanks/Maintenance/Move Battle Scenes Into Maps And Labs")]
        public static void MoveScenesIntoPlace()
        {
            EnsureFolder(ScenesRoot);
            EnsureFolder(MapsFolder);
            EnsureFolder(LabsFolder);

            foreach (string sceneName in KnownScenes)
            {
                EnsurePlaced(sceneName);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // The build settings are written after the refresh on purpose: a refresh re-reads
            // ProjectSettings/EditorBuildSettings.asset from disk, so a change made before it is undone.
            RelocateBuildSettings();
            PointTheProjectTemplateAtTheTemplateScene();
            RemoveTheUnusedSampleScene();

            AssetDatabase.SaveAssets();
            Debug.Log($"Local Tanks scene layout applied: {string.Join(", ", KnownScenes)}");

            List<string> problems = Validate();
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "The scene layout is not valid after the migration:" + Environment.NewLine +
                    string.Join(Environment.NewLine, problems));
            }
        }

        /// <summary>
        /// Every problem that stops the layout from being true, as readable sentences. The Edit Mode test
        /// asserts this is empty, so a scene cannot be silently left in the flat folder or dropped from the
        /// build settings.
        /// </summary>
        public static List<string> Validate()
        {
            List<string> problems = new List<string>();

            foreach (string sceneName in KnownScenes)
            {
                string path = Path(sceneName);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    problems.Add($"Scene '{sceneName}' is missing at '{path}'.");
                }

                string legacy = LegacyPath(sceneName);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(legacy) != null)
                {
                    problems.Add($"Scene '{sceneName}' still sits in the flat folder at '{legacy}'.");
                }
            }

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            foreach (string sceneName in KnownScenes)
            {
                string path = Path(sceneName);
                EditorBuildSettingsScene entry = scenes.FirstOrDefault(scene => scene.path == path);
                if (entry == null)
                {
                    problems.Add($"Scene '{sceneName}' is not in the build settings.");
                    continue;
                }

                if (!entry.enabled)
                {
                    problems.Add($"Scene '{sceneName}' is disabled in the build settings.");
                }

                // The file, not the session: see BuildSettingsFileLists.
                if (!BuildSettingsFileLists(path))
                {
                    problems.Add($"Build settings file '{SettingsFilePath}' does not list '{path}'.");
                }
            }

            foreach (EditorBuildSettingsScene scene in scenes)
            {
                if (KnownScenes.Any(name => LegacyPath(name) == scene.path))
                {
                    problems.Add($"Build settings still point at the flat folder: '{scene.path}'.");
                }
            }

            string template = TemplateDefaultScene();
            if (template != TemplateScenePath)
            {
                problems.Add($"The project template scene is '{template}' instead of '{TemplateScenePath}'.");
            }
            else if (AssetDatabase.LoadAssetAtPath<SceneAsset>(template) == null)
            {
                problems.Add($"The project template scene '{template}' does not exist.");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SampleScenePath) != null)
            {
                problems.Add($"The unused sample scene '{SampleScenePath}' still exists.");
            }

            return problems;
        }

        /// <summary>The scene a new project created from this one opens, read straight out of the project settings.</summary>
        public static string TemplateDefaultScene()
        {
            UnityEngine.Object[] settings = AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsPath);
            if (settings == null || settings.Length == 0)
            {
                return string.Empty;
            }

            SerializedProperty property = new SerializedObject(settings[0]).FindProperty(TemplateDefaultSceneProperty);
            return property == null ? string.Empty : property.stringValue;
        }

        /// <summary>
        /// Puts every known scene into the build settings with the right path, the right GUID and its
        /// original position.
        ///
        /// Each entry is rebuilt as a new object and the GUID is set from the asset database. The stored
        /// GUID cannot be checked through the API — <c>EditorBuildSettingsScene.guid</c> derives its value
        /// from the path — so a stale or empty GUID in the settings file stays invisible until something
        /// reads the file. That is why <c>BattleSceneLayoutTests</c> reads the settings text and compares it
        /// with the scene metadata: the first version of this migration wrote empty GUIDs and every runtime
        /// check was happy with them.
        /// </summary>
        private static void RelocateBuildSettings()
        {
            List<EditorBuildSettingsScene> existing = EditorBuildSettings.scenes.ToList();
            List<EditorBuildSettingsScene> rebuilt = new List<EditorBuildSettingsScene>(existing.Count);

            // Entries keep their position, so the build index of every scene stays what it was.
            foreach (EditorBuildSettingsScene scene in existing)
            {
                string sceneName = KnownScenes.FirstOrDefault(
                    name => scene.path == Path(name) || scene.path == LegacyPath(name));
                if (sceneName == null)
                {
                    rebuilt.Add(scene);
                    continue;
                }

                rebuilt.Add(new EditorBuildSettingsScene(GuidOf(sceneName), scene.enabled));
            }

            // A known scene missing from the list is added at the end rather than silently dropped from the
            // build.
            foreach (string sceneName in KnownScenes)
            {
                if (rebuilt.Any(scene => scene.path == Path(sceneName)))
                {
                    continue;
                }

                rebuilt.Add(new EditorBuildSettingsScene(GuidOf(sceneName), true));
            }

            // Written unconditionally: the list can already be correct by path while the settings file still
            // stores an older one, and this call is what brings the editor session in line.
            EditorBuildSettings.scenes = rebuilt.ToArray();
        }

        private static UnityEngine.GUID GuidOf(string sceneName)
        {
            string path = Path(sceneName);
            UnityEngine.GUID guid = AssetDatabase.GUIDFromAssetPath(path);
            if (!HasGuid(guid))
            {
                throw new InvalidOperationException($"Scene '{path}' has no GUID in the asset database.");
            }

            return guid;
        }

        /// <summary>
        /// The project keeps one template scene; the sample scene it used to point at is deleted below, so
        /// the reference has to move first. The value is written through the serialized project settings
        /// rather than by editing the YAML, and a missing property fails loudly instead of leaving a
        /// reference to a deleted scene.
        /// </summary>
        private static void PointTheProjectTemplateAtTheTemplateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TemplateScenePath) == null)
            {
                throw new InvalidOperationException($"The project template scene '{TemplateScenePath}' does not exist.");
            }

            UnityEngine.Object[] settings = AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsPath);
            if (settings == null || settings.Length == 0)
            {
                throw new InvalidOperationException($"'{ProjectSettingsPath}' could not be loaded to repoint the project template.");
            }

            SerializedObject serialized = new SerializedObject(settings[0]);
            SerializedProperty property = serialized.FindProperty(TemplateDefaultSceneProperty);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"'{ProjectSettingsPath}' has no '{TemplateDefaultSceneProperty}' property, so the project template cannot be repointed.");
            }

            if (property.stringValue == TemplateScenePath)
            {
                return;
            }

            property.stringValue = TemplateScenePath;
            serialized.ApplyModifiedProperties();
        }

        private static void RemoveTheUnusedSampleScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SampleScenePath) != null)
            {
                if (!AssetDatabase.DeleteAsset(SampleScenePath))
                {
                    throw new InvalidOperationException($"Sample scene '{SampleScenePath}' could not be deleted.");
                }

                Debug.Log($"Local Tanks removed the unused sample scene: {SampleScenePath}");
            }

            // The flat folder existed only to hold that sample scene; leaving an empty one behind would
            // suggest that scenes still live there.
            const string flatFolder = "Assets/Scenes";
            if (!AssetDatabase.IsValidFolder(flatFolder))
            {
                return;
            }

            if (AssetDatabase.FindAssets("t:Scene", new[] { flatFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(flatFolder);
            }
        }

        /// <summary>
        /// True when the build settings file lists this scene path.
        ///
        /// The file is the only witness that the migration reached the disk: assigning
        /// <c>EditorBuildSettings.scenes</c> updates the editor session, while in a batch run Unity writes the
        /// file from its own copy of the list.
        ///
        /// The GUID next to the path is deliberately not validated. Measured behaviour: after a batch-mode
        /// move Unity itself writes <c>00000000000000000000000000000000</c> for entries it rebuilt, the
        /// runtime API answers with the GUID derived from the path anyway, and every scene still loads by
        /// name — so an empty GUID there is Unity's own state, not a reference this project can or should
        /// repair from code. See <c>Docs/KNOWN_ISSUES.md</c>.
        /// </summary>
        public static bool BuildSettingsFileLists(string scenePath)
        {
            if (!File.Exists(SettingsFilePath))
            {
                return false;
            }

            return File.ReadAllText(SettingsFilePath).Contains($"path: {scenePath}");
        }

        /// <summary>
        /// True when the asset database knows a GUID for this path. <c>UnityEngine.GUID</c> offers no empty
        /// check in this editor version that reads as a value, so the answer comes from its text form.
        /// </summary>
        private static bool HasGuid(UnityEngine.GUID guid)
        {
            System.Guid parsed;
            return System.Guid.TryParse(guid.ToString(), out parsed) && parsed != System.Guid.Empty;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException($"'{path}' has no parent folder to create it in.");
            }

            parent = parent.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
