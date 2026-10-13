using System.Collections;
using LocalTanks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The tread overlay follows the motion a machine actually achieved, so the driving commands are
    /// deliberately not part of it: a tank pressed against a wall must keep its tracks still, while a
    /// tank that moves or turns must advance both tracks by the distance each one travelled.
    /// </summary>
    public sealed class TankTrackAnimatorPlayModeTests
    {
        private const float TreadPitchUnits = 0.05f;
        private const float HalfGaugeUnits = 0.2f;
        private const float MaskHeightUnits = 1f;
        private const float ForwardSign = -1f;
        private const float StepDistance = 0.02f;
        private const int Steps = 30;

        [UnityTest]
        public IEnumerator DrivingForward_AdvancesBothTracksByTheDistanceTravelled()
        {
            TrackRig rig = TrackRig.Create(new Vector2(400f, 400f));
            try
            {
                yield return rig.Drive(Vector2.up * StepDistance, Steps, 0f);

                float travelled = rig.Body.position.y - 400f;
                Assert.That(travelled, Is.GreaterThan(StepDistance * Steps * 0.8f),
                    "The control tank must actually move, otherwise the measurement proves nothing.");

                float expected = TankTrackAnimator.AdvanceTrackTravel(0f, travelled, MaskHeightUnits, ForwardSign);
                Assert.That(rig.Animator.LeftTravel, Is.EqualTo(expected).Within(0.05f),
                    "The left track must follow the distance the hull travelled.");
                Assert.That(rig.Animator.RightTravel, Is.EqualTo(expected).Within(0.05f),
                    "Driving straight must advance both tracks equally.");
            }
            finally
            {
                rig.Destroy();
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator TurningOnTheSpot_AdvancesTheTracksInOppositeDirections()
        {
            TrackRig rig = TrackRig.Create(new Vector2(400f, 400f));
            try
            {
                yield return rig.Drive(Vector2.zero, Steps, 3f);

                float turned = rig.Body.rotation;
                Assert.That(Mathf.Abs(turned), Is.GreaterThan(5f),
                    "The control tank must actually turn, otherwise the measurement proves nothing.");

                float forward = rig.Body.position.y - 400f;
                TankTrackAnimator.SplitTrackDistances(
                    forward, turned, HalfGaugeUnits, out float leftUnits, out float rightUnits);

                Assert.That(leftUnits, Is.LessThan(0f),
                    "A counter-clockwise turn pulls the port track backwards over the ground.");
                Assert.That(rightUnits, Is.GreaterThan(0f),
                    "A counter-clockwise turn pushes the starboard track forwards over the ground.");

                float expectedLeft = TankTrackAnimator.AdvanceTrackTravel(
                    0f, leftUnits, MaskHeightUnits, ForwardSign);
                float expectedRight = TankTrackAnimator.AdvanceTrackTravel(
                    0f, rightUnits, MaskHeightUnits, ForwardSign);

                Assert.That(rig.Animator.LeftTravel, Is.EqualTo(expectedLeft).Within(0.05f),
                    "The port track must follow its own distance over the ground.");
                Assert.That(rig.Animator.RightTravel, Is.EqualTo(expectedRight).Within(0.05f),
                    "The starboard track must follow its own distance over the ground.");
                Assert.That(rig.Animator.LeftTravel, Is.EqualTo(rig.Animator.RightTravel * -1f).Within(0.05f),
                    "A turn on the spot must move the two tracks by equal and opposite amounts.");
            }
            finally
            {
                rig.Destroy();
            }

            yield return null;
        }

        /// <summary>
        /// This is the reason the component measures motion instead of reading the motor: a tank that
        /// is blocked still receives the same drive command every step, and reading the command would
        /// make its tracks run on the spot forever.
        /// </summary>
        [UnityTest]
        public IEnumerator ATankBlockedByAWall_KeepsItsTracksStill()
        {
            TrackRig rig = TrackRig.Create(new Vector2(400f, 400f));
            GameObject wall = new GameObject("TrackAnimatorTestWall");
            try
            {
                wall.transform.position = new Vector2(400f, 400.6f);
                BoxCollider2D wallCollider = wall.AddComponent<BoxCollider2D>();
                wallCollider.size = new Vector2(4f, 0.4f);

                // Close the gap first, then keep pushing: the second half is the measurement, because
                // driving up to a wall is real movement and the machine must be allowed to make it.
                yield return rig.Drive(Vector2.up * StepDistance, Steps, 0f);
                float settledY = rig.Body.position.y;
                float settledLeft = rig.Animator.LeftTravel;
                float settledRight = rig.Animator.RightTravel;

                yield return rig.Drive(Vector2.up * StepDistance, Steps, 0f);

                Assert.That(rig.Body.position.y - settledY, Is.LessThan(0.01f),
                    "The wall must hold the tank, otherwise the measurement proves nothing.");
                Assert.That(rig.Animator.LeftTravel - settledLeft, Is.LessThan(0.01f),
                    "A blocked tank must not move its left track.");
                Assert.That(rig.Animator.RightTravel - settledRight, Is.LessThan(0.01f),
                    "A blocked tank must not move its right track.");
            }
            finally
            {
                Object.Destroy(wall);
                rig.Destroy();
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ADestroyedTank_StopsMovingAndSwitchesTheOverlayOff()
        {
            TrackRig rig = TrackRig.Create(new Vector2(400f, 400f));
            try
            {
                yield return rig.Drive(Vector2.up * StepDistance, Steps, 0f);
                Assert.That(rig.Animator.IsRunning, Is.True, "A live tank must animate its tracks.");

                rig.Health.ApplyDamage(rig.Health.Definition.maxHitPoints);
                yield return new WaitForFixedUpdate();
                yield return rig.Drive(Vector2.up * StepDistance, 5, 0f);

                Assert.That(rig.Health.IsDestroyed, Is.True, "Full damage must destroy the tank.");
                Assert.That(rig.Animator.IsRunning, Is.False, "A wreck must stop animating its tracks.");

                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                rig.Overlay.GetPropertyBlock(properties);
                Vector4 strength = properties.GetVector("_TrackStrength");
                Assert.That(strength.x, Is.Zero.Within(0.0001f),
                    "A wreck must switch the highlight off instead of leaving it glowing.");
            }
            finally
            {
                rig.Destroy();
            }

            yield return null;
        }

        private sealed class TrackRig
        {
            public GameObject Root { get; private set; }
            public Rigidbody2D Body { get; private set; }
            public TankTrackAnimator Animator { get; private set; }
            public SpriteRenderer Overlay { get; private set; }
            public TankHealth Health { get; private set; }

            public static TrackRig Create(Vector2 position)
            {
                GameObject root = new GameObject("TrackAnimatorTestTank");
                root.transform.position = position;
                Rigidbody2D body = root.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.bodyType = RigidbodyType2D.Dynamic;
                root.AddComponent<BoxCollider2D>().size = new Vector2(0.6f, 0.6f);

                GameObject overlay = new GameObject("TrackOverlay");
                overlay.transform.SetParent(root.transform, false);
                SpriteRenderer renderer = overlay.AddComponent<SpriteRenderer>();

                // The hull sprite is what the shader reads for its metal response; a rig without one
                // must still animate, which is what this test checks.
                GameObject hull = new GameObject("HullVisual");
                hull.transform.SetParent(root.transform, false);
                SpriteRenderer hullRenderer = hull.AddComponent<SpriteRenderer>();

                // TankHealth only needs a definition for the destroyed path; the other tests drive the
                // animator without one, which is exactly how a prefab without audio or armor behaves.
                TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
                definition.maxHitPoints = 100;
                TankHealth health = root.AddComponent<TankHealth>();
                health.Configure(definition);

                TankTrackAnimator animator = root.AddComponent<TankTrackAnimator>();
                animator.Configure(
                    body,
                    health,
                    renderer,
                    hullRenderer,
                    new Vector4(0f, 0f, 1f, 1f),
                    HalfGaugeUnits,
                    TreadPitchUnits,
                    MaskHeightUnits,
                    ForwardSign,
                    TankTrackAnimator.DefaultStrength);

                return new TrackRig
                {
                    Root = root,
                    Body = body,
                    Animator = animator,
                    Overlay = renderer,
                    Health = health
                };
            }

            /// <summary>Drives the body the way TankMotor does: a queued move every physics step.</summary>
            public IEnumerator Drive(Vector2 step, int steps, float degreesPerStep)
            {
                for (int index = 0; index < steps; index++)
                {
                    Body.MovePosition(Body.position + step);
                    if (!Mathf.Approximately(degreesPerStep, 0f))
                    {
                        Body.MoveRotation(Body.rotation + degreesPerStep);
                    }

                    yield return new WaitForFixedUpdate();
                }
            }

            public void Destroy()
            {
                if (Root != null)
                {
                    Object.Destroy(Root);
                }
            }
        }
    }
}
