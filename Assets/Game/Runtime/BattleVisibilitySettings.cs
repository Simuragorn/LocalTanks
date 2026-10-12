using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Per-scene visibility presentation settings, stored on the scene's "BattleSystems" object.
    /// <see cref="BattleDirector"/> copies these values into every tank it spawns, so a scene owns
    /// its own choice instead of duplicating the value across fourteen prefabs.
    /// </summary>
    /// <remarks>
    /// <see cref="showHiddenTanksForTesting"/> is a developer aid: when it is enabled, a tank that
    /// the team cannot see stays rendered as a semi-transparent silhouette at
    /// <see cref="hiddenTankAlpha"/>. It is meant for the test laboratories, where observing enemy
    /// movement is the point; a normal battle leaves it disabled so an unseen tank is invisible.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BattleVisibilitySettings : MonoBehaviour
    {
        [Tooltip("Keep unseen tanks rendered as a semi-transparent silhouette. Developer aid for test scenes.")]
        [SerializeField] private bool showHiddenTanksForTesting;

        [Tooltip("Alpha applied to an unseen tank when the testing silhouette is enabled.")]
        [SerializeField, Range(0.05f, 0.9f)] private float hiddenTankAlpha = 0.28f;

        public bool ShowHiddenTanksForTesting => showHiddenTanksForTesting;
        public float HiddenTankAlpha => hiddenTankAlpha;

        /// <summary>
        /// Enables or disables the semi-transparent silhouette for this scene. Called by the scene
        /// builder and by a laboratory controller that opts into the developer aid at runtime.
        /// </summary>
        public void Configure(bool showHiddenForTesting, float hiddenAlpha = 0.28f)
        {
            showHiddenTanksForTesting = showHiddenForTesting;
            hiddenTankAlpha = Mathf.Clamp(hiddenAlpha, 0.05f, 0.9f);
        }
    }
}
