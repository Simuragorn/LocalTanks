using LocalTanks;
using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The geometry of track prints and the band arithmetic behind every effect size.
    /// </summary>
    /// <remarks>
    /// These are the parts of the visual layer that can be wrong without looking wrong: a sign error in the
    /// differential turns a left turn into a right one, a frame-rate dependent accumulator lays prints in
    /// bursts, and a band boundary silently gives a machine the wrong effect. None of them needs a scene.
    /// </remarks>
    public sealed class TrackMarkMathTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void SplitTrackSpeeds_GoingStraight_MovesBothTracksTogether()
        {
            TrackMarkMath.SplitTrackSpeeds(2f, 0f, 0.3f, out float left, out float right);

            Assert.That(left, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(right, Is.EqualTo(2f).Within(Tolerance));
        }

        [Test]
        public void SplitTrackSpeeds_TurningRight_SlowsTheRightTrack()
        {
            // The hull rotates as `rotation - steering * turnSpeed * dt`, so a positive turn rate is a turn
            // to the right, and a tracked vehicle turns right by slowing its right track.
            TrackMarkMath.SplitTrackSpeeds(2f, 60f, 0.5f, out float left, out float right);

            Assert.That(right, Is.LessThan(left));
            Assert.That(left, Is.GreaterThan(2f));
        }

        [Test]
        public void SplitTrackSpeeds_PivotInPlace_DrivesTheTracksInOppositeDirections()
        {
            TrackMarkMath.SplitTrackSpeeds(0f, 90f, 0.5f, out float left, out float right);

            Assert.That(left, Is.GreaterThan(0f));
            Assert.That(right, Is.LessThan(0f));
            Assert.That(left, Is.EqualTo(-right).Within(Tolerance),
                "A pivot in place is symmetric: neither track may be favoured.");
        }

        [Test]
        public void TrackVelocity_PivotInPlace_PointsAlongTheHullSide()
        {
            Vector2 velocity = TrackMarkMath.TrackVelocity(Vector2.up, Vector2.right, 0f, 90f, 0.5f);

            Assert.That(Vector2.Dot(velocity.normalized, Vector2.up), Is.EqualTo(0f).Within(0.001f),
                "A print laid during a pivot runs across the hull, which is why a single trail renderer " +
                "folds over itself and a per-track print does not.");
        }

        [Test]
        public void TrackMarkTrail_SpacingDoesNotDependOnTheFrameRate()
        {
            TrackMarkTrail coarse = new TrackMarkTrail();
            int coarsePrints = coarse.Consume(0.35f, 0.1f);

            TrackMarkTrail fine = new TrackMarkTrail();
            int finePrints = 0;
            for (int step = 0; step < 7; step++)
            {
                finePrints += fine.Consume(0.05f, 0.1f);
            }

            Assert.That(coarsePrints, Is.EqualTo(3));
            Assert.That(finePrints, Is.EqualTo(3),
                "The same distance must lay the same prints whether it was covered in one step or seven.");
        }

        [Test]
        public void TrackMarkTrail_KeepsTheRemainder()
        {
            TrackMarkTrail trail = new TrackMarkTrail();

            Assert.That(trail.Consume(0.06f, 0.1f), Is.EqualTo(0));
            Assert.That(trail.PendingDistance, Is.EqualTo(0.06f).Within(Tolerance));
            Assert.That(trail.Consume(0.06f, 0.1f), Is.EqualTo(1), "Two short steps must add up to one print.");
            Assert.That(trail.PendingDistance, Is.EqualTo(0.02f).Within(Tolerance));
        }

        [Test]
        public void TrackMarkTrail_BoundsPrintsPerStepAndReportsWhatItDropped()
        {
            TrackMarkTrail trail = new TrackMarkTrail();

            int prints = trail.Consume(50f, 0.1f);

            Assert.That(prints, Is.EqualTo(TrackMarkTrail.MaximumPrintsPerStep),
                "A paused frame must not spawn hundreds of prints that nobody would ever see.");
            Assert.That(trail.DroppedPrints, Is.GreaterThan(0));
        }

        [Test]
        public void SelectBandIndex_TakesTheFirstBandThatCoversTheValue()
        {
            float[] bounds = { 25f, 50f, 90f, 1000f };

            Assert.That(VfxProfileMath.SelectBandIndex(0f, bounds), Is.EqualTo(0));
            Assert.That(VfxProfileMath.SelectBandIndex(25f, bounds), Is.EqualTo(0));
            Assert.That(VfxProfileMath.SelectBandIndex(26f, bounds), Is.EqualTo(1));
            Assert.That(VfxProfileMath.SelectBandIndex(5000f, bounds), Is.EqualTo(3),
                "The last band is open-ended: a caliber above the table still has a look.");
            Assert.That(VfxProfileMath.SelectBandIndex(10f, null), Is.EqualTo(-1));
        }

        [Test]
        public void SelectDescendingBandIndex_TakesTheFirstBandTheValueStillReaches()
        {
            float[] thresholds = { 0.66f, 0.25f, 0f };

            Assert.That(VfxProfileMath.SelectDescendingBandIndex(1f, thresholds), Is.EqualTo(0));
            Assert.That(VfxProfileMath.SelectDescendingBandIndex(0.66f, thresholds), Is.EqualTo(0));
            Assert.That(VfxProfileMath.SelectDescendingBandIndex(0.5f, thresholds), Is.EqualTo(1));
            Assert.That(VfxProfileMath.SelectDescendingBandIndex(0.01f, thresholds), Is.EqualTo(2));
            Assert.That(VfxProfileMath.SelectDescendingBandIndex(0f, thresholds), Is.EqualTo(2));
        }

        [Test]
        public void HealthFraction_IsClampedAndSafeWithoutAMaximum()
        {
            Assert.That(VfxProfileMath.HealthFraction(50, 100), Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(VfxProfileMath.HealthFraction(-5, 100), Is.EqualTo(0f));
            Assert.That(VfxProfileMath.HealthFraction(500, 100), Is.EqualTo(1f));
            Assert.That(VfxProfileMath.HealthFraction(10, 0), Is.EqualTo(0f));
        }

        [Test]
        public void Intensity01_ReachesOneExactlyAtTheLimit()
        {
            Assert.That(VfxProfileMath.Intensity01(0f, 0f, 2f), Is.EqualTo(0f));
            Assert.That(VfxProfileMath.Intensity01(2f, 0f, 2f), Is.EqualTo(1f));
            Assert.That(VfxProfileMath.Intensity01(1f, 0f, 2f), Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(VfxProfileMath.Intensity01(4f, 0f, 2f), Is.EqualTo(1f));
        }

        [Test]
        public void ImpactScaleForRadius_GrowsWithTheShellAndStaysBounded()
        {
            float small = VfxProfileMath.ImpactScaleForRadius(20f / 1200f);
            float medium = VfxProfileMath.ImpactScaleForRadius(88f / 1200f);
            float huge = VfxProfileMath.ImpactScaleForRadius(400f / 1200f);

            Assert.That(small, Is.GreaterThanOrEqualTo(0.65f));
            Assert.That(medium, Is.GreaterThan(small));
            Assert.That(huge, Is.LessThanOrEqualTo(2.2f),
                "One enormous shell must not cover the whole screen with dust.");
            Assert.That(VfxProfileMath.ImpactScaleForRadius(0f), Is.EqualTo(0.65f).Within(Tolerance));
        }

        [Test]
        public void ScaleCount_NeverLosesAnEffectThatWasAskedFor()
        {
            Assert.That(VfxProfileMath.ScaleCount(0, 1f), Is.EqualTo(0));
            Assert.That(VfxProfileMath.ScaleCount(8, 1f), Is.EqualTo(8));
            Assert.That(VfxProfileMath.ScaleCount(8, 0.5f), Is.EqualTo(4));
            Assert.That(VfxProfileMath.ScaleCount(1, 0.1f), Is.EqualTo(1));
        }
    }
}
