using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Shape used for a static obstacle footprint. The conversion pipeline reduces every World of
    /// Tanks primitive to one of these, because the exact mesh is neither needed nor wanted in a 2D
    /// top-down game.
    /// </summary>
    public enum MapObstacleShape
    {
        Rectangle,
        Ellipse,
        Circle
    }

    /// <summary>How a piece of cover behaves, so AI and gameplay read one flag instead of a name.</summary>
    public enum MapCoverKind
    {
        None,
        Bush,
        Tree
    }

    /// <summary>
    /// One static obstacle of an imported arena, in Unity units already centred on the map origin.
    /// </summary>
    [Serializable]
    public sealed class WoTMapObstacle
    {
        public string id;
        public string category;
        public MapObstacleShape shape;
        public Vector2 center;
        public Vector2 size;
        public float rotationDegrees;
        public bool destructible;
        public bool blocksMovement;
        public bool blocksProjectiles;
        public bool blocksVision;

        /// <summary>Name of the source WoT resource, kept so art can be matched to the original model.</summary>
        public string sourceResource;
    }

    /// <summary>One tree or bush placement, with the concealment radius it occupies.</summary>
    [Serializable]
    public sealed class WoTMapVegetation
    {
        public Vector2 center;
        public float radius;
        public MapCoverKind kind;
        public bool destructible;
        public string sourceResource;
    }

    /// <summary>
    /// An arena converted from World of Tanks, ready for the scene builder. This is the only map
    /// representation the runtime and the builders read: terrain heights, water cells and the
    /// navigation grid all index into it, so nothing has to re-parse the source JSON.
    /// </summary>
    [CreateAssetMenu(menuName = "Local Tanks/WoT Map", fileName = "wot_map")]
    public sealed class WoTMapAsset : ScriptableObject
    {
        /// <summary>
        /// Upper bound on navigation samples. The grid is serialised into the scene, so its size is a
        /// hard cost: 40 000 cells is comfortable, a million is not. See ADR-053.
        /// </summary>
        public const int MaximumNavigationCells = 250_000;

        [Header("Identity")]
        public string mapId;
        public string displayName;
        public string englishName;
        public string clientVersion;
        public string spaceSha256;

        [Header("World placement")]
        [Tooltip("Unity units per source metre. The fleet scale is one world scale, so this stays uniform.")]
        public float metresPerUnit = 3.835f;

        [Tooltip("Centre of the playable area in Unity units. Placing the centre at the origin keeps " +
                 "coordinates small and lets the camera start on the action.")]
        public Vector2 worldCenter;

        [Tooltip("Playable extent in Unity units.")]
        public Vector2 playableSize;

        [Tooltip("Centre of the playable area in source metres. Every conversion from source metres " +
                 "subtracts it, so the imported map is centred on the origin whatever the client used.")]
        public Vector2 sourceCenterMetres;

        [Header("Terrain")]
        [Tooltip("Square cell of the elevation grid in Unity units.")]
        public float terrainCellSize;
        public int terrainColumns;
        public int terrainRows;

        [Tooltip("Row-major heights in source metres, row 0 at the southern edge.")]
        public float[] terrainHeightsMetres;
        public float terrainMinimumMetres;
        public float terrainMaximumMetres;

        [Header("Water")]
        public float waterCellSize;
        public int waterColumns;
        public int waterRows;

        [Tooltip("Indices of water cells in a row-major grid, in Unity units.")]
        public int[] waterCellIndices;

        [Header("Gameplay")]
        public Vector2[] basePositions;
        public Vector2[] baseSpawnPositions;

        [Header("Content")]
        public WoTMapObstacle[] obstacles;
        public WoTMapVegetation[] vegetation;

        [Header("Navigation")]
        [Tooltip("Navigation is deliberately coarser than the colliders: it answers routing, cost and " +
                 "terrain kind, while the colliders answer hits and collisions.")]
        public float navigationCellSize = 4f;
        public int navigationColumns;
        public int navigationRows;

        [Tooltip("Row-major terrain kind per navigation cell.")]
        public int[] navigationTerrain;

        [Tooltip("Row-major blocked flag per navigation cell.")]
        public bool[] navigationBlocked;

        public int ObstacleCount => obstacles?.Length ?? 0;
        public int VegetationCount => vegetation?.Length ?? 0;
        public int WaterCellCount => waterCellIndices?.Length ?? 0;

        public Vector2 WorldOrigin => worldCenter - playableSize * 0.5f;

        /// <summary>Converts a source position in metres to Unity units centred on <see cref="worldCenter"/>.</summary>
        public Vector2 FromSourceMetres(Vector2 meters)
        {
            Vector2 centred = (meters - sourceCenterMetres) / metresPerUnit;
            // World of Tanks uses a left-handed X/Z plane; Unity 2D uses X/Y with Y up.
            return worldCenter + centred;
        }

        /// <summary>
        /// World position of a grid sample. Cells and samples are addressed by their lower-left corner,
        /// so a grid that starts on the playable edge and spans the playable size agrees with its
        /// neighbours exactly: the 41 elevation samples 20 m apart, the 81 water samples 10 m apart and
        /// the 201 navigation samples 4 m apart all cover the same 800 m field without a half cell of
        /// drift between layers.
        /// </summary>
        public Vector2 SampleToWorld(int column, int row, float stepMeters)
        {
            Vector2 origin = worldCenter - playableSize * 0.5f;
            return origin + new Vector2(column, row) * (stepMeters / metresPerUnit);
        }

        /// <summary>Counts every problem that would make the asset unusable, for the importer and tests.</summary>
        public List<string> Validate()
        {
            List<string> errors = new List<string>();
            if (string.IsNullOrWhiteSpace(mapId)) errors.Add("Map id is required.");
            if (metresPerUnit <= 0f) errors.Add("Metres per unit must be positive.");
            if (playableSize.x <= 0f || playableSize.y <= 0f) errors.Add("Playable size must be positive.");
            if (terrainColumns <= 0 || terrainRows <= 0) errors.Add("Terrain grid must have positive size.");
            if (terrainHeightsMetres == null || terrainHeightsMetres.Length != terrainColumns * terrainRows)
            {
                errors.Add("Terrain heights must cover the whole terrain grid.");
            }

            if (waterColumns < 0 || waterRows < 0) errors.Add("Water grid size cannot be negative.");
            if (waterCellIndices != null && waterColumns > 0 && waterRows > 0)
            {
                int cells = waterColumns * waterRows;
                foreach (int index in waterCellIndices)
                {
                    if (index < 0 || index >= cells)
                    {
                        errors.Add($"Water cell index {index} is outside the {waterColumns}x{waterRows} grid.");
                        break;
                    }
                }
            }

            if (basePositions == null || basePositions.Length < 2)
            {
                errors.Add("At least two bases are required.");
            }

            if (navigationColumns <= 0 || navigationRows <= 0)
            {
                errors.Add("Navigation grid must have positive size.");
            }
            else
            {
                int cells = navigationColumns * navigationRows;
                if (navigationTerrain == null || navigationTerrain.Length != cells)
                {
                    errors.Add("Navigation terrain must cover the whole navigation grid.");
                }

                if (navigationBlocked == null || navigationBlocked.Length != cells)
                {
                    errors.Add("Navigation blocked flags must cover the whole navigation grid.");
                }
            }

            // A finer navigation grid than the colliders would buy nothing and grow with the square of
            // the difference. The real limit is the cell count, because the grid is serialised into the
            // scene: see ADR-053, where 0.75 m over an 800 m field meant over a million cells.
            if (navigationColumns > 0 && navigationRows > 0)
            {
                int navigationCells = navigationColumns * navigationRows;
                if (navigationCells > MaximumNavigationCells)
                {
                    errors.Add($"Navigation grid has {navigationCells} cells, above the limit of " +
                               $"{MaximumNavigationCells}. A coarser navigation step keeps the scene " +
                               "loadable; exact collision is the colliders' job, not the grid's.");
                }
            }

            if (waterCellIndices != null && waterCellIndices.Length > 0 && waterCellSize <= 0f)
            {
                errors.Add("Water cells are present but the water cell size is not positive.");
            }

            if (obstacles != null)
            {
                for (int index = 0; index < obstacles.Length; index++)
                {
                    WoTMapObstacle obstacle = obstacles[index];
                    if (obstacle == null)
                    {
                        errors.Add($"Obstacle {index} is null.");
                        continue;
                    }

                    if (obstacle.size.x < 0f || obstacle.size.y < 0f)
                    {
                        errors.Add($"Obstacle '{obstacle.id}' has a negative size.");
                        break;
                    }
                }
            }

            return errors;
        }
    }
}
