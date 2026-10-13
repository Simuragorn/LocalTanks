using LocalTanks.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// A battle scene is either a playable map or a service scene, and it lives under <c>Maps</c> or
    /// <c>Labs</c> accordingly. The split is data for the project rather than a convention: a scene left in
    /// the flat folder, dropped from the build settings, or a project template still pointing at the deleted
    /// sample scene fails here instead of during a batch run.
    /// </summary>
    public sealed class BattleSceneLayoutTests
    {
        [Test]
        public void EveryScene_SitsInItsFolderAndIsEnabledInTheBuildSettings()
        {
            Assert.That(BattleSceneLayout.Validate(), Is.Empty);
        }

        [Test]
        public void SceneNames_AreUnchanged()
        {
            // Play Mode suites and the sweep runner load a scene by name, so the folder may move but the
            // name may not: the file name and the loaded name have to stay identical.
            foreach (string sceneName in BattleSceneLayout.KnownScenes)
            {
                Assert.That(
                    System.IO.Path.GetFileNameWithoutExtension(BattleSceneLayout.Path(sceneName)),
                    Is.EqualTo(sceneName));
            }
        }

        [Test]
        public void MapsAndLabs_AreSeparatedByTheirFolder()
        {
            foreach (string map in BattleSceneLayout.KnownMapScenes)
            {
                Assert.That(BattleSceneLayout.IsMap(map), Is.True);
                Assert.That(BattleSceneLayout.Path(map), Does.StartWith(BattleSceneLayout.MapsFolder + "/"));
            }

            foreach (string lab in BattleSceneLayout.LabScenes)
            {
                Assert.That(BattleSceneLayout.IsMap(lab), Is.False);
                Assert.That(BattleSceneLayout.Path(lab), Does.StartWith(BattleSceneLayout.LabsFolder + "/"));
            }
        }

        [Test]
        public void AFutureArena_IsAMapWithoutASecondListToUpdate()
        {
            Assert.That(
                BattleSceneLayout.MapScenePath("42_future"),
                Is.EqualTo($"{BattleSceneLayout.MapsFolder}/Battle_42_future.unity"));
        }

        [Test]
        public void BuildSettingsFile_ListsEveryScene()
        {
            // Reading the file is the point: assigning EditorBuildSettings.scenes only updates the editor
            // session, and in a batch run Unity writes the file from its own copy of the list. A migration
            // whose result never reached the disk would otherwise look successful.
            foreach (string sceneName in BattleSceneLayout.KnownScenes)
            {
                string path = BattleSceneLayout.Path(sceneName);
                Assert.That(
                    BattleSceneLayout.BuildSettingsFileLists(path),
                    Is.True,
                    $"'{path}' must be listed in {BattleSceneLayout.SettingsFilePath}.");
            }
        }

        [Test]
        public void ProjectTemplate_PointsAtTheTemplateSceneThatExists()
        {
            Assert.That(BattleSceneLayout.TemplateDefaultScene(), Is.EqualTo(BattleSceneLayout.TemplateScenePath));
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(BattleSceneLayout.TemplateScenePath), Is.Not.Null);
        }
    }
}
