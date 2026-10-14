using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Central ownership of gameplay input by modal overlays and non-modal pointer regions.
    /// </summary>
    /// <remarks>
    /// One place, because every reader that must stay quiet while an overlay is open has to agree on it:
    /// the tank must not fire when a slider is being dragged, the turret and camera must not react behind a
    /// non-modal diagnostic panel, developer keys must not act behind a modal window, and a future chat or
    /// pause panel joins the same contract instead of adding its own flag.
    ///
    /// Owners are held as objects rather than counted, so two overlays closing in any order cannot leave
    /// a stuck flag, and a destroyed overlay stops counting the moment it is gone.
    /// </remarks>
    public static class ModalUiState
    {
        private static readonly HashSet<Object> Owners = new HashSet<Object>();
        private static readonly Dictionary<Object, Rect> PointerRegions = new Dictionary<Object, Rect>();

        public static bool AnyOpen
        {
            get
            {
                Owners.RemoveWhere(owner => owner == null);
                return Owners.Count > 0;
            }
        }

        public static int OpenCount
        {
            get
            {
                RemoveDestroyedOwners();
                return Owners.Count;
            }
        }

        public static int PointerRegionCount
        {
            get
            {
                RemoveDestroyedOwners();
                return PointerRegions.Count;
            }
        }

        public static void Set(Object owner, bool open)
        {
            if (owner == null)
            {
                return;
            }

            if (open)
            {
                Owners.Add(owner);
            }
            else
            {
                Owners.Remove(owner);
            }
        }

        /// <summary>
        /// Registers one non-modal IMGUI region in top-left GUI coordinates. Gameplay keeps keyboard
        /// movement available, but mouse aiming, firing and zoom are ignored while the pointer is inside.
        /// Calling this every OnGUI pass also keeps the region aligned with responsive windows.
        /// </summary>
        public static void SetPointerRegion(Object owner, Rect region)
        {
            if (owner == null) return;
            PointerRegions[owner] = region;
        }

        public static void ClearPointerRegion(Object owner)
        {
            if (owner != null) PointerRegions.Remove(owner);
        }

        public static bool BlocksPointer(Vector2 screenPosition)
        {
            return BlocksPointer(screenPosition, Screen.height);
        }

        public static bool BlocksPointer(Vector2 screenPosition, float screenHeight)
        {
            if (AnyOpen) return true;
            RemoveDestroyedOwners();
            Vector2 guiPosition = new Vector2(screenPosition.x, screenHeight - screenPosition.y);
            foreach (Rect region in PointerRegions.Values)
            {
                if (region.Contains(guiPosition)) return true;
            }

            return false;
        }

        private static void RemoveDestroyedOwners()
        {
            Owners.RemoveWhere(owner => owner == null);
            if (PointerRegions.Count == 0) return;
            List<Object> destroyed = null;
            foreach (Object owner in PointerRegions.Keys)
            {
                if (owner != null) continue;
                destroyed ??= new List<Object>();
                destroyed.Add(owner);
            }

            if (destroyed == null) return;
            foreach (Object owner in destroyed) PointerRegions.Remove(owner);
        }
    }
}
