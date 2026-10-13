using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Which overlays currently own the pointer and the keyboard.
    /// </summary>
    /// <remarks>
    /// One place, because every reader that must stay quiet while an overlay is open has to agree on it:
    /// the tank must not fire when a slider is being dragged, developer keys must not act behind a modal
    /// window, and a future chat or pause panel joins the same set instead of adding its own flag.
    ///
    /// Owners are held as objects rather than counted, so two overlays closing in any order cannot leave
    /// a stuck flag, and a destroyed overlay stops counting the moment it is gone.
    /// </remarks>
    public static class ModalUiState
    {
        private static readonly HashSet<Object> Owners = new HashSet<Object>();

        public static bool AnyOpen
        {
            get
            {
                Owners.RemoveWhere(owner => owner == null);
                return Owners.Count > 0;
            }
        }

        public static int OpenCount => Owners.Count;

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
    }
}
