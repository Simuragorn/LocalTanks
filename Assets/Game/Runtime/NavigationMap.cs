using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public enum TerrainKind
    {
        Grass,
        Road,
        Mud,
        ShallowWater,
        Blocked
    }

    public sealed class NavigationMap : MonoBehaviour
    {
        [SerializeField, Min(1)] private int width = 1;
        [SerializeField, Min(1)] private int height = 1;
        [SerializeField, Min(0.01f)] private float cellSize = 0.5f;
        [SerializeField] private Vector2 worldOrigin;
        [SerializeField] private TerrainKind[] terrain;
        [SerializeField] private float[] navigationCosts;
        [SerializeField] private float[] speedMultipliers;
        [SerializeField] private float[] accelerationMultipliers;
        [SerializeField] private bool[] blocked;

        private NavigationGridModel model;

        public int Width => width;
        public int Height => height;
        public float CellSize => cellSize;
        public Vector2 WorldOrigin => worldOrigin;
        public int Version { get; private set; }

        private void Awake()
        {
            BuildModel();
        }

        public void Configure(
            int mapWidth,
            int mapHeight,
            float mapCellSize,
            Vector2 origin,
            TerrainKind[] terrainKinds,
            float[] costs,
            float[] speeds,
            float[] accelerations,
            bool[] blockedCells)
        {
            width = Mathf.Max(1, mapWidth);
            height = Mathf.Max(1, mapHeight);
            cellSize = Mathf.Max(0.01f, mapCellSize);
            worldOrigin = origin;
            terrain = terrainKinds;
            navigationCosts = costs;
            speedMultipliers = speeds;
            accelerationMultipliers = accelerations;
            blocked = blockedCells;
            BuildModel();
        }

        public Vector2Int WorldToCell(Vector2 worldPosition)
        {
            Vector2 local = worldPosition - worldOrigin;
            return new Vector2Int(
                Mathf.FloorToInt(local.x / cellSize),
                Mathf.FloorToInt(local.y / cellSize));
        }

        public Vector2 CellToWorld(Vector2Int cell)
        {
            return worldOrigin + new Vector2((cell.x + 0.5f) * cellSize, (cell.y + 0.5f) * cellSize);
        }

        public bool Contains(Vector2Int cell)
        {
            EnsureModel();
            return model.Contains(cell);
        }

        public TerrainKind GetTerrain(Vector2Int cell)
        {
            if (!Contains(cell) || terrain == null)
            {
                return TerrainKind.Blocked;
            }

            return terrain[model.GetIndex(cell)];
        }

        public float GetSpeedMultiplier(Vector2Int cell)
        {
            return ReadMultiplier(speedMultipliers, cell);
        }

        public float GetAccelerationMultiplier(Vector2Int cell)
        {
            return ReadMultiplier(accelerationMultipliers, cell);
        }

        public float GetNavigationCost(Vector2Int cell)
        {
            if (!Contains(cell))
            {
                return float.PositiveInfinity;
            }

            return model.GetCost(cell);
        }

        public bool IsBlocked(Vector2Int cell)
        {
            return !Contains(cell) || model.IsBlocked(cell);
        }

        public void SetBlocked(Vector2Int cell, bool value)
        {
            if (!Contains(cell))
            {
                return;
            }

            int index = model.GetIndex(cell);
            if (blocked[index] == value)
            {
                return;
            }

            blocked[index] = value;
            model.SetBlocked(cell, value);
            Version++;
        }

        public void SetTerrain(
            Vector2Int cell,
            TerrainKind kind,
            float navigationCost,
            float speedMultiplier,
            float accelerationMultiplier,
            bool isBlocked)
        {
            if (!Contains(cell))
            {
                return;
            }

            int index = model.GetIndex(cell);
            terrain[index] = kind;
            navigationCosts[index] = Mathf.Max(0.01f, navigationCost);
            speedMultipliers[index] = Mathf.Max(0.05f, speedMultiplier);
            accelerationMultipliers[index] = Mathf.Max(0.05f, accelerationMultiplier);
            blocked[index] = isBlocked;
            model.SetCell(cell, navigationCosts[index], isBlocked);
            Version++;
        }

        public List<Vector2Int> FindCellPath(
            Vector2Int start,
            Vector2Int destination,
            int clearance = 0,
            float heuristicWeight = 1.1f)
        {
            EnsureModel();
            return WeightedAStar.FindPath(model, start, destination, clearance, heuristicWeight);
        }

        public List<Vector2> FindWorldPath(
            Vector2 start,
            Vector2 destination,
            int clearance = 0,
            float heuristicWeight = 1.1f)
        {
            List<Vector2Int> cells = FindCellPath(
                WorldToCell(start),
                WorldToCell(destination),
                clearance,
                heuristicWeight);
            List<Vector2> result = new List<Vector2>(cells.Count);
            foreach (Vector2Int cell in cells)
            {
                result.Add(CellToWorld(cell));
            }

            return result;
        }

        private void BuildModel()
        {
            int count = width * height;
            if (terrain == null || navigationCosts == null || speedMultipliers == null ||
                accelerationMultipliers == null || blocked == null ||
                terrain.Length != count || navigationCosts.Length != count ||
                speedMultipliers.Length != count || accelerationMultipliers.Length != count ||
                blocked.Length != count)
            {
                model = null;
                return;
            }

            model = new NavigationGridModel(width, height);
            for (int index = 0; index < count; index++)
            {
                model.SetCell(model.GetCell(index), navigationCosts[index], blocked[index]);
            }

            Version++;
        }

        private void EnsureModel()
        {
            if (model == null)
            {
                BuildModel();
            }

            if (model == null)
            {
                throw new InvalidOperationException("Navigation map arrays are missing or have invalid lengths.");
            }
        }

        private float ReadMultiplier(float[] values, Vector2Int cell)
        {
            if (!Contains(cell) || values == null)
            {
                return 1f;
            }

            return Mathf.Max(0.05f, values[model.GetIndex(cell)]);
        }
    }
}
