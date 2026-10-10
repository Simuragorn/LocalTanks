using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    [Serializable]
    public sealed class AiDuelLabCase
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public int seed;
        [Min(5f)] public float durationSeconds = 45f;
        public Vector2Int teamAStart;
        public float teamAHeading = -90f;
        public Vector2Int[] teamARoute;
        public Vector2Int teamBStart;
        public float teamBHeading = 90f;
        public Vector2Int[] teamBRoute;
    }

    public sealed class AiDuelLabCatalog : ScriptableObject
    {
        public AiDuelLabCase[] cases;

        public string[] Validate(NavigationMap map = null)
        {
            List<string> errors = new List<string>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (cases == null || cases.Length == 0)
            {
                errors.Add("AI duel lab catalog contains no cases.");
                return errors.ToArray();
            }

            foreach (AiDuelLabCase item in cases)
            {
                if (item == null)
                {
                    errors.Add("AI duel lab catalog contains a null case.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id))
                    errors.Add($"AI duel case id '{item.id}' is empty or duplicated.");
                if (string.IsNullOrWhiteSpace(item.displayName))
                    errors.Add($"AI duel case '{item.id}' has no display name.");
                if (item.durationSeconds < 5f)
                    errors.Add($"AI duel case '{item.id}' is shorter than five seconds.");
                ValidateRoute(item.id, "Team A", item.teamAStart, item.teamARoute, map, errors);
                ValidateRoute(item.id, "Team B", item.teamBStart, item.teamBRoute, map, errors);
            }
            return errors.ToArray();
        }

        private static void ValidateRoute(
            string caseId,
            string team,
            Vector2Int start,
            Vector2Int[] route,
            NavigationMap map,
            ICollection<string> errors)
        {
            if (route == null || route.Length < 2)
            {
                errors.Add($"AI duel case '{caseId}' needs at least two {team} route cells.");
                return;
            }
            if (route[0] != start)
                errors.Add($"AI duel case '{caseId}' {team} route must begin at its start cell.");
            if (map == null) return;
            foreach (Vector2Int cell in route)
            {
                if (!map.Contains(cell) || map.IsBlocked(cell))
                    errors.Add($"AI duel case '{caseId}' {team} route contains blocked or missing cell {cell}.");
            }
        }
    }
}
