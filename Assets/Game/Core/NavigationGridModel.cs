using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public sealed class NavigationGridModel
    {
        private readonly float[] costs;
        private readonly bool[] blocked;

        public int Width { get; }
        public int Height { get; }

        public NavigationGridModel(int width, int height, float defaultCost = 1f)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Navigation grid dimensions must be positive.");
            }

            Width = width;
            Height = height;
            costs = new float[width * height];
            blocked = new bool[width * height];
            for (int index = 0; index < costs.Length; index++)
            {
                costs[index] = Mathf.Max(0.01f, defaultCost);
            }
        }

        public bool Contains(Vector2Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;
        }

        public void SetCell(Vector2Int cell, float cost, bool isBlocked)
        {
            int index = GetIndex(cell);
            costs[index] = Mathf.Max(0.01f, cost);
            blocked[index] = isBlocked;
        }

        public void SetBlocked(Vector2Int cell, bool value)
        {
            blocked[GetIndex(cell)] = value;
        }

        public float GetCost(Vector2Int cell)
        {
            return costs[GetIndex(cell)];
        }

        public bool IsBlocked(Vector2Int cell)
        {
            return blocked[GetIndex(cell)];
        }

        public bool IsAreaWalkable(Vector2Int center, int clearance)
        {
            int radius = Mathf.Max(0, clearance);
            for (int y = center.y - radius; y <= center.y + radius; y++)
            {
                for (int x = center.x - radius; x <= center.x + radius; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!Contains(cell) || IsBlocked(cell))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public int GetIndex(Vector2Int cell)
        {
            if (!Contains(cell))
            {
                throw new ArgumentOutOfRangeException(nameof(cell), $"Cell {cell} is outside {Width}x{Height} grid.");
            }

            return cell.y * Width + cell.x;
        }

        public Vector2Int GetCell(int index)
        {
            if (index < 0 || index >= costs.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new Vector2Int(index % Width, index / Width);
        }
    }

    public static class WeightedAStar
    {
        private static readonly Vector2Int[] Directions =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1),
            new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };

        public static List<Vector2Int> FindPath(
            NavigationGridModel grid,
            Vector2Int start,
            Vector2Int destination,
            int clearance = 0,
            float heuristicWeight = 1.1f)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (!grid.Contains(start) || !grid.Contains(destination) ||
                !grid.IsAreaWalkable(start, clearance) || !grid.IsAreaWalkable(destination, clearance))
            {
                return new List<Vector2Int>();
            }

            int cellCount = grid.Width * grid.Height;
            float[] distances = new float[cellCount];
            int[] previous = new int[cellCount];
            bool[] closed = new bool[cellCount];
            for (int index = 0; index < cellCount; index++)
            {
                distances[index] = float.PositiveInfinity;
                previous[index] = -1;
            }

            int startIndex = grid.GetIndex(start);
            int destinationIndex = grid.GetIndex(destination);
            distances[startIndex] = 0f;
            MinHeap open = new MinHeap();
            open.Push(startIndex, Heuristic(start, destination) * Mathf.Max(1f, heuristicWeight));

            while (open.Count > 0)
            {
                int currentIndex = open.Pop();
                if (closed[currentIndex])
                {
                    continue;
                }

                if (currentIndex == destinationIndex)
                {
                    return Reconstruct(grid, previous, destinationIndex);
                }

                closed[currentIndex] = true;
                Vector2Int current = grid.GetCell(currentIndex);
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = current + direction;
                    if (!grid.Contains(next) || !grid.IsAreaWalkable(next, clearance) ||
                        IsDiagonalBlocked(grid, current, direction, clearance))
                    {
                        continue;
                    }

                    int nextIndex = grid.GetIndex(next);
                    if (closed[nextIndex])
                    {
                        continue;
                    }

                    float stepLength = direction.x != 0 && direction.y != 0 ? 1.41421356f : 1f;
                    float candidate = distances[currentIndex] + grid.GetCost(next) * stepLength;
                    if (candidate >= distances[nextIndex])
                    {
                        continue;
                    }

                    distances[nextIndex] = candidate;
                    previous[nextIndex] = currentIndex;
                    float priority = candidate + Heuristic(next, destination) * Mathf.Max(1f, heuristicWeight);
                    open.Push(nextIndex, priority);
                }
            }

            return new List<Vector2Int>();
        }

        private static bool IsDiagonalBlocked(
            NavigationGridModel grid,
            Vector2Int current,
            Vector2Int direction,
            int clearance)
        {
            if (direction.x == 0 || direction.y == 0)
            {
                return false;
            }

            return !grid.IsAreaWalkable(current + new Vector2Int(direction.x, 0), clearance) ||
                   !grid.IsAreaWalkable(current + new Vector2Int(0, direction.y), clearance);
        }

        private static float Heuristic(Vector2Int from, Vector2Int to)
        {
            int dx = Mathf.Abs(from.x - to.x);
            int dy = Mathf.Abs(from.y - to.y);
            return Mathf.Max(dx, dy) + 0.41421356f * Mathf.Min(dx, dy);
        }

        private static List<Vector2Int> Reconstruct(NavigationGridModel grid, int[] previous, int destinationIndex)
        {
            List<Vector2Int> path = new List<Vector2Int>();
            int current = destinationIndex;
            while (current >= 0)
            {
                path.Add(grid.GetCell(current));
                current = previous[current];
            }

            path.Reverse();
            return path;
        }

        private sealed class MinHeap
        {
            private readonly List<Entry> entries = new List<Entry>();
            public int Count => entries.Count;

            public void Push(int cellIndex, float priority)
            {
                entries.Add(new Entry(cellIndex, priority));
                int index = entries.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (entries[parent].Priority <= entries[index].Priority)
                    {
                        break;
                    }

                    Swap(parent, index);
                    index = parent;
                }
            }

            public int Pop()
            {
                int result = entries[0].CellIndex;
                int last = entries.Count - 1;
                entries[0] = entries[last];
                entries.RemoveAt(last);
                int index = 0;
                while (index < entries.Count)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    if (left >= entries.Count)
                    {
                        break;
                    }

                    int smallest = right < entries.Count && entries[right].Priority < entries[left].Priority
                        ? right
                        : left;
                    if (entries[index].Priority <= entries[smallest].Priority)
                    {
                        break;
                    }

                    Swap(index, smallest);
                    index = smallest;
                }

                return result;
            }

            private void Swap(int first, int second)
            {
                Entry value = entries[first];
                entries[first] = entries[second];
                entries[second] = value;
            }

            private readonly struct Entry
            {
                public readonly int CellIndex;
                public readonly float Priority;

                public Entry(int cellIndex, float priority)
                {
                    CellIndex = cellIndex;
                    Priority = priority;
                }
            }
        }
    }
}
