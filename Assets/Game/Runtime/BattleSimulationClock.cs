using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Runs a battle faster than real time for diagnosis, without changing the physics step.
    /// <para>
    /// Two different things make a battle slow to study, and they need separate controls. Simulation time
    /// can advance quickly, which is what <see cref="Time.timeScale"/> does. Frames can also be produced
    /// faster than the display would, which is what <see cref="Time.captureDeltaTime"/> does: a larger
    /// captured delta makes the engine run more fixed steps per frame instead of waiting for a monitor.
    /// </para>
    /// <para>
    /// This component raises both, and never changes <c>Time.fixedDeltaTime</c>. That restraint is the
    /// point: the physics step stays 0.02 seconds, so a fast run produces the same trajectory as a slow
    /// one and a result measured at speed still describes the real game. Changing the fixed step instead
    /// would make bullets tunnel and turns overshoot, and every measurement would be about a game that
    /// does not exist.
    /// </para>
    /// <para>
    /// The controller is attached only when the command line asks for it, so an ordinary battle is
    /// untouched. See <c>-localtanksSpeed &lt;multiplier&gt;</c>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleSimulationClock : MonoBehaviour
    {
        /// <summary>Command line switch: <c>-localtanksSpeed 50</c>.</summary>
        public const string SpeedSwitchName = "-localtanksSpeed";

        /// <summary>Command line switch limiting how long a batch run may take, in simulated seconds.</summary>
        public const string LimitSwitchName = "-localtanksSeconds";

        /// <summary>Highest multiplier accepted, so a typo cannot ask for a millionfold run.</summary>
        public const float MaximumMultiplier = 200f;

        /// <summary>
        /// Ceiling on captured delta, in seconds. Beyond roughly a tenth of a second Unity gives up on
        /// catching up and stops running fixed steps, so a larger ask would silently do nothing.
        /// </summary>
        private const float MaximumCaptureDelta = 0.1f;

        private float multiplier = 1f;
        private float previousTimeScale = 1f;
        private float previousCaptureDeltaTime;
        private bool applied;

        /// <summary>Multiplier requested on the command line, or 1 when none was.</summary>
        public static float RequestedMultiplier
        {
            get
            {
                string raw = CommandLineValue(SpeedSwitchName);
                if (raw == null) return 1f;
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    Debug.LogWarning($"Ignoring {SpeedSwitchName} '{raw}': not a number.");
                    return 1f;
                }

                return Mathf.Clamp(value, 1f, MaximumMultiplier);
            }
        }

        /// <summary>Simulated seconds a batch run should stop after, or 0 when unbounded.</summary>
        public static float RequestedLimitSeconds
        {
            get
            {
                string raw = CommandLineValue(LimitSwitchName);
                if (raw == null) return 0f;
                return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    ? Mathf.Max(0f, value)
                    : 0f;
            }
        }

        public float Multiplier => multiplier;

        /// <summary>Attaches the clock when the command line asked for a faster run, else returns null.</summary>
        public static BattleSimulationClock AttachWhenRequested(BattleDirector director)
        {
            if (director == null) return null;
            float requested = RequestedMultiplier;
            if (requested <= 1f) return null;
            BattleSimulationClock clock = director.gameObject.GetComponent<BattleSimulationClock>();
            if (clock == null) clock = director.gameObject.AddComponent<BattleSimulationClock>();
            clock.Apply(requested);
            return clock;
        }

        /// <summary>Raises the simulation rate. Does not touch the physics step.</summary>
        public void Apply(float wanted)
        {
            if (!applied)
            {
                previousTimeScale = Time.timeScale;
                previousCaptureDeltaTime = Time.captureDeltaTime;
                applied = true;
            }

            multiplier = Mathf.Clamp(wanted, 1f, MaximumMultiplier);
            Time.timeScale = multiplier;
            // A captured delta larger than the frame budget would stall the loop, so it is capped and the
            // remaining speedup comes from timeScale, which does not change step size.
            Time.captureDeltaTime = Mathf.Min(Time.fixedDeltaTime * multiplier, MaximumCaptureDelta);
        }

        /// <summary>Restores the rates that were in force before this clock applied its own.</summary>
        public void Restore()
        {
            if (!applied) return;
            Time.timeScale = previousTimeScale;
            Time.captureDeltaTime = previousCaptureDeltaTime;
            applied = false;
        }

        private void OnDestroy()
        {
            Restore();
        }

        private static string CommandLineValue(string switchName)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], switchName, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }
    }
}
