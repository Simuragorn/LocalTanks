using UnityEngine;

namespace LocalTanks
{
    public interface ITankAudioClipProvider
    {
        AudioClip Load(string clipId);
    }

    public sealed class ResourcesTankAudioClipProvider : ITankAudioClipProvider
    {
        public AudioClip Load(string clipId)
        {
            return string.IsNullOrWhiteSpace(clipId)
                ? null
                : Resources.Load<AudioClip>($"WoTAudio/Clips/{clipId}");
        }
    }

    [RequireComponent(typeof(TankMotor))]
    public sealed class TankAudioController : MonoBehaviour
    {
        private const float SpatialBlend = 0.72f;
        private const float MinimumDistance = 7f;
        private const float MaximumDistance = 30f;
        private static uint nextEnginePhase;

        [SerializeField] private TankDefinition config;
        [SerializeField] private TankMotor motor;
        [SerializeField] private TurretAiming turret;
        [SerializeField] private WeaponController weapon;
        [SerializeField] private TankHealth health;

        private ITankAudioClipProvider clipProvider;
        private AudioSource engineSource;
        private AudioSource turretSource;
        private AudioSource weaponSource;
        private AudioClip shotClip;
        private bool configured;

        public AudioSource EngineSource => engineSource;
        public AudioSource TurretSource => turretSource;
        public AudioSource WeaponSource => weaponSource;
        public bool HasEngineClip => engineSource != null && engineSource.clip != null;
        public bool HasShotClip => shotClip != null;
        public int ShotPlayCount { get; private set; }

        private void Awake()
        {
            motor ??= GetComponent<TankMotor>();
            weapon ??= GetComponent<WeaponController>();
            health ??= GetComponent<TankHealth>();
            turret ??= GetComponentInChildren<TurretAiming>();
            EnsureSources();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            if (!configured && config != null)
            {
                Configure(config, motor, turret, weapon, health);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            StopLoops();
        }

        private void Update()
        {
            TankAudioProfile profile = config != null ? config.audio : null;
            if (profile == null || motor == null)
            {
                return;
            }

            float speedLimit = motor.CurrentSpeed < 0f
                ? Mathf.Max(0.01f, config.maxReverseSpeed)
                : Mathf.Max(0.01f, config.maxForwardSpeed);
            float movement01 = Mathf.Clamp01(Mathf.Abs(motor.CurrentSpeed) / speedLimit);
            float turn01 = Mathf.Clamp01(Mathf.Abs(motor.CurrentTurnRate) / Mathf.Max(0.01f, config.hullTurnSpeed));
            float load01 = Mathf.Max(movement01, turn01 * 0.65f);

            if (engineSource != null && engineSource.clip != null)
            {
                engineSource.volume = profile.engineVolume * Mathf.Lerp(0.48f, 1f, load01);
                engineSource.pitch = profile.enginePitch * Mathf.Lerp(0.82f, 1.2f, load01);
                StartLoop(engineSource);
            }

            if (turretSource != null && turretSource.clip != null && turret != null)
            {
                float turret01 = Mathf.Clamp01(
                    Mathf.Abs(turret.AngularVelocityDegreesPerSecond) /
                    Mathf.Max(0.01f, config.turretTurnSpeed));
                turretSource.volume = profile.turretVolume * turret01;
                turretSource.pitch = profile.turretPitch * Mathf.Lerp(0.92f, 1.08f, turret01);
                if (turret01 > 0.015f)
                {
                    StartLoop(turretSource);
                }
                else if (turretSource.isPlaying)
                {
                    turretSource.Pause();
                }
            }
        }

        public void Configure(
            TankDefinition newConfig,
            TankMotor newMotor,
            TurretAiming newTurret,
            WeaponController newWeapon,
            TankHealth newHealth = null,
            ITankAudioClipProvider newClipProvider = null)
        {
            Unsubscribe();
            config = newConfig;
            motor = newMotor != null ? newMotor : GetComponent<TankMotor>();
            turret = newTurret != null ? newTurret : GetComponentInChildren<TurretAiming>();
            weapon = newWeapon != null ? newWeapon : GetComponent<WeaponController>();
            health = newHealth != null ? newHealth : GetComponent<TankHealth>();
            clipProvider = newClipProvider ?? new ResourcesTankAudioClipProvider();
            configured = true;
            EnsureSources();
            if (Application.isPlaying)
            {
                LoadClips();
            }
            else
            {
                engineSource.clip = null;
                turretSource.clip = null;
                shotClip = null;
            }
            Subscribe();
        }

        private void EnsureSources()
        {
            if (engineSource == null) engineSource = CreateSource("EngineAudio", true);
            if (turretSource == null) turretSource = CreateSource("TurretAudio", true);
            if (weaponSource == null) weaponSource = CreateSource("WeaponAudio", false);
        }

        private AudioSource CreateSource(string sourceName, bool loop)
        {
            GameObject child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = SpatialBlend;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = MinimumDistance;
            source.maxDistance = MaximumDistance;
            source.dopplerLevel = 0f;
            return source;
        }

        private void LoadClips()
        {
            TankAudioProfile profile = config != null ? config.audio : null;
            if (profile == null || clipProvider == null)
            {
                return;
            }

            engineSource.clip = clipProvider.Load(profile.engineClipId);
            engineSource.pitch = profile.enginePitch;
            if (engineSource.clip != null && engineSource.clip.length > 0f)
            {
                uint phase = nextEnginePhase++ % 997u;
                engineSource.time = phase / 997f * engineSource.clip.length;
            }
            turretSource.clip = clipProvider.Load(profile.turretClipId);
            turretSource.pitch = profile.turretPitch;
            shotClip = clipProvider.Load(profile.shotClipId);
        }

        private void Subscribe()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

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
            if (weapon != null) weapon.Fired -= OnFired;
            if (health != null) health.Destroyed -= OnDestroyed;
        }

        private void OnFired(WeaponController source)
        {
            TankAudioProfile profile = config != null ? config.audio : null;
            if (profile == null || weaponSource == null || clipProvider == null)
            {
                return;
            }

            if (shotClip == null)
            {
                return;
            }

            weaponSource.pitch = profile.shotPitch;
            weaponSource.PlayOneShot(shotClip, profile.shotVolume);
            ShotPlayCount++;
        }

        private void OnDestroyed(TankHealth destroyedHealth)
        {
            StopLoops();
        }

        private static void StartLoop(AudioSource source)
        {
            if (source.isPlaying)
            {
                return;
            }

            source.UnPause();
            if (!source.isPlaying) source.Play();
        }

        private void StopLoops()
        {
            if (engineSource != null) engineSource.Stop();
            if (turretSource != null) turretSource.Stop();
        }
    }
}
