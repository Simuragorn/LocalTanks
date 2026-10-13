using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Every visual effect that belongs to one machine: the print of each track, the dust or spray it
    /// raises, the exhaust, the smoke and fire of damage, the muzzle flash and the burst of destruction.
    /// </summary>
    /// <remarks>
    /// The rig resolves nothing about appearance itself. It reads its own geometry (the hull collider it
    /// already has), its own state (speed, turn rate, remaining health) and the surface it stands on, and
    /// asks <see cref="CombatVfxSystem"/> for the band that matches. A machine therefore needs no visual
    /// profile of its own, and a new tank looks right the moment its caliber, mass and class exist.
    /// <para>
    /// All loop effects are children of the machine; every print goes to the world-space container of the
    /// effect system, because a print belongs to the ground. Nothing is created per frame: the loops are
    /// instantiated once when the machine appears and the prints come from a fixed ring buffer.
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
        private ParticleSystem leftDust;
        private ParticleSystem rightDust;
        private ParticleSystem leftSpray;
        private ParticleSystem rightSpray;
        private ParticleSystem damageSmoke;
        private ParticleSystem damageFire;
        private ParticleSystem exhaust;

        private Vector2 hullSize = new Vector2(0.6f, 1.0f);
        private float halfTrackOffset = 0.18f;
        private bool built;
        private bool destroyed;
        private float appliedDustRate = -1f;
        private float appliedSprayRate = -1f;
        private float appliedExhaustRate = -1f;
        private TerrainKind appliedSurface = TerrainKind.Blocked;
        private DamageVfxProfile appliedDamage;

        public bool IsBuilt => built;
        public bool HasVisuals => built && (marks != null || leftDust != null || damageSmoke != null);
        public int MuzzleFlashCount { get; private set; }
        public int DestructionBurstCount { get; private set; }
        public int EmittedMarks => marks != null ? marks.EmittedCount : 0;
        public TerrainKind CurrentSurface { get; private set; } = TerrainKind.Grass;
        public DamageVfxProfile CurrentDamageProfile => appliedDamage;
        public Vector2 HullSize => hullSize;

        private void Awake()
        {
            motor ??= GetComponent<TankMotor>();
            health ??= GetComponent<TankHealth>();
            weapon ??= GetComponent<WeaponController>();
            terrain ??= GetComponent<TerrainMotorModifier>();
            visibility ??= GetComponent<TankVisibilityPresenter>();
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
            TankVisibilityPresenter newVisibility = null)
        {
            config = newConfig;
            motor = newMotor != null ? newMotor : GetComponent<TankMotor>();
            health = newHealth != null ? newHealth : GetComponent<TankHealth>();
            weapon = newWeapon != null ? newWeapon : GetComponent<WeaponController>();
            terrain = newTerrain != null ? newTerrain : GetComponent<TerrainMotorModifier>();
            visibility = newVisibility != null ? newVisibility : GetComponent<TankVisibilityPresenter>();
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

            // Prints belong to the ground and keep fading whether or not their author is still on screen.
            marks?.Tick(deltaTime);
        }

        /// <summary>
        /// Instantiates the persistent part of the effect set once. A missing generated asset leaves the
        /// corresponding system null and the machine simply looks the way it did before the effects
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
                marks = gameObject.AddComponent<TankTrackMarkEmitter>();
                marks.Configure(provider.LoadMarkSprite(), movement.markLimit);
            }

            leftDust = AttachLoop(provider, CombatVfxEffects.TrackDust, "VfxDustLeft", -halfTrackOffset, RearOffset());
            rightDust = AttachLoop(provider, CombatVfxEffects.TrackDust, "VfxDustRight", halfTrackOffset, RearOffset());
            leftSpray = AttachLoop(provider, CombatVfxEffects.WaterSpray, "VfxSprayLeft", -halfTrackOffset, RearOffset());
            rightSpray = AttachLoop(provider, CombatVfxEffects.WaterSpray, "VfxSprayRight", halfTrackOffset, RearOffset());
            damageSmoke = AttachLoop(provider, CombatVfxEffects.DamageSmoke, "VfxDamageSmoke", hullSize.x * 0.12f, -hullSize.y * 0.08f);
            damageFire = AttachLoop(provider, CombatVfxEffects.DamageFire, "VfxDamageFire", -hullSize.x * 0.12f, -hullSize.y * 0.08f);
            exhaust = AttachLoop(provider, CombatVfxEffects.ExhaustSmoke, "VfxExhaust", hullSize.x * 0.34f, -hullSize.y * 0.42f);

            // The generated loops start emitting on their own so that a prefab looked at in isolation
            // behaves like the machine does. Until the rig has read the machine's state, every one of them
            // is silenced, or a parked tank would puff at the rate the prefab was built with.
            SetEmission(leftDust, 0f);
            SetEmission(rightDust, 0f);
            SetEmission(leftSpray, 0f);
            SetEmission(rightSpray, 0f);
            SetEmission(exhaust, 0f);
            SetEmission(damageSmoke, 0f);
            SetEmissionEnabled(damageFire, false);
            appliedDustRate = 0f;
            appliedSprayRate = 0f;
            appliedExhaustRate = 0f;
        }

        private ParticleSystem AttachLoop(
            ICombatVfxAssetProvider provider,
            string effectId,
            string instanceName,
            float localX,
            float localY)
        {
            GameObject prefab = provider.LoadPrefab(effectId);
            if (prefab == null)
            {
                return null;
            }

            GameObject instance = Instantiate(prefab, transform);
            instance.name = instanceName;
            instance.transform.localPosition = new Vector3(localX, localY, 0f);
            instance.transform.localRotation = Quaternion.identity;
            ParticleSystem system = instance.GetComponent<ParticleSystem>();
            if (system == null)
            {
                Destroy(instance);
                return null;
            }

            // Clears whatever the prefab emitted on the frame it was created, then starts it again: a
            // stopped particle system keeps its rate but never emits, so the rig must leave every loop
            // playing and control it through the emission module instead.
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Play(true);
            return system;
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
            if (RateChanged(exhaustRate, appliedExhaustRate))
            {
                appliedExhaustRate = exhaustRate;
                SetEmission(exhaust, exhaustRate);
            }

            EmitPrints(profile, markMultiplier, deltaTime);
        }

        /// <summary>
        /// Places the prints this step earned for each track. Each track has its own accumulator and its
        /// own direction, which is what makes a turn in place leave two arcs instead of one twisted ribbon.
        /// </summary>
        private void EmitPrints(MovementVfxProfile profile, float surfaceMarkMultiplier, float deltaTime)
        {
            if (marks == null || profile.markOpacity <= 0f || surfaceMarkMultiplier <= 0f)
            {
                return;
            }

            float spacing = profile.markSpacingFraction * hullSize.y;
            float width = profile.markWidthFraction * hullSize.x;
            float length = profile.markLengthFraction * hullSize.y;
            Color color = Color.white;
            color.a = Mathf.Clamp01(profile.markOpacity * surfaceMarkMultiplier);

            Vector2 forward = transform.up;
            Vector2 right = transform.right;
            float speed = motor.CurrentSpeed;
            float turnRate = motor.CurrentTurnRate;
            EmitTrack(ref leftTrail, -halfTrackOffset, forward, right, speed, turnRate, deltaTime, spacing, width, length, color, profile.markFadeSeconds);
            EmitTrack(ref rightTrail, halfTrackOffset, forward, right, speed, turnRate, deltaTime, spacing, width, length, color, profile.markFadeSeconds);
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
                SetEmissionEnabled(damageFire, false);
                return;
            }

            if (damageSmoke != null)
            {
                Color tint = new Color(0.16f, 0.15f, 0.14f, Mathf.Clamp01(profile.smokeOpacity));
                ParticleSystem.MainModule main = damageSmoke.main;
                main.startColor = tint;
                main.startSize = profile.smokeSizeFraction * hullSize.x;
                ApplyVelocityScale(damageSmoke, profile.smokeRise);
                SetEmission(damageSmoke, profile.smokeRate);
            }

            // The fire keeps the rate the prefab was generated with: how much flame a burning machine
            // shows is a property of the flame, and the band only decides whether it burns at all.
            SetEmissionEnabled(damageFire, profile.fire);
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
            system.Play(VfxEmission.At(CombatVfxEffects.MuzzleFlash, muzzle.position, rotation, profile.flashScale));
            if (profile.smokeBurst > 0)
            {
                system.Play(VfxEmission.At(CombatVfxEffects.MuzzleSmoke, muzzle.position, rotation, profile.smokeScale)
                    .WithCount(profile.smokeBurst));
            }

            MuzzleFlashCount++;
        }

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
        /// Hull extents from the machine's own collider outline. Reading the outline instead of the world
        /// AABB keeps the numbers correct while the hull is rotated, and it keeps a machine's track width
        /// and print size derived from geometry nobody has to repeat in data.
        /// </summary>
        private void MeasureHull()
        {
            PolygonCollider2D hull = GetComponent<PolygonCollider2D>();
            if (hull == null || hull.points == null || hull.points.Length < 3)
            {
                return;
            }

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
                // Prints sit at the outer edge of the hull, where the tracks are, not under its centre.
                halfTrackOffset = Mathf.Max(0.02f, size.x * 0.5f - size.x * 0.13f);
            }
        }

        /// <summary>Middle of the rear half of the hull, where dust comes out from under the tracks.</summary>
        private float RearOffset()
        {
            return -hullSize.y * 0.3f;
        }

        private static void SetEmission(ParticleSystem system, float rate)
        {
            if (system == null)
            {
                return;
            }

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = Mathf.Max(0f, rate);
            emission.enabled = rate > 0.01f;
        }

        private static void SetEmissionEnabled(ParticleSystem system, bool enabled)
        {
            if (system == null)
            {
                return;
            }

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = enabled;
        }

        private static void ApplyStartColor(ParticleSystem system, Color color)
        {
            if (system == null)
            {
                return;
            }

            ParticleSystem.MainModule main = system.main;
            main.startColor = color;
        }

        private static void ApplyStartSize(ParticleSystem system, float size)
        {
            if (system == null)
            {
                return;
            }

            ParticleSystem.MainModule main = system.main;
            main.startSize = Mathf.Max(0.01f, size);
        }

        private static void ApplyVelocityScale(ParticleSystem system, float multiplier)
        {
            if (system == null)
            {
                return;
            }

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.yMultiplier = Mathf.Max(0f, multiplier);
            velocity.xMultiplier = Mathf.Max(0f, multiplier);
        }
    }
}
