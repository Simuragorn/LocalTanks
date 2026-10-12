using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The art library is built from other arenas, so most objects cannot match their own resource and
    /// fall back through keyword rules to a category representative. These checks pin the ladder and
    /// the honest-miss behaviour, because a matcher that silently invents a sprite would put the wrong
    /// art on thousands of objects without anyone noticing.
    /// </summary>
    public sealed class MapSpriteMatcherTests
    {
        private static readonly string[] Library =
        {
            "village_cowshed", "village_wood_shed", "village_windmill", "rock_sandstone",
            "wood_fence", "haystack", "log_pile", "dry_bush", "fallen_tree_root", "burnt_tree"
        };

        [Test]
        public void FenceFamily_MatchesAnyFenceVariant()
        {
            Assert.That(MapSpriteMatcher.Resolve(
                "content/GatesAndFences/gaf_19_02_StoneFence_tile.primitives", "obstacle", Library),
                Is.EqualTo("wood_fence"));
            Assert.That(MapSpriteMatcher.Resolve(
                "content/GatesAndFences/gaf_19_05_GrapevineFence.primitives", "obstacle", Library),
                Is.EqualTo("wood_fence"));
        }

        [Test]
        public void RockFamily_MatchesEveryCliffVariant()
        {
            foreach (string resource in new[]
                     {
                         "content/Environment/env216_ItalyCliffs/normal/lod0/env216_ItalyCliffsSmallMountain_06.primitives",
                         "content/Environment/hd_env_EU_001_Cliff_rock_01.primitives",
                         "content/Decor/dec_011_Murovanka_Rock.primitives"
                     })
            {
                Assert.That(MapSpriteMatcher.Resolve(resource, "rock", Library), Is.EqualTo("rock_sandstone"),
                    resource);
            }
        }

        [Test]
        public void Buildings_KeepTheirOwnKindWhenTheLibraryHasIt()
        {
            Assert.That(MapSpriteMatcher.Resolve(
                "content/Buildings/hd_bld_SU_002_Cowshed_01.primitives", "house", Library),
                Is.EqualTo("village_cowshed"));
            Assert.That(MapSpriteMatcher.Resolve(
                "content/Buildings/hd_bld_EU_223_PMill_01.primitives", "house", Library),
                Is.EqualTo("village_windmill"));
        }

        [Test]
        public void UnknownResource_FallsBackToItsCategory()
        {
            // A wheelbarrow is cluttered ground furniture; the library holds no wheelbarrow, and the
            // honest answer is the obstacle representative rather than nothing.
            Assert.That(MapSpriteMatcher.Resolve(
                "content/Environment/env_19_26_Wheelbarrow/normal/lod0/env_19_26_Wheelbarrow.primitives",
                "obstacle", Library),
                Is.EqualTo("wood_fence"));
        }

        [Test]
        public void MissInTheLibrary_ReturnsNothingRatherThanGuessing()
        {
            Assert.That(MapSpriteMatcher.Resolve(
                "content/Buildings/hd_bld_SU_002_Cowshed_01.primitives", "house", new[] { "dry_bush" }),
                Is.Null);
            Assert.That(MapSpriteMatcher.Resolve("anything", "house", System.Array.Empty<string>()), Is.Null);
        }

        [Test]
        public void Rules_OnlyNameSpritesTheLibraryActuallyHas()
        {
            // A rule pointing at a sprite the library does not contain would silently never fire.
            FieldInfo field = typeof(MapSpriteMatcher).GetField(
                "Rules", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "The keyword rules were renamed; this check must follow them.");

            var rules = ((System.Collections.IEnumerable)field.GetValue(null))
                .Cast<object>()
                .Select(rule => (string)rule.GetType().GetField("Item2").GetValue(rule))
                .ToArray();
            Assert.That(rules, Is.Not.Empty);
            foreach (string spriteId in rules)
            {
                Assert.That(Library, Does.Contain(spriteId),
                    $"Rule names sprite '{spriteId}' which the environment library does not contain.");
            }
        }
    }
}
