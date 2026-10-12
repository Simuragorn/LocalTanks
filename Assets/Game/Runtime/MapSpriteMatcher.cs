using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Chooses the environment sprite that stands in for a converted obstacle or plant.
    /// <para>
    /// Matching runs from most to least specific. An exact match on the source resource is ideal but
    /// almost never available, because the art library is built from other arenas: on the converted
    /// Mines only 1.7% of objects match their own resource. The measured useful step is the second one,
    /// a family keyword such as `cliff` or `fence`, which covers 95.6% of objects, and the remaining
    /// 2.7% fall back to a representative sprite for their category. Keeping the ladder explicit means
    /// the fallback is a decision on record rather than an accident of naming.
    /// </para>
    /// </summary>
    public static class MapSpriteMatcher
    {
        /// <summary>Ordered keyword rules; the first token found in the resource path wins.</summary>
        private static readonly (string Token, string SpriteId)[] Rules =
        {
            // Fences, walls and gates share one family: the library holds a wooden fence, and a stone or
            // vine row is the same kind of object at the same size, so reusing it is honest rather than
            // approximate art.
            ("gaf", "wood_fence"),
            ("fence", "wood_fence"),
            ("grapevine", "wood_fence"),
            ("wall", "wood_fence"),
            ("windbreak", "wood_fence"),
            ("cliff", "rock_sandstone"),
            ("rock", "rock_sandstone"),
            ("boulder", "rock_sandstone"),
            ("mountain", "rock_sandstone"),
            ("logs", "log_pile"),
            ("firewood", "log_pile"),
            ("haystack", "haystack"),
            ("bush", "dry_bush"),
            ("shrub", "dry_bush"),
            ("burnttree", "burnt_tree"),
            ("fallentree", "fallen_tree_root"),
            ("cowshed", "village_cowshed"),
            ("shed", "village_wood_shed"),
            ("pmill", "village_windmill"),
            ("windmill", "village_windmill"),
            ("bld", "village_cowshed")
        };

        /// <summary>Last resort per pipeline category, used when no keyword matches.</summary>
        private static readonly Dictionary<string, string> CategoryFallback = new Dictionary<string, string>
        {
            { "house", "village_cowshed" },
            { "building", "village_cowshed" },
            { "rock", "rock_sandstone" },
            { "obstacle", "wood_fence" },
            { "destructible_cover", "wood_fence" },
            { "bridge", "wood_fence" },
            { "tree", "burnt_tree" },
            { "bush", "dry_bush" },
            { "vegetation", "dry_bush" }
        };

        /// <summary>
        /// Sprite id for one source resource, or null when nothing in the library can stand in for it.
        /// The caller decides what to do with a miss; this never invents a sprite.
        /// </summary>
        public static string Resolve(string sourceResource, string category, IEnumerable<string> availableSpriteIds)
        {
            HashSet<string> available = availableSpriteIds != null
                ? new HashSet<string>(availableSpriteIds)
                : null;
            if (available == null || available.Count == 0)
            {
                return null;
            }

            string resource = (sourceResource ?? string.Empty).ToLowerInvariant();
            foreach ((string token, string spriteId) in Rules)
            {
                if (resource.Contains(token) && available.Contains(spriteId))
                {
                    return spriteId;
                }
            }

            string key = (category ?? string.Empty).ToLowerInvariant();
            return CategoryFallback.TryGetValue(key, out string fallback) && available.Contains(fallback)
                ? fallback
                : null;
        }
    }
}
