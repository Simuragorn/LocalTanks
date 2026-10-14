using System;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Loads the generated arena grid into its runtime navigation map. The source asset remains the one
    /// serialized copy; runtime arrays are cloned because destructible cover may open cells during a match.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(NavigationMap))]
    public sealed class WoTNavigationMapSource : MonoBehaviour
    {
        [SerializeField] private WoTMapAsset source;

        public WoTMapAsset Source => source;

        public void Configure(WoTMapAsset mapAsset)
        {
            source = mapAsset;
        }

        private void Awake()
        {
            if (source == null)
            {
                throw new InvalidOperationException($"{name} has no generated map source.");
            }

            int count = source.navigationColumns * source.navigationRows;
            TerrainKind[] terrain = new TerrainKind[count];
            float[] costs = CopyOrFill(source.navigationCosts, count, 1f);
            float[] speeds = CopyOrFill(source.navigationSpeedMultipliers, count, 1f);
            bool[] blocked = source.navigationBlocked != null
                ? (bool[])source.navigationBlocked.Clone()
                : new bool[count];

            for (int index = 0; index < count; index++)
            {
                terrain[index] = (TerrainKind)source.navigationTerrain[index];
                // Route cost already steers bots around small source clutter that has no collider. A
                // physical slowdown there would look like an invisible obstacle, so only the cost remains.
                if (terrain[index] == TerrainKind.Obstacle) speeds[index] = 1f;
            }

            GetComponent<NavigationMap>().Configure(
                source.navigationColumns,
                source.navigationRows,
                source.navigationCellSize,
                source.worldCenter - source.playableSize * 0.5f,
                terrain,
                costs,
                speeds,
                (float[])speeds.Clone(),
                blocked);
        }

        private static float[] CopyOrFill(float[] values, int count, float fallback)
        {
            if (values != null && values.Length == count) return (float[])values.Clone();
            float[] result = new float[count];
            for (int index = 0; index < count; index++) result[index] = fallback;
            return result;
        }
    }
}
