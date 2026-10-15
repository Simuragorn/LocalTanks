using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Moves the tread pattern inside the track mask of a tank hull sprite.
    /// <para>
    /// The offsets come from the motion the machine actually achieved between two physics steps,
    /// never from the driving commands or from the motor's own intent. A tank pressed against a
    /// building, another tank or a wreck therefore keeps its tracks still, because its body does
    /// not move either. Each track is driven like one side of a differential: the hull's forward
    /// travel plus or minus the arc that its rotation sweeps at that track's distance from the
    /// centre line.
    /// </para>
    /// <para>
    /// The values travel to the overlay renderer in a <see cref="MaterialPropertyBlock"/>, so every
    /// machine shares one material asset and one shader without a material instance per tank.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class TankTrackAnimator : MonoBehaviour
    {
        private static readonly int TrackTravelId = Shader.PropertyToID("_TrackTravel");
        private static readonly int TrackParamsId = Shader.PropertyToID("_TrackParams");
        private static readonly int TrackStrengthId = Shader.PropertyToID("_TrackStrength");
        private static readonly int TrackMotionId = Shader.PropertyToID("_TrackMotion");
        private static readonly int TrackGeometryId = Shader.PropertyToID("_TrackGeometry");
        private static readonly int HullTexId = Shader.PropertyToID("_HullTex");
        private static readonly int HullRectId = Shader.PropertyToID("_HullRect");

        /// <summary>
        /// How many coarse sweeps span the mask. The sweep is the part of the pattern that survives a
        /// distant camera, so it is counted in mask heights rather than metres: every machine then
        /// shows the same number of sweeps no matter how long its hull is.
        /// </summary>
        public const float SweepRepeats = 6.5f;

        /// <summary>Rendered motion reaches full strength at this achieved belt speed.</summary>
        public const float FullMotionSpeedUnitsPerSecond = 0.55f;

        /// <summary>The distant pattern stays subordinate to individually resolved links.</summary>
        public const float DistantPatternGain = 0.95f;

        /// <summary>
        /// The metal relief that remains visible while a belt is stopped. Motion strengthens the same
        /// frozen pattern instead of creating highlights from nothing and erasing them at rest.
        /// </summary>
        public const float StaticPatternStrength = 0.42f;

        [SerializeField] private Rigidbody2D body;
        [SerializeField] private TankHealth health;
        [SerializeField] private SpriteRenderer overlay;

        /// <summary>
        /// The hull sprite the overlay sits on, plus the rectangle it occupies inside its own
        /// texture. The shader reads those pixels so the tread lightens what is already painted
        /// instead of laying a flat sheen over the sprite.
        /// </summary>
        [SerializeField] private SpriteRenderer hull;
        [SerializeField] private Vector4 hullTextureRect = new Vector4(0f, 0f, 1f, 1f);

        /// <summary>Half the distance between the two track centre lines, in Unity units.</summary>
        [SerializeField] private float halfGaugeUnits = 0.1f;

        /// <summary>Distance between two tread links, in Unity units.</summary>
        [SerializeField] private float treadPitchUnits = 0.2f;

        /// <summary>Height of the mask sprite in Unity units, which is the unit of the travel.</summary>
        [SerializeField] private float maskHeightUnits = 1f;

        /// <summary>+1 when the machine's front points towards increasing sprite V, otherwise -1.</summary>
        [SerializeField] private float forwardSign = -1f;

        /// <summary>Centres of the red and green mask bands, and their shared half-width, in mask UV.</summary>
        [SerializeField] private Vector3 trackGeometry = new Vector3(0.85f, 0.15f, 0.08f);

        /// <summary>x lightens a tread plate, y darkens the gap between two links.</summary>
        [SerializeField] private Vector2 strength = new Vector2(0.32f, 0.5f);

        private MaterialPropertyBlock properties;
        private Vector2 previousPosition;
        private float previousRotation;
        private float leftTravel;
        private float rightTravel;
        private float leftMotion;
        private float rightMotion;
        private bool running = true;

        public bool IsRunning => running;

        /// <summary>How far the left track has travelled, in mask heights.</summary>
        public float LeftTravel => leftTravel;

        /// <summary>How far the right track has travelled, in mask heights.</summary>
        public float RightTravel => rightTravel;

        /// <summary>Current visible contribution of each moving belt.</summary>
        public Vector2 MotionAmount => new Vector2(leftMotion, rightMotion);

        /// <summary>Measured band geometry supplied to the preview and shader.</summary>
        public Vector4 TrackGeometry => new Vector4(trackGeometry.x, trackGeometry.y, trackGeometry.z, 0f);

        /// <summary>
        /// How strongly a tread plate is lightened and the gap between two links is darkened, as
        /// multipliers around one. Deliberately low: the tread is a movement cue, not a conveyor
        /// belt, and the pattern is fine enough that a strong contrast would look painted on.
        /// </summary>
        public static Vector2 DefaultStrength => new Vector2(0.38f, 0.58f);

        /// <summary>
        /// Splits the hull's real motion into the distance each track travelled over the ground.
        /// A positive forward distance is travel towards the machine's front; a positive rotation
        /// is counter-clockwise, which advances the starboard track and holds the port one back.
        /// </summary>
        public static void SplitTrackDistances(
            float forwardUnits, float rotationDegrees, float halfGaugeUnits, out float leftUnits, out float rightUnits)
        {
            float arcUnits = rotationDegrees * Mathf.Deg2Rad * halfGaugeUnits;
            leftUnits = forwardUnits - arcUnits;
            rightUnits = forwardUnits + arcUnits;
        }

        /// <summary>
        /// Advances one track by the distance it travelled over the ground, measured in mask heights
        /// so the same value can drive a fine link pattern and a coarse sweep.
        /// <para>
        /// The travel is not wrapped: the shader repeats the pattern with <c>frac</c>, and wrapping
        /// independently of the pattern length would jump the phase whenever two machines use a
        /// different number of links.
        /// </para>
        /// </summary>
        public static float AdvanceTrackTravel(
            float travel, float trackDistanceUnits, float maskHeightUnits, float forwardSign)
        {
            if (maskHeightUnits <= 0f)
            {
                return travel;
            }

            return travel + forwardSign * trackDistanceUnits / maskHeightUnits;
        }

        /// <summary>How many link periods span the whole mask.</summary>
        public static float LinkRepeats(float maskHeightUnits, float treadPitchUnits)
        {
            if (treadPitchUnits <= 0f)
            {
                return 1f;
            }

            return Mathf.Max(1f, maskHeightUnits / treadPitchUnits);
        }

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (health == null)
            {
                health = GetComponent<TankHealth>();
            }

            properties = new MaterialPropertyBlock();
            previousPosition = CurrentPosition();
            previousRotation = CurrentRotation();
            if (health != null)
            {
                health.Destroyed += OnDestroyed;
            }

            ApplyProperties();
        }

        private void OnDestroy()
        {
            if (health != null)
            {
                health.Destroyed -= OnDestroyed;
            }
        }

        /// <summary>Wires the component from the generated prefab. Called by the asset builder.</summary>
        public void Configure(
            Rigidbody2D trackedBody,
            TankHealth tankHealth,
            SpriteRenderer trackOverlay,
            SpriteRenderer hullRenderer,
            Vector4 hullRect,
            float halfGauge,
            float treadPitch,
            float maskHeight,
            float forwardSignV,
            Vector2 effectStrength,
            float redBandCentreU = 0.85f,
            float greenBandCentreU = 0.15f,
            float bandHalfWidthU = 0.08f)
        {
            body = trackedBody != null ? trackedBody : GetComponent<Rigidbody2D>();
            health = tankHealth;
            overlay = trackOverlay;
            hull = hullRenderer;
            hullTextureRect = hullRect;
            halfGaugeUnits = Mathf.Max(0.0001f, halfGauge);
            treadPitchUnits = Mathf.Max(0.0001f, treadPitch);
            maskHeightUnits = Mathf.Max(0.0001f, maskHeight);
            forwardSign = forwardSignV < 0f ? -1f : 1f;
            strength = effectStrength;
            trackGeometry = new Vector3(
                Mathf.Clamp01(redBandCentreU),
                Mathf.Clamp01(greenBandCentreU),
                Mathf.Max(0.001f, bandHalfWidthU));
            properties = new MaterialPropertyBlock();
            previousPosition = CurrentPosition();
            previousRotation = CurrentRotation();
            ApplyProperties();
        }

        private void FixedUpdate()
        {
            using var performanceScope = BattlePerformanceProbe.Measure(BattlePerformanceSubsystem.TrackAnimation);
            Vector2 position = CurrentPosition();
            float rotation = CurrentRotation();
            Vector2 delta = position - previousPosition;
            float deltaRotation = Mathf.DeltaAngle(previousRotation, rotation);
            previousPosition = position;
            previousRotation = rotation;

            if (!running)
            {
                return;
            }

            float forwardUnits = Vector2.Dot(delta, ForwardDirection(rotation));
            SplitTrackDistances(forwardUnits, deltaRotation, halfGaugeUnits, out float leftUnits, out float rightUnits);
            leftTravel = AdvanceTrackTravel(leftTravel, leftUnits, maskHeightUnits, forwardSign);
            rightTravel = AdvanceTrackTravel(rightTravel, rightUnits, maskHeightUnits, forwardSign);
            float seconds = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            float leftTarget = Mathf.Clamp01(Mathf.Abs(leftUnits) / seconds / FullMotionSpeedUnitsPerSecond);
            float rightTarget = Mathf.Clamp01(Mathf.Abs(rightUnits) / seconds / FullMotionSpeedUnitsPerSecond);
            leftMotion = ApproachMotion(leftMotion, leftTarget, seconds);
            rightMotion = ApproachMotion(rightMotion, rightTarget, seconds);
            ApplyProperties();
        }

        private void OnDestroyed(TankHealth destroyedHealth)
        {
            running = false;
            leftMotion = 0f;
            rightMotion = 0f;
            ApplyProperties();
        }

        private static float ApproachMotion(float current, float target, float seconds)
        {
            // Start decisively, then settle without one-frame flashes; stopping is slightly softer so a
            // low frame rate cannot make the belt blink between physics samples.
            float response = target > current ? 9f : 5f;
            return Mathf.MoveTowards(current, target, response * seconds);
        }

        private void ApplyProperties()
        {
            if (overlay == null)
            {
                return;
            }

            if (properties == null)
            {
                properties = new MaterialPropertyBlock();
            }

            properties.SetVector(
                TrackParamsId,
                new Vector4(LinkRepeats(maskHeightUnits, treadPitchUnits), SweepRepeats, 0f, 0f));
            properties.SetVector(TrackTravelId, new Vector4(leftTravel, rightTravel, 0f, 0f));
            properties.SetVector(TrackStrengthId, new Vector4(strength.x, strength.y, 0f, 0f));
            properties.SetVector(
                TrackMotionId,
                new Vector4(leftMotion, rightMotion, DistantPatternGain, StaticPatternStrength));
            properties.SetVector(TrackGeometryId, TrackGeometry);
            properties.SetVector(HullRectId, hullTextureRect);

            // The hull texture is bound per instance rather than owned by the material, so one
            // shared material still serves machines with different artwork.
            Texture hullTexture = hull != null && hull.sprite != null ? hull.sprite.texture : null;
            if (hullTexture != null)
            {
                properties.SetTexture(HullTexId, hullTexture);
            }

            overlay.SetPropertyBlock(properties);
        }

        private Vector2 CurrentPosition()
        {
            return body != null ? body.position : (Vector2)transform.position;
        }

        private float CurrentRotation()
        {
            return body != null ? body.rotation : transform.eulerAngles.z;
        }

        private static Vector2 ForwardDirection(float rotationDegrees)
        {
            float radians = rotationDegrees * Mathf.Deg2Rad;
            return new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
        }
    }
}
