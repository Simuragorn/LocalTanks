using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Geometry of the two track prints a moving tank leaves on the ground.
    /// </summary>
    /// <remarks>
    /// The marks are not a <c>TrailRenderer</c>. On a tight turn or a pivot in place a single trail
    /// twists around itself and folds the ribbon over the hull, because one trail has one direction for
    /// the whole machine. Real tracks are two independent lines that can even move in opposite
    /// directions, so every print is placed separately with its own direction.
    /// <para>
    /// Everything here is plain arithmetic on purpose: it is the part of the effect that can be wrong
    /// without looking wrong, so Edit Mode tests cover it without a scene.
    /// </para>
    /// </remarks>
    public static class TrackMarkMath
    {
        /// <summary>
        /// Speed of each track of a machine that moves forward at <paramref name="forwardSpeed"/> while
        /// its hull rotates at <paramref name="clockwiseTurnRateDegreesPerSecond"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="TankMotor"/> turns the hull with <c>rotation - steering * turnSpeed * dt</c> and
        /// reports <c>CurrentTurnRate = steering * turnSpeed</c>, so a positive turn rate is a clockwise
        /// turn, that is a turn to the right, and a point on the right track then travels slower than the
        /// hull centre while the left track travels faster. That differential is what lets a tracked
        /// vehicle pivot at all.
        /// <para>
        /// Note that this is the opposite sign convention from the tread scroll in
        /// <c>TankTrackAnimator.SplitTrackDistances</c>, which takes the counter-clockwise rotation delta
        /// the rigid body actually gained. Both compute the same physical split; only the input differs,
        /// and the parameter name is spelled out here so the two cannot be confused silently.
        /// </para>
        /// </remarks>
        public static void SplitTrackSpeeds(
            float forwardSpeed,
            float clockwiseTurnRateDegreesPerSecond,
            float halfTrackWidth,
            out float leftSpeed,
            out float rightSpeed)
        {
            float angularVelocity = -clockwiseTurnRateDegreesPerSecond * Mathf.Deg2Rad;
            float half = Mathf.Max(0f, halfTrackWidth);
            rightSpeed = forwardSpeed + angularVelocity * half;
            leftSpeed = forwardSpeed - angularVelocity * half;
        }

        /// <summary>
        /// Direction a track contact point actually travels. A rotating hull moves its tracks sideways
        /// relative to the hull axis, so a pivot in place still leaves a print and the print is an arc,
        /// not a line along the hull.
        /// </summary>
        /// <param name="forward">Hull forward axis in world space, normally <c>transform.up</c>.</param>
        /// <param name="right">Hull right axis in world space, normally <c>transform.right</c>.</param>
        /// <param name="lateralOffset">Signed distance of the track from the hull centre line.</param>
        public static Vector2 TrackVelocity(
            Vector2 forward,
            Vector2 right,
            float forwardSpeed,
            float clockwiseTurnRateDegreesPerSecond,
            float lateralOffset)
        {
            float angularVelocity = -clockwiseTurnRateDegreesPerSecond * Mathf.Deg2Rad;
            return forward * forwardSpeed + right * (angularVelocity * lateralOffset);
        }
    }

    /// <summary>
    /// Distance accumulator for one track: it turns covered length into whole prints and keeps the
    /// remainder, so spacing never depends on the frame rate. A tank that moves twice as fast lays
    /// twice as many prints, not twice as long a gap.
    /// </summary>
    public struct TrackMarkTrail
    {
        /// <summary>
        /// Upper bound of prints one track may emit in a single step. A paused or lagging frame can
        /// report a large travelled distance, and without this bound the accumulator would try to catch
        /// up by spawning hundreds of objects that no one would ever see.
        /// </summary>
        public const int MaximumPrintsPerStep = 4;

        private float pendingDistance;
        private int droppedPrints;

        /// <summary>Length covered since the last print, smaller than the configured spacing.</summary>
        public float PendingDistance => pendingDistance;

        /// <summary>Prints that were skipped because one step covered too much ground.</summary>
        public int DroppedPrints => droppedPrints;

        /// <summary>
        /// Adds covered length and returns how many prints belong at the start of this step.
        /// </summary>
        public int Consume(float travelledDistance, float spacing)
        {
            if (spacing <= 0f || travelledDistance <= 0f || float.IsNaN(travelledDistance))
            {
                return 0;
            }

            pendingDistance += travelledDistance;
            int count = (int)(pendingDistance / spacing);
            if (count > MaximumPrintsPerStep)
            {
                droppedPrints += count - MaximumPrintsPerStep;
                count = MaximumPrintsPerStep;
            }

            if (count > 0)
            {
                pendingDistance -= count * spacing;
            }

            return count;
        }

        public void Reset()
        {
            pendingDistance = 0f;
            droppedPrints = 0;
        }
    }
}
