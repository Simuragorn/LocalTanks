using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LocalTanks
{
    /// <summary>
    /// Every visual effect that belongs to one machine: the print of each track, the dust or spray it
    /// raises, the exhaust, the smoke and fire of damage, the muzzle flash and the burst of destruction.
    /// </summary>
    /// <remarks>
    /// The rig resolves nothing about appearance itself. It reads its own geometry (the hull collider and
    /// the anchors the prefab builder measured), its own state (speed, turn rate, remaining health) and the
    /// surface it stands on, and asks <see cref="CombatVfxSystem"/> for the band that matches. A machine
    /// therefore needs no visual profile of its own, and a new tank looks right the moment its caliber,
    /// mass and class exist.
    /// <para>
    /// All loop effects are children of the machine; every print goes to the world-space container of the
    /// effect system, because a print belongs to the ground. Nothing is created per frame: the loops are
    /// instantiated once when the machine appears and the prints come from a fixed ring buffer.
    /// </para>
    /// <para>
    /// Every generated effect is a hierarchy, not a single component: the muzzle flash is a core and a
    /// star, and the fire is a flame and embers. The rig therefore works with the systems it finds in a
    /// prefab rather than with one component, which is the difference between an effect that appears and
    /// one that is silently never attached.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(TankMotor))]
    public sealed class TankVfxRig : MonoBehaviour
    {
        [SerializeField] private TankDefinition config;
        [SerializeField] private TankMotor motor;
        [SerializeField] private TankHealth health;
        [SerializeField] private WeaponController weapon;
        [SerializeField] private TerrainMotorModifier terrain;
        [SerializeField] private TankVisibilityPresenter visibility;

        /// <summary>
        /// Distance between the centre lines of the two tracks, in world units, measured from the 3D model
        /// by the sprite pipeline and carried into the prefab. Zero on a prefab built before the
        /// measurement existed, and then the hull width is used instead.
        /// </summary>
        [SerializeField, Min(0f)] private float measuredTrackSeparation;

        /// <summary>Name of the child transform that marks the machine's exhaust outlet.</summary>
        public const string ExhaustAnchorName = "Exhaust";

        private static readonly Color DustFallback = new Color(0.62f, 0.56f, 0.42f, 1f);

        /// <summary>
        /// Smallest change in a rate worth a native call. The generated prefabs start emitting on their
        /// own, so the rig silences every loop the moment it attaches it and then only touches a rate when
        /// the machine actually changes what it is doing.
        /// </summary>
        private const float EmissionEpsilon = 0.05f;

        private TrackMarkTrail leftTrail;
        private TrackMarkTrail rightTrail;
        private TankTrackMarkEmitter marks;
        private ParticleSystem[] leftDust;
        private ParticleSystem[] rightDust;
        private ParticleSystem[] leftSpray;
        private ParticleSystem[] rightSpray;
        private ParticleSystem[] damageSmoke;
        private ParticleSystem[] damageFire;

        /// <summary>
        /// The lights the fire prefab carries. A light is not a particle: switching emission off leaves
        /// it shining, and a machine that is not burning must not light the map, so it is switched with
        /// the fire itself.
        /// </summary>
        private Light2D[] fireLights = System.Array.Empty<Light2D>();
        private ParticleSystem[] exhaust;
        private ParticleSystem[] barrelSmoke;
        private Transform exhaustAnchor;
        private Sprite markSprite;

        private Vector2 hullSize = new Vector2(0.6f, 1.0f);
        private float halfTrackOffset = 0.18f;
        private bool built;
        private bool destroyed;
        private float appliedDustRate = -1f;
        private float appliedSprayRate = -1f;
        private float appliedExhaustRate = -1f;
        private float barrelSmokeSecondsLeft;
        private float exhaustPuffSecondsLeft;
        private bool wasMoving;
        private TerrainKind appliedSurface = TerrainKind.Blocked;
        private DamageVfxProfile appliedDamage;

        public bool IsBuilt => built;
        public bool HasVisuals => built && (marks != null || leftDust != null || damageSmoke != null);

        /// <summary>Systems that were actually attached; a diagnostic for the whole loop chain.</summary>
        public int AttachedLoopSystems =>
            Count(leftDust) + Count(rightDust) + Count(leftSpray) + Count(rightSpray) +
            Count(damageSmoke) + Count(damageFire) + Count(exhaust) + Count(barrelSmoke);

        public int MuzzleFlashCount { get; private set; }
        public int DestructionBurstCount { get; private set; }
        public int EmittedMarks => marks != null ? marks.EmittedCount : 0;
        public float BarrelSmokeSecondsLeft => barrelSmokeSecondsLeft;
        public TerrainKind CurrentSurface { get; private set; } = TerrainKind.Grass;
        public DamageVfxProfile CurrentDamageProfile => appliedDamage;
        public Vector2 HullSize => hullSize;

        /// <summary>Distance between the track centre lines used for prints and dust.</summary>
        public float TrackSeparation { get; private set; }

        private void Awake()
        {
            motor ??= GetComponent<TankMotor>();
            health ??= GetComponent<TankHealth>();
            weapon ??= GetComponent<WeaponController>();
            terrain ??= GetComponent<TerrainMotorModifier>();
            visibility ??= GetComponent<TankVisibilityPresenter>();
            exhaustAnchor ??= transform.Find(ExhaustAnchorName);
            MeasureHull();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            BuildEffects();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(
            TankDefinition newConfig,
            TankMotor newMotor,
            TankHealth newHealth = null,
            WeaponController newWeapon = null,
            TerrainMotorModifier newTerrain = null,
            TankVisibilityPresenter newVisibility = null,
            float trackSeparation = 0f,
            Transform newExhaustAnchor = null)
        {
            config = newConfig;
            motor = newMotor != null ? newMotor : GetComponent<TankMotor>();
            health = newHealth != null ? newHealth : GetComponent<TankHealth>();
            weapon = newWeapon != null ? newWeapon : GetComponent<WeaponController>();
            terrain = newTerrain != null ? newTerrain : GetComponent<TerrainMotorModifier>();
            visibility = newVisibility != null ? newVisibility : GetComponent<TankVisibilityPresenter>();
            if (trackSeparation > 0f)
            {
                measuredTrackSeparation = trackSeparation;
            }

            if (newExhaustAnchor != null)
            {
                exhaustAnchor = newExhaustAnchor;
            }

            MeasureHull();
        }

        private void Update()
        {
            if (config == null || motor == null)
            {
                return;
            }

            if (!built)
            {
                BuildEffects();
            }

            float deltaTime = Time.deltaTime;
            if (destroyed || !IsVisibleToPlayer())
            {
                SuppressMovement();
            }
            else
            {
                UpdateMovement(deltaTime);
            }

            UpdateDamage();
            UpdateBarrelSmoke(deltaTime);

            // Prints belong to the ground and keep fading whether or not their author is still on screen.
            marks?.Tick(deltaTime);
        }

        /// <summary>
        /// Instantiates the persistent part of the effect set once. A missing generated asset leaves the
        /// corresponding effect null and the machine simply looks the way it did before the effects
        /// existed; nothing in the simulation depends on this succeeding.
        /// </summary>
        private void BuildEffects()
        {
            if (built)
            {
                return;
            }

            built = true;
            CombatVfxSystem system = CombatVfxSystem.Ensure();
            ICombatVfxAssetProvider provider = system != null ? system.AssetProvider : null;
            CombatVfxCatalog catalog = system != null ? system.Catalog : null;
            if (provider == null || catalog == null)
            {
                return;
            }

            MovementVfxProfile movement = catalog.ResolveMovement(config.massTonnes);
            if (movement != null)
            {
                markSprite = provider.LoadMarkSprite(CombatVfxEffects.MarkVariantFor(movement.id));
                marks = gameObject.AddComponent<TankTrackMarkEmitter>();
                marks.Configure(markSprite, movement.markLimit);
            }

            leftDust = AttachLoop(provider, CombatVfxEffects.TrackDust, "VfxDustLeft", -halfTrackOffset, RearOffset());
            rightDust = AttachLoop(provider, CombatVfxEffects.TrackDust, "VfxDustRight", halfTrackOffset, RearOffset());
            leftSpray = AttachLoop(provider, CombatVfxEffects.WaterSpray, "VfxSprayLeft", -halfTrackOffset, RearOffset());
            rightSpray = AttachLoop(provider, CombatVfxEffects.WaterSpray, "VfxSprayRight", halfTrackOffset, RearOffset());
            damageSmoke = AttachLoop(provider, CombatVfxEffects.DamageSmoke, "VfxDamageSmoke", hullSize.x * 0.12f, -hullSize.y * 0.26f);
            damageFire = AttachLoop(provider, CombatVfxEffects.DamageFire, "VfxDamageFire", -hullSize.x * 0.18f, -hullSize.y * 0.2f);
            fireLights = CollectLights(damageFire);
            exhaust = AttachExhaust(provider);
            barrelSmoke = AttachBarrelSmoke(provider);

            // The generated loops start emitting on their own so that a prefab looked at in isolation
            // behaves like the machine does. Until the rig has read the machine's state, every one of them
            // is silenced, or a parked tank would puff at the rate the prefab was built with.
            SilenceRunningEffects();
            appliedDustRate = 0f;
            appliedSprayRate = 0f;
            appliedExhaustRate = 0f;
        }

        /// <summary>
        /// Attaches the exhaust at the machine's own outlet. The outlet is a child transform built into the
        /// prefab from the canonical sprite, because where a tank breathes out is a fact of its model and
        /// not of its mass or class; the hull's rear quarter is only the fallback for a prefab without one.
        /// </summary>
        private ParticleSystem[] AttachExhaust(ICombatVfxAssetProvider provider)
        {
            if (exhaustAnchor != null)
            {
                return AttachLoopAt(provider, CombatVfxEffects.ExhaustSmoke, "VfxExhaust", exhaustAnchor);
            }

            return AttachLoop(provider, CombatVfxEffects.ExhaustSmoke, "VfxExhaust", hullSize.x * 0.3f, -hullSize.y * 0.4f);
        }

        /// <summary>
        /// A short puff of smoke from the barrel after a shot. It lives at the muzzle, where the propellant
        /// actually leaves the gun, and the rig only opens its emission for a moment after each shot.
        /// </summary>
        private ParticleSystem[] AttachBarrelSmoke(ICombatVfxAssetProvider provider)
        {
            Transform muzzle = weapon != null ? weapon.Muzzle : null;
            if (muzzle == null)
            {
                return null;
            }

            return AttachLoopAt(provider, CombatVfxEffects.BarrelSmoke, "VfxBarrelSmoke", muzzle);
        }

        private ParticleSystem[] AttachLoop(
            ICombatVfxAssetProvider provider,
            string effectId,
            string instanceName,
            float localX,
            float localY)
        {
            return AttachLoopAt(provider, effectId, instanceName, transform, new Vector3(localX, localY, 0f));
        }

        private ParticleSystem[] AttachLoopAt(
            ICombatVfxAssetProvider provider,
            string effectId,
            string instanceName,
            Transform anchor)
        {
            return AttachLoopAt(provider, effectId, instanceName, anchor, Vector3.zero);
        }

        private ParticleSystem[] AttachLoopAt(
            ICombatVfxAssetProvider provider,
            string effectId,
            string instanceName,
            Transform parent,
            Vector3 localPosition)
        {
            GameObject prefab = provider.LoadPrefab(effectId);
            if (prefab == null)
            {
                return null;
            }

            GameObject instance = Instantiate(prefab, parent);
            instance.name = instanceName;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;

            // A generated effect is a hierarchy: the muzzle flash is a core and a star, the fire is a flame
            // and embers. Looking for one component on the root found nothing at all and silently switched
            // every persistent effect off, which is exactly what a machine with no dust, no smoke and no
            // fire looks like.
            ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0)
            {
                Destroy(instance);
                return null;
            }

            foreach (ParticleSystem system in systems)
            {
                // Clears whatever the prefab emitted on the frame it was created, then starts it again: a
                // stopped particle system keeps its rate but never emits, so the rig leaves every loop
                // playing and controls it through the emission module instead.
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Play(true);
            }

            return systems;
        }

        private void UpdateMovement(float deltaTime)
        {
            CombatVfxSystem system = CombatVfxSystem.Current;
            CombatVfxCatalog catalog = system != null ? system.Catalog : null;
            MovementVfxProfile profile = catalog != null ? catalog.ResolveMovement(config.massTonnes) : null;
            if (profile == null)
            {
                return;
            }

            float speed = motor.CurrentSpeed;
            float turnRate = motor.CurrentTurnRate;
            float speedLimit = speed < 0f
                ? Mathf.Max(0.01f, config.maxReverseSpeed)
                : Mathf.Max(0.01f, config.maxForwardSpeed);
            float load01 = Mathf.Clamp01(Mathf.Abs(speed) / speedLimit);
            float turn01 = Mathf.Clamp01(Mathf.Abs(turnRate) / Mathf.Max(0.01f, config.hullTurnSpeed));
            float motion01 = Mathf.Max(load01, turn01 * 0.7f);

            TerrainKind surface = terrain != null ? terrain.CurrentTerrain : TerrainKind.Grass;
            CurrentSurface = surface;
            SurfaceVfxProfile surfaceProfile = catalog.ResolveSurface(surface);
            bool splash = surfaceProfile != null && surfaceProfile.splash;
            float dustMultiplier = surfaceProfile != null ? Mathf.Max(0f, surfaceProfile.dustRateMultiplier) : 1f;
            float markMultiplier = surfaceProfile != null ? Mathf.Max(0f, surfaceProfile.markOpacityMultiplier) : 1f;

            if (surface != appliedSurface)
            {
                appliedSurface = surface;
                Color dustTint = surfaceProfile != null ? surfaceProfile.dustTint.ToColor() : DustFallback;
                dustTint.a = profile.dustOpacity;
                ApplyStartColor(leftDust, dustTint);
                ApplyStartColor(rightDust, dustTint);
                ApplyStartSize(leftDust, profile.dustSizeFraction * hullSize.x);
                ApplyStartSize(rightDust, profile.dustSizeFraction * hullSize.x);
                ApplyVelocityScale(leftDust, profile.dustSpeed);
                ApplyVelocityScale(rightDust, profile.dustSpeed);
            }

            float dustRate = splash ? 0f : profile.dustRate * motion01 * dustMultiplier;
            if (RateChanged(dustRate, appliedDustRate))
            {
                appliedDustRate = dustRate;
                SetEmission(leftDust, dustRate);
                SetEmission(rightDust, dustRate);
            }

            float sprayRate = splash ? profile.sprayRate * motion01 : 0f;
            if (RateChanged(sprayRate, appliedSprayRate))
            {
                appliedSprayRate = sprayRate;
                SetEmission(leftSpray, sprayRate);
                SetEmission(rightSpray, sprayRate);
            }

            float exhaustRate = Mathf.Lerp(profile.exhaustIdleRate, profile.exhaustLoadedRate, load01);

            // A machine that was standing still and is asked to move smokes hardest for a moment: the
            // engine is cold, the fuel is rich and the plume is the one a player actually notices. The
            // transition is read from the achieved speed, so a machine held against a wall stays quiet.
            bool moving = Mathf.Abs(speed) > 0.05f;
            if (moving && !wasMoving)
            {
                exhaustPuffSecondsLeft = Mathf.Max(0f, profile.exhaustStartPuffSeconds);
            }

            wasMoving = moving;
            if (exhaustPuffSecondsLeft > 0f)
            {
                exhaustPuffSecondsLeft -= deltaTime;
                exhaustRate = profile.exhaustLoadedRate * Mathf.Max(1f, profile.exhaustStartPuffMultiplier);
            }

            if (RateChanged(exhaustRate, appliedExhaustRate))
            {
                appliedExhaustRate = exhaustRate;
                SetEmission(exhaust, exhaustRate);
            }

            EmitPrints(profile, surfaceProfile, markMultiplier, deltaTime);
        }

        /// <summary>
        /// Places the prints this step earned for each track. Each track has its own accumulator and its
        /// own direction, which is what makes a turn in place leave two arcs instead of one twisted ribbon.
        /// </summary>
        private void EmitPrints(MovementVfxProfile profile, SurfaceVfxProfile surface, float surfaceMarkMultiplier, float deltaTime)
        {
            if (marks == null || profile.markOpacity <= 0f || surfaceMarkMultiplier <= 0f)
            {
                return;
            }

            float separation = TrackSeparation > 0f ? TrackSeparation : halfTrackOffset * 2f;
            float spacing = profile.markSpacingFraction * hullSize.y;
            float width = profile.markWidthFraction * separation;
            float length = profile.markLengthFraction * hullSize.y;
            // The colour comes from the surface profile: a print is a dark groove, and how dark it is
            // depends on what the tracks are pressing into.
            Color color = surface != null
                ? surface.markTint.ToColor()
                : new Color(0.16f, 0.15f, 0.13f, 1f);
            color.a = Mathf.Clamp01(profile.markOpacity * surfaceMarkMultiplier);

            Vector2 forward = transform.up;
            Vector2 right = transform.right;
            float speed = motor.CurrentSpeed;
            float turnRate = motor.CurrentTurnRate;
            float half = separation * 0.5f;
            EmitTrack(ref leftTrail, -half, forward, right, speed, turnRate, deltaTime, spacing, width, length, color, profile.markFadeSeconds);
            EmitTrack(ref rightTrail, half, forward, right, speed, turnRate, deltaTime, spacing, width, length, color, profile.markFadeSeconds);
        }

        private void EmitTrack(
            ref TrackMarkTrail trail,
            float lateralOffset,
            Vector2 forward,
            Vector2 right,
            float speed,
            float turnRate,
            float deltaTime,
            float spacing,
            float width,
            float length,
            Color color,
            float fadeSeconds)
        {
            Vector2 velocity = TrackMarkMath.TrackVelocity(forward, right, speed, turnRate, lateralOffset);
            int prints = trail.Consume(velocity.magnitude * deltaTime, spacing);
            if (prints <= 0)
            {
                return;
            }

            Vector3 position = transform.TransformPoint(new Vector3(lateralOffset, 0f, 0f));
            Quaternion rotation = CombatVfxSystem.PlaneRotation(velocity);
            for (int index = 0; index < prints; index++)
            {
                marks.Emit(position, rotation, width, length, color, fadeSeconds);
            }
        }

        /// <summary>Keeps the barrel smoking for a moment after each shot, then closes it again.</summary>
        private void UpdateBarrelSmoke(float deltaTime)
        {
            if (barrelSmoke == null || barrelSmokeSecondsLeft <= 0f)
            {
                return;
            }

            barrelSmokeSecondsLeft -= deltaTime;
            if (barrelSmokeSecondsLeft <= 0f)
            {
                barrelSmokeSecondsLeft = 0f;
                SetEmission(barrelSmoke, 0f);
            }
        }

        private void SuppressMovement()
        {
            if (appliedDustRate > 0f)
            {
                appliedDustRate = 0f;
                SetEmission(leftDust, 0f);
                SetEmission(rightDust, 0f);
            }

            if (appliedSprayRate > 0f)
            {
                appliedSprayRate = 0f;
                SetEmission(leftSpray, 0f);
                SetEmission(rightSpray, 0f);
            }

            if (appliedExhaustRate > 0f)
            {
                appliedExhaustRate = 0f;
                SetEmission(exhaust, 0f);
            }

            if (barrelSmokeSecondsLeft > 0f)
            {
                barrelSmokeSecondsLeft = 0f;
                SetEmission(barrelSmoke, 0f);
            }
        }

        /// <summary>Turns every generated loop off until the machine's own state says otherwise.</summary>
        private void SilenceRunningEffects()
        {
            SetEmission(leftDust, 0f);
            SetEmission(rightDust, 0f);
            SetEmission(leftSpray, 0f);
            SetEmission(rightSpray, 0f);
            SetEmission(exhaust, 0f);
            SetEmission(damageSmoke, 0f);
            SetEmissionEnabled(damageFire, false);
            SetLightsEnabled(fireLights, false);
            SetEmission(barrelSmoke, 0f);
        }

        /// <summary>
        /// Whether a rate differs enough from the one already applied to be worth a native call. The
        /// threshold is well below what a particle system can show and well above the float noise of a
        /// machine driving at a steady speed.
        /// </summary>
        private static bool RateChanged(float rate, float appliedRate)
        {
            return Mathf.Abs(rate - appliedRate) > EmissionEpsilon;
        }

        /// <summary>
        /// Smoke and fire follow the remaining health. The band is resolved when it changes rather than
        /// every frame: the profile table is data, and comparing its entries keeps the native particle
        /// calls out of the steady state.
        /// </summary>
        private void UpdateDamage()
        {
            CombatVfxSystem system = CombatVfxSystem.Current;
            CombatVfxCatalog catalog = system != null ? system.Catalog : null;
            if (catalog == null)
            {
                return;
            }

            bool isDestroyed = destroyed || (health != null && health.IsDestroyed);
            float fraction = health != null
                ? VfxProfileMath.HealthFraction(health.CurrentHitPoints, health.MaximumHitPoints)
                : 1f;
            DamageVfxProfile profile = catalog.ResolveDamage(fraction, isDestroyed);
            if (ReferenceEquals(profile, appliedDamage))
            {
                return;
            }

            appliedDamage = profile;
            ApplyDamageProfile(profile);
        }

        private void ApplyDamageProfile(DamageVfxProfile profile)
        {
            if (profile == null)
            {
                SetEmission(damageSmoke, 0f);
                SetEmission(damageFire, 0f);
                return;
            }

            if (damageSmoke != null)
            {
                Color tint = new Color(0.16f, 0.15f, 0.14f, Mathf.Clamp01(profile.smokeOpacity));
                foreach (ParticleSystem system in damageSmoke)
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startColor = tint;
                    main.startSize = profile.smokeSizeFraction * hullSize.x;
                }

                ApplyVelocityScale(damageSmoke, profile.smokeRise);
                SetEmission(damageSmoke, profile.smokeRate);
            }

            // Burning is decided by the band, and only the wreck band burns: a machine on its last legs
            // smokes, and fire is what tells the player that this machine is finished. The layered rates
            // belong to the generated prefab, so the rig only opens and closes the fire.
            SetEmissionEnabled(damageFire, profile.fire);
            SetLightsEnabled(fireLights, profile.fire);
        }

        private void Subscribe()
        {
            if (weapon != null)
            {
                weapon.Fired -= OnFired;
                weapon.Fired += OnFired;
            }

            if (health != null)
            {
                health.Destroyed -= OnDestroyed;
                health.Destroyed += OnDestroyed;
            }
        }

        private void Unsubscribe()
        {
            if (weapon != null)
            {
                weapon.Fired -= OnFired;
            }

            if (health != null)
            {
                health.Destroyed -= OnDestroyed;
            }
        }

        private void OnFired(WeaponController source)
        {
            CombatVfxSystem system = CombatVfxSystem.Current;
            CombatVfxCatalog catalog = system != null ? system.Catalog : null;
            Transform muzzle = source != null ? source.Muzzle : null;
            if (catalog == null || muzzle == null)
            {
                return;
            }

            WeaponVfxProfile profile = catalog.ResolveWeapon(CaliberMm());
            if (profile == null)
            {
                return;
            }

            Quaternion rotation = CombatVfxSystem.PlaneRotation(muzzle.up);
            // No two shots are identical: the flash is rotated a little and its size varies, which is what
            // keeps a burst of fire from looking like one stamped sprite repeated.
            float spread = Random.Range(-16f, 16f);
            float variation = Random.Range(0.85f, 1.2f);
            rotation *= Quaternion.Euler(0f, 0f, spread);
            system.Play(VfxEmission.At(
                CombatVfxEffects.MuzzleFlash,
                muzzle.position,
                rotation,
                profile.flashScale * variation));
            if (profile.smokeBurst > 0)
            {
                system.Play(VfxEmission.At(
                        CombatVfxEffects.MuzzleSmoke,
                        muzzle.position,
                        rotation,
                        profile.smokeScale * Random.Range(0.9f, 1.1f))
                    .WithCount(profile.smokeBurst));
            }

            if (profile.barrelSmokeSeconds > 0f)
            {
                barrelSmokeSecondsLeft = profile.barrelSmokeSeconds;
                SetEmission(barrelSmoke, BarrelSmokeRate);
            }

            MuzzleFlashCount++;
        }

        /// <summary>Rate of the barrel plume while it is open.</summary>
        private const float BarrelSmokeRate = 7f;

        private void OnDestroyed(TankHealth source)
        {
            destroyed = true;
            SuppressMovement();
            CombatVfxSystem system = CombatVfxSystem.Current;
            if (system == null)
            {
                return;
            }

            // The size of the burst follows the machine, because the pool only knows the prefab and the
            // machine is the only thing here that knows how large it is.
            float scale = Mathf.Max(0.4f, hullSize.x * 2.2f);
            system.Play(VfxEmission.At(CombatVfxEffects.DestructionBurst, transform.position, Quaternion.identity, scale));
            DestructionBurstCount++;
        }

        private float CaliberMm()
        {
            WeaponDefinition definition = config != null ? config.weapon : null;
            return definition != null ? definition.caliberMm : 0f;
        }

        /// <summary>
        /// True while the machine may show itself. An unspotted machine draws nothing at all: a cloud of
        /// dust or a line of fresh prints would hand the player exactly the position the visibility system
        /// just took away. The development silhouette counts as visible, because its whole purpose is to
        /// be looked at.
        /// </summary>
        private bool IsVisibleToPlayer()
        {
            return visibility == null || visibility.IsVisible || visibility.ShowHiddenTanksForTesting;
        }

        /// <summary>
        /// Hull extents from the machine's own collider outline, and the track gauge from the measurement
        /// the prefab carries. Reading the outline instead of the world AABB keeps the numbers correct
        /// while the hull is rotated.
        /// </summary>
        private void MeasureHull()
        {
            PolygonCollider2D hull = GetComponent<PolygonCollider2D>();
            if (hull != null && hull.points != null && hull.points.Length >= 3)
            {
                Vector2 minimum = hull.points[0] + hull.offset;
                Vector2 maximum = minimum;
                foreach (Vector2 point in hull.points)
                {
                    Vector2 shifted = point + hull.offset;
                    minimum = Vector2.Min(minimum, shifted);
                    maximum = Vector2.Max(maximum, shifted);
                }

                Vector2 size = maximum - minimum;
                if (size.x > 0.01f && size.y > 0.01f)
                {
                    hullSize = size;
                }
            }

            // Where the tracks are is measured from the model; the hull width is only a fallback for a
            // prefab that predates the measurement, and both keep the dust and the prints on the tracks
            // rather than under the centre of the machine.
            TrackSeparation = measuredTrackSeparation > 0.01f ? measuredTrackSeparation : hullSize.x * 0.78f;
            halfTrackOffset = Mathf.Max(0.02f, TrackSeparation * 0.5f);
        }

        /// <summary>Middle of the rear half of the hull, where dust comes out from under the tracks.</summary>
        private float RearOffset()
        {
            return -hullSize.y * 0.3f;
        }

        private static int Count(ParticleSystem[] systems)
        {
            return systems != null ? systems.Length : 0;
        }

        private static void SetEmission(ParticleSystem[] systems, float rate)
        {
            if (systems == null)
            {
                return;
            }

            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = Mathf.Max(0f, rate);
                emission.enabled = rate > 0.01f;
            }
        }

        /// <summary>The lights inside a generated effect prefab, if it has any.</summary>
        private static Light2D[] CollectLights(ParticleSystem[] systems)
        {
            if (systems == null || systems.Length == 0)
            {
                return System.Array.Empty<Light2D>();
            }

            Transform root = systems[0].transform.parent;
            return root != null
                ? root.GetComponentsInChildren<Light2D>(true)
                : System.Array.Empty<Light2D>();
        }

        private static void SetLightsEnabled(Light2D[] lights, bool enabled)
        {
            foreach (Light2D light in lights)
            {
                if (light != null)
                {
                    light.enabled = enabled;
                }
            }
        }

        /// <summary>
        /// Opens or closes an effect without touching its rates. The fire is layered inside its generated
        /// prefab, so enabling it keeps the balance between flame, core and embers that the builder chose.
        /// </summary>
        private static void SetEmissionEnabled(ParticleSystem[] systems, bool enabled)
        {
            if (systems == null)
            {
                return;
            }

            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.EmissionModule emission = system.emission;
                emission.enabled = enabled;
            }
        }

        private static void ApplyStartColor(ParticleSystem[] systems, Color color)
        {
            if (systems == null)
            {
                return;
            }

            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.MainModule main = system.main;
                main.startColor = color;
            }
        }

        private static void ApplyStartSize(ParticleSystem[] systems, float size)
        {
            if (systems == null)
            {
                return;
            }

            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.MainModule main = system.main;
                main.startSize = Mathf.Max(0.01f, size);
            }
        }

        private static void ApplyVelocityScale(ParticleSystem[] systems, float multiplier)
        {
            if (systems == null)
            {
                return;
            }

            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                velocity.yMultiplier = Mathf.Max(0f, multiplier);
                velocity.xMultiplier = Mathf.Max(0f, multiplier);
            }
        }
    }
}
