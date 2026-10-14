using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    [Serializable]
    public sealed class AiSquadLabCase
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public int seed;
        [Min(10f)] public float durationSeconds = 40f;
    }

    /// <summary>
    /// Reproducible seeds for the small group laboratory. Geometry, roster and routes belong to the
    /// battle scenario; a case changes only the random stream and observation duration, so results are
    /// directly comparable instead of quietly changing two inputs at once.
    /// </summary>
    public sealed class AiSquadLabCatalog : ScriptableObject
    {
        public AiSquadLabCase[] cases;

        public string[] Validate()
        {
            List<string> errors = new List<string>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> seeds = new HashSet<int>();
            if (cases == null || cases.Length < 3)
            {
                errors.Add("AI squad lab needs at least three cases for a seed distribution.");
                return errors.ToArray();
            }

            foreach (AiSquadLabCase item in cases)
            {
                if (item == null)
                {
                    errors.Add("AI squad lab catalog contains a null case.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id))
                    errors.Add($"AI squad case id '{item.id}' is empty or duplicated.");
                if (string.IsNullOrWhiteSpace(item.displayName))
                    errors.Add($"AI squad case '{item.id}' has no display name.");
                if (!seeds.Add(item.seed))
                    errors.Add($"AI squad case '{item.id}' duplicates seed {item.seed}.");
                if (item.durationSeconds < 10f)
                    errors.Add($"AI squad case '{item.id}' is shorter than ten seconds.");
            }

            return errors.ToArray();
        }
    }
}
