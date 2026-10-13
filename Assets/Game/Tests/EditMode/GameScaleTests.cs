using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LocalTanks.Editor;
using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// One coefficient converts client metres into game units (ADR-064). These checks exist because the
    /// project already carried four different versions of that number — a literal in the map asset, a formula
    /// in the editor, a divisor in the data pipeline and a multiplier applied by hand to the data — and none
    /// of them compared itself with the others.
    /// </summary>
    public sealed class GameScaleTests
    {
        [Test]
        public void MetresAndUnits_RoundTrip()
        {
            Assert.That(GameScale.MetresToUnits(GameScale.MetresPerUnit), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(GameScale.UnitsToMetres(GameScale.MetresToUnits(240f)), Is.EqualTo(240f).Within(0.001f));
        }

        [Test]
        public void TheCoefficient_LivesInOnePlace()
        {
            // A second copy of this number is what let the map importer, the tank builder and the data pipeline
            // disagree before, so the literal is allowed in one file only. The needle is built from the constant
            // itself: writing the literal here would make this file its own counter-example.
            string needle = GameScale.MetresPerUnit.ToString("0.###", CultureInfo.InvariantCulture);
            List<string> owners = new List<string>();
            foreach (string file in Directory.GetFiles("Assets/Game", "*.cs", SearchOption.AllDirectories))
            {
                if (File.ReadAllText(file).Contains(needle))
                {
                    owners.Add(file.Replace('\\', '/'));
                }
            }

            Assert.That(
                owners,
                Is.EqualTo(new[] { "Assets/Game/Core/GameScale.cs" }),
                "The scale must be written in exactly one file; everything else calls GameScale.");
        }

        [Test]
        public void TheMapAsset_IsBuiltInTheGameScale()
        {
            WoTMapAsset fresh = ScriptableObject.CreateInstance<WoTMapAsset>();
            try
            {
                Assert.That(fresh.metresPerUnit, Is.EqualTo(GameScale.MetresPerUnit));
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }

            // The generated arena is the one that matters: its imported sizes divide by this value, so a
            // different number there would resize every object on the map instead of relabelling it.
            WoTMapAsset converted = UnityEditor.AssetDatabase.LoadAssetAtPath<WoTMapAsset>(WoTMapImporter.AssetPath("10_hills"));
            Assert.That(converted, Is.Not.Null, "The converted arena asset is missing.");
            Assert.That(converted.metresPerUnit, Is.EqualTo(GameScale.MetresPerUnit));
            Assert.That(converted.playableSize.x * converted.metresPerUnit, Is.EqualTo(800f).Within(1f));
        }

        [Test]
        public void TheDataPipeline_DoesNotCarryASecondScaleOrAViewRangeDivisor()
        {
            string conversion = File.ReadAllText("Tools/WoTDataPipeline/vehicle_profiles.json");
            Assert.That(conversion, Does.Not.Contain("viewRangeMetresPerUnityUnit"));
            Assert.That(conversion, Does.Not.Contain("guaranteedDetectionRange"));
        }
    }
}
