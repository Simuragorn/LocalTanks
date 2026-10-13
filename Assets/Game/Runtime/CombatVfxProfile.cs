using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>A colour in the committed VFX profile file, in linear 0..1 components.</summary>
    [Serializable]
    public struct VfxColor
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public VfxColor(float red, float green, float blue, float alpha)
        {
            r = red;
            g = green;
            b = blue;
            a = alpha;
        }

        public Color ToColor()
        {
            return new Color(r, g, b, a);
        }

        public bool IsFinite()
        {
            return IsFinite(r) && IsFinite(g) && IsFinite(b) && IsFinite(a);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// How large a shot looks. One band serves every gun of a caliber range, so a 45 mm and an 88 mm
    /// gun differ because their caliber differs and not because someone tuned two machines separately.
    /// </summary>
    [Serializable]
    public sealed class WeaponVfxProfile
    {
        public string id;
        public string displayName;

        /// <summary>Upper caliber of the band in millimetres; the last band is open-ended.</summary>
        public float maxCaliberMm;

        public float flashScale = 0.5f;
        public float flashSeconds = 0.06f;
        public float smokeScale = 0.6f;
        public float smokeSeconds = 1.2f;
        public int smokeBurst = 8;

        /// <summary>
        /// How long the barrel keeps smoking after a shot. Propellant leaves the bore for a moment after
        /// the flash is gone, and a longer gun breathes for longer.
        /// </summary>
        public float barrelSmokeSeconds = 0.5f;

        public float recoilDistance = 0.05f;
        public float lightIntensity = 0.7f;
        public float lightRadius = 1.8f;
        public float lightSeconds = 0.05f;
    }

    /// <summary>
    /// How much ground a machine disturbs. Driven by mass, which is the honest reason a light tank
    /// raises less dust and presses a fainter print than a heavy one.
    /// </summary>
    [Serializable]
    public sealed class MovementVfxProfile
    {
        public string id;
        public string displayName;

        /// <summary>Upper mass of the band in tonnes; the last band is open-ended.</summary>
        public float maxMassTonnes;

        [Range(0f, 1f)] public float dustOpacity = 0.3f;

        /// <summary>Particle size of one dust puff as a fraction of the hull width.</summary>
        public float dustSizeFraction = 0.55f;

        public float dustRate = 6f;

        /// <summary>Multiplier of the drift the generated dust prefab was built with.</summary>
        public float dustSpeed = 1f;

        /// <summary>Exhaust puffs per second at a standstill and at the machine's top speed.</summary>
        public float exhaustIdleRate = 1.5f;
        public float exhaustLoadedRate = 6f;

        [Range(0f, 1f)] public float markOpacity = 0.35f;

        /// <summary>Distance between prints as a fraction of the hull length.</summary>
        public float markSpacingFraction = 0.12f;

        /// <summary>Length of one print as a fraction of the hull length.</summary>
        public float markLengthFraction = 0.16f;

        /// <summary>
        /// Width of one print as a fraction of the machine's track gauge, which the sprite pipeline
        /// measures from the model. A heavier machine therefore presses a wider print without a number of
        /// its own, and the two print lines stay on its tracks.
        /// </summary>
        public float markWidthFraction = 0.35f;

        public float markFadeSeconds = 6f;

        /// <summary>Prints one machine may keep on the ground at once; the oldest is reused first.</summary>
        public int markLimit = 48;

        public float sprayRate = 9f;
    }

    /// <summary>
    /// What a damaged machine trails. Selected by the remaining health fraction, so damage changes the
    /// look of every machine with no per-machine configuration.
    /// </summary>
    [Serializable]
    public sealed class DamageVfxProfile
    {
        public string id;
        public string displayName;

        /// <summary>
        /// Health fraction at which this band begins; the table runs from the healthiest band to the wreck,
        /// and the band that owns a fraction is the first one whose threshold it still reaches.
        /// </summary>
        public float minHealthFraction;

        /// <summary>Exactly one band carries this flag and must be the last entry of the table.</summary>
        public bool wreck;

        public float smokeRate = 3f;
        [Range(0f, 1f)] public float smokeOpacity = 0.3f;

        /// <summary>Particle size of one smoke puff as a fraction of the hull width.</summary>
        public float smokeSizeFraction = 0.8f;

        public float smokeRise = 1f;
        public bool fire;
    }

    /// <summary>Look of one impact result: penetration, non-penetration, ricochet or a hit on the map.</summary>
    [Serializable]
    public sealed class ImpactVfxProfile
    {
        public string id;
        public string displayName;

        /// <summary>One of <c>penetrated</c>, <c>blocked</c>, <c>ricocheted</c> or <c>ground</c>.</summary>
        public string outcome;

        public float flashScale = 0.5f;
        public int sparkCount = 12;
        public float sparkSpeed = 4f;
        public float dustScale = 0.4f;
        public int debrisCount = 1;
        public float seconds = 0.6f;
    }

    /// <summary>
    /// How a surface answers: dust colour, how visible a print stays and whether water splashes.
    /// The surface itself comes from the map, never from a machine.
    /// </summary>
    [Serializable]
    public sealed class SurfaceVfxProfile
    {
        public string id;
        public string displayName;

        /// <summary>Name of a <see cref="TerrainKind"/> value.</summary>
        public string terrain;

        public VfxColor dustTint = new VfxColor(0.62f, 0.56f, 0.42f, 1f);

        /// <summary>
        /// Colour the track print is multiplied by. It is dark on purpose: a print is a groove pressed
        /// into the ground, and the colour has to come from data rather than from the texture, because a
        /// sprite renderer under the 2D render pipeline did not reproduce the texture's own dark pixels.
        /// </summary>
        public VfxColor markTint = new VfxColor(0.16f, 0.15f, 0.13f, 1f);

        public float dustRateMultiplier = 1f;
        public float markOpacityMultiplier = 1f;
        public bool splash;

        public bool TryGetTerrain(out TerrainKind kind)
        {
            return Enum.TryParse(terrain, false, out kind);
        }
    }

    /// <summary>Budgets shared by every effect, so a heavy battle cannot create unbounded objects.</summary>
    [Serializable]
    public sealed class CombatVfxLimits
    {
        /// <summary>Live one-shot effects at once. Older ones are recycled instead of allocating more.</summary>
        public int maxPooledInstances = 192;

        /// <summary>Multiplier applied to every particle count; the one place quality is dialled down.</summary>
        public float qualityScale = 1f;
    }

    /// <summary>
    /// All combat visual effects in one committed JSON resource.
    /// </summary>
    /// <remarks>
    /// This is a curated art-direction table, not a copy of machine data: bands are keyed by caliber,
    /// mass, remaining health, impact result and map surface. A new machine therefore appears with the
    /// right effects without a line of new data, and the tables cannot fall out of step with the fleet
    /// because no machine names them. The file is validated by an Editor command, by an Edit Mode test
    /// and by a build-time check in <c>CombatVfxAssetBuilder</c>.
    /// </remarks>
    [Serializable]
    public sealed class CombatVfxCatalog
    {
        public const string ResourceName = "CombatVfxProfiles";
        public const string SourcePath = "Assets/Game/Resources/CombatVfxProfiles.json";

        public const string GroundOutcomeId = "ground";
        public const string PenetratedOutcomeId = "penetrated";
        public const string BlockedOutcomeId = "blocked";
        public const string RicochetedOutcomeId = "ricocheted";

        public int schemaVersion = 1;
        public WeaponVfxProfile[] weaponProfiles = Array.Empty<WeaponVfxProfile>();
        public MovementVfxProfile[] movementProfiles = Array.Empty<MovementVfxProfile>();
        public DamageVfxProfile[] damageProfiles = Array.Empty<DamageVfxProfile>();
        public ImpactVfxProfile[] impactProfiles = Array.Empty<ImpactVfxProfile>();
        public SurfaceVfxProfile[] surfaceProfiles = Array.Empty<SurfaceVfxProfile>();
        public CombatVfxLimits limits = new CombatVfxLimits();

        private float[] weaponBounds;
        private float[] movementBounds;
        private float[] damageBounds;

        public static CombatVfxCatalog Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new CombatVfxCatalog();
            }

            return JsonUtility.FromJson<CombatVfxCatalog>(json) ?? new CombatVfxCatalog();
        }

        public static CombatVfxCatalog Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null)
            {
                Debug.LogWarning(
                    $"Combat VFX profile resource '{ResourceName}' is missing; battles stay without visual effects.");
                return new CombatVfxCatalog();
            }

            return Parse(asset.text);
        }

        /// <summary>Effect band for a gun of the given caliber. Null when the table is empty.</summary>
        public WeaponVfxProfile ResolveWeapon(float caliberMm)
        {
            weaponBounds ??= Bounds(weaponProfiles, profile => profile.maxCaliberMm);
            int index = VfxProfileMath.SelectBandIndex(Mathf.Max(0f, caliberMm), weaponBounds);
            return index < 0 ? null : weaponProfiles[index];
        }

        /// <summary>Effect band for a machine of the given mass. Null when the table is empty.</summary>
        public MovementVfxProfile ResolveMovement(float massTonnes)
        {
            movementBounds ??= Bounds(movementProfiles, profile => profile.maxMassTonnes);
            int index = VfxProfileMath.SelectBandIndex(Mathf.Max(0f, massTonnes), movementBounds);
            return index < 0 ? null : movementProfiles[index];
        }

        /// <summary>
        /// Effect band for a machine that has the given fraction of its health left. A destroyed machine
        /// always uses the wreck band, because a wreck smokes for the rest of the battle regardless of
        /// what it was doing while it burned.
        /// </summary>
        public DamageVfxProfile ResolveDamage(float healthFraction, bool destroyed)
        {
            if (damageProfiles == null || damageProfiles.Length == 0)
            {
                return null;
            }

            if (destroyed)
            {
                return WreckProfile();
            }

            damageBounds ??= LiveDamageBounds();
            int last = damageProfiles.Length - 1;
            int index = VfxProfileMath.SelectDescendingBandIndex(Mathf.Clamp01(healthFraction), damageBounds);
            // The wreck band is the open-ended last entry and never represents a live machine.
            return index < 0 ? null : damageProfiles[Mathf.Min(index, last)];
        }

        /// <summary>
        /// Health-fraction bounds of the bands that describe a live machine. The wreck band is excluded
        /// on purpose: its own threshold is meaningless, because a destroyed machine is selected by the
        /// wreck flag rather than by how much health it had.
        /// </summary>
        private float[] LiveDamageBounds()
        {
            List<float> bounds = new List<float>(damageProfiles.Length);
            foreach (DamageVfxProfile profile in damageProfiles)
            {
                if (profile != null && !profile.wreck)
                {
                    bounds.Add(profile.minHealthFraction);
                }
            }

            return bounds.ToArray();
        }

        /// <summary>Look of one armour impact result.</summary>
        public ImpactVfxProfile ResolveImpact(ImpactOutcome outcome)
        {
            switch (outcome)
            {
                case ImpactOutcome.Penetrated: return ImpactByOutcome(PenetratedOutcomeId);
                case ImpactOutcome.Ricocheted: return ImpactByOutcome(RicochetedOutcomeId);
                default: return ImpactByOutcome(BlockedOutcomeId);
            }
        }

        /// <summary>Look of a shell that hit the map instead of armour.</summary>
        public ImpactVfxProfile GroundImpact => ImpactByOutcome(GroundOutcomeId);

        /// <summary>Look of the surface the machine is standing on.</summary>
        public SurfaceVfxProfile ResolveSurface(TerrainKind terrain)
        {
            if (surfaceProfiles == null)
            {
                return null;
            }

            foreach (SurfaceVfxProfile profile in surfaceProfiles)
            {
                if (profile != null && profile.TryGetTerrain(out TerrainKind kind) && kind == terrain)
                {
                    return profile;
                }
            }

            return null;
        }

        /// <summary>The band used by a destroyed machine, or null when the table declares none.</summary>
        public DamageVfxProfile WreckProfile()
        {
            if (damageProfiles == null)
            {
                return null;
            }

            foreach (DamageVfxProfile profile in damageProfiles)
            {
                if (profile != null && profile.wreck)
                {
                    return profile;
                }
            }

            return null;
        }

        private ImpactVfxProfile ImpactByOutcome(string outcome)
        {
            if (impactProfiles == null)
            {
                return null;
            }

            foreach (ImpactVfxProfile profile in impactProfiles)
            {
                if (profile != null && string.Equals(profile.outcome, outcome, StringComparison.Ordinal))
                {
                    return profile;
                }
            }

            return null;
        }

        /// <summary>Every problem found in the catalog, in a stable order.</summary>
        public string[] Validate()
        {
            List<string> errors = new List<string>();
            if (schemaVersion != 1)
            {
                errors.Add($"Combat VFX catalog has unsupported schemaVersion {schemaVersion}.");
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            ValidateWeapons(errors, ids);
            ValidateMovement(errors, ids);
            ValidateDamage(errors, ids);
            ValidateImpacts(errors, ids);
            ValidateSurfaces(errors, ids);

            if (limits == null)
            {
                errors.Add("Combat VFX catalog has no limits block.");
            }
            else
            {
                RequireRange(limits.maxPooledInstances, 1, 4096, "limits.maxPooledInstances", errors);
                RequireRange(limits.qualityScale, 0.05f, 2f, "limits.qualityScale", errors);
            }

            return errors.ToArray();
        }

        private void ValidateWeapons(List<string> errors, HashSet<string> ids)
        {
            if (weaponProfiles == null || weaponProfiles.Length == 0)
            {
                errors.Add("Combat VFX catalog declares no weapon profiles.");
                return;
            }

            float previous = float.NegativeInfinity;
            float lastFlashScale = 0f;
            foreach (WeaponVfxProfile profile in weaponProfiles)
            {
                if (!ValidateEntry(profile?.id, profile?.displayName, "Weapon VFX profile", ids, errors))
                {
                    continue;
                }

                string field = $"Weapon VFX profile '{profile.id}'";
                RequirePositive(profile.maxCaliberMm, field + ".maxCaliberMm", errors);
                if (profile.maxCaliberMm <= previous)
                {
                    errors.Add($"{field}.maxCaliberMm must grow along the table; {profile.maxCaliberMm} follows {previous}.");
                }

                previous = profile.maxCaliberMm;
                RequirePositive(profile.flashScale, field + ".flashScale", errors);
                if (profile.flashScale < lastFlashScale)
                {
                    // The table is ordered by caliber, so a smaller flash behind a larger one contradicts
                    // the only reason the bands exist. A guard here is what keeps the progression from
                    // being broken one value at a time.
                    errors.Add($"{field}.flashScale must not fall as the caliber grows; " +
                               $"{profile.flashScale} follows {lastFlashScale}.");
                }

                lastFlashScale = profile.flashScale;
                RequirePositive(profile.flashSeconds, field + ".flashSeconds", errors);
                RequireNonNegative(profile.smokeScale, field + ".smokeScale", errors);
                RequirePositive(profile.smokeSeconds, field + ".smokeSeconds", errors);
                RequireRange(profile.smokeBurst, 0, 64, field + ".smokeBurst", errors);
                RequireNonNegative(profile.barrelSmokeSeconds, field + ".barrelSmokeSeconds", errors);
                RequireNonNegative(profile.recoilDistance, field + ".recoilDistance", errors);
                RequireNonNegative(profile.lightIntensity, field + ".lightIntensity", errors);
                RequireNonNegative(profile.lightRadius, field + ".lightRadius", errors);
                RequirePositive(profile.lightSeconds, field + ".lightSeconds", errors);
            }
        }

        private void ValidateMovement(List<string> errors, HashSet<string> ids)
        {
            if (movementProfiles == null || movementProfiles.Length == 0)
            {
                errors.Add("Combat VFX catalog declares no movement profiles.");
                return;
            }

            float previous = float.NegativeInfinity;
            float lastDustRate = 0f;
            foreach (MovementVfxProfile profile in movementProfiles)
            {
                if (!ValidateEntry(profile?.id, profile?.displayName, "Movement VFX profile", ids, errors))
                {
                    continue;
                }

                string field = $"Movement VFX profile '{profile.id}'";
                RequirePositive(profile.maxMassTonnes, field + ".maxMassTonnes", errors);
                if (profile.maxMassTonnes <= previous)
                {
                    errors.Add($"{field}.maxMassTonnes must grow along the table; {profile.maxMassTonnes} follows {previous}.");
                }

                previous = profile.maxMassTonnes;
                RequireRange(profile.dustOpacity, 0f, 1f, field + ".dustOpacity", errors);
                RequirePositive(profile.dustSizeFraction, field + ".dustSizeFraction", errors);
                RequireNonNegative(profile.dustRate, field + ".dustRate", errors);
                if (profile.dustRate < lastDustRate)
                {
                    // Mass orders the table: a heavier machine cannot raise less dust than a lighter one
                    // without making the band it belongs to meaningless.
                    errors.Add($"{field}.dustRate must not fall as the mass grows; " +
                               $"{profile.dustRate} follows {lastDustRate}.");
                }

                lastDustRate = profile.dustRate;
                RequireNonNegative(profile.dustSpeed, field + ".dustSpeed", errors);
                RequireNonNegative(profile.exhaustIdleRate, field + ".exhaustIdleRate", errors);
                RequireNonNegative(profile.exhaustLoadedRate, field + ".exhaustLoadedRate", errors);
                RequireRange(profile.markOpacity, 0f, 1f, field + ".markOpacity", errors);
                RequirePositive(profile.markSpacingFraction, field + ".markSpacingFraction", errors);
                RequirePositive(profile.markLengthFraction, field + ".markLengthFraction", errors);
                RequirePositive(profile.markWidthFraction, field + ".markWidthFraction", errors);
                RequirePositive(profile.markFadeSeconds, field + ".markFadeSeconds", errors);
                RequireRange(profile.markLimit, 1, 512, field + ".markLimit", errors);
                RequireNonNegative(profile.sprayRate, field + ".sprayRate", errors);
            }
        }

        private void ValidateDamage(List<string> errors, HashSet<string> ids)
        {
            if (damageProfiles == null || damageProfiles.Length == 0)
            {
                errors.Add("Combat VFX catalog declares no damage profiles.");
                return;
            }

            int wreckCount = 0;
            float previous = float.PositiveInfinity;
            float lastSmokeRate = -1f;
            float lowestLiveThreshold = float.PositiveInfinity;
            for (int index = 0; index < damageProfiles.Length; index++)
            {
                DamageVfxProfile profile = damageProfiles[index];
                if (!ValidateEntry(profile?.id, profile?.displayName, "Damage VFX profile", ids, errors))
                {
                    continue;
                }

                string field = $"Damage VFX profile '{profile.id}'";
                RequireNonNegative(profile.smokeRate, field + ".smokeRate", errors);
                if (profile.smokeRate < lastSmokeRate)
                {
                    // Health orders the table: a machine that is closer to destruction cannot smoke less
                    // than a healthier one, or the band it belongs to says nothing about its state.
                    errors.Add($"{field}.smokeRate must not fall as health drops; " +
                               $"{profile.smokeRate} follows {lastSmokeRate}.");
                }

                lastSmokeRate = profile.smokeRate;
                RequireRange(profile.smokeOpacity, 0f, 1f, field + ".smokeOpacity", errors);
                RequirePositive(profile.smokeSizeFraction, field + ".smokeSizeFraction", errors);
                RequireNonNegative(profile.smokeRise, field + ".smokeRise", errors);

                if (profile.wreck)
                {
                    wreckCount++;
                    if (index != damageProfiles.Length - 1)
                    {
                        errors.Add($"{field} is the wreck band and must be the last entry of the table.");
                    }

                    continue;
                }

                if (profile.minHealthFraction < 0f || profile.minHealthFraction > 1f)
                {
                    errors.Add($"{field}.minHealthFraction must be between 0 and 1.");
                }

                if (profile.minHealthFraction >= previous)
                {
                    errors.Add($"{field}.minHealthFraction must fall along the table; " +
                               $"{profile.minHealthFraction} follows {previous}.");
                }

                previous = profile.minHealthFraction;
                lowestLiveThreshold = Mathf.Min(lowestLiveThreshold, profile.minHealthFraction);
            }

            if (wreckCount != 1)
            {
                errors.Add($"Combat VFX catalog must declare exactly one wreck damage profile; found {wreckCount}.");
            }

            if (float.IsPositiveInfinity(lowestLiveThreshold))
            {
                errors.Add("Combat VFX catalog declares no damage band for a live machine.");
            }
            else if (lowestLiveThreshold > 0f)
            {
                // Without a band that starts at zero, a machine on the edge of destruction would fall past
                // the whole table and show no damage at all — the loudest possible state would be the only
                // silent one.
                errors.Add("The lowest damage band must start at 0 so every remaining health fraction " +
                           $"belongs to a band; the lowest declared threshold is {lowestLiveThreshold:0.###}.");
            }
        }

        private void ValidateImpacts(List<string> errors, HashSet<string> ids)
        {
            if (impactProfiles == null || impactProfiles.Length == 0)
            {
                errors.Add("Combat VFX catalog declares no impact profiles.");
                return;
            }

            HashSet<string> outcomes = new HashSet<string>(StringComparer.Ordinal);
            foreach (ImpactVfxProfile profile in impactProfiles)
            {
                if (!ValidateEntry(profile?.id, profile?.displayName, "Impact VFX profile", ids, errors))
                {
                    continue;
                }

                string field = $"Impact VFX profile '{profile.id}'";
                if (!IsKnownOutcome(profile.outcome))
                {
                    errors.Add($"{field}.outcome must be one of '{PenetratedOutcomeId}', '{BlockedOutcomeId}', " +
                               $"'{RicochetedOutcomeId}' or '{GroundOutcomeId}'.");
                }
                else if (!outcomes.Add(profile.outcome))
                {
                    errors.Add($"{field}.outcome '{profile.outcome}' is declared twice.");
                }

                RequirePositive(profile.flashScale, field + ".flashScale", errors);
                RequireRange(profile.sparkCount, 0, 128, field + ".sparkCount", errors);
                RequireNonNegative(profile.sparkSpeed, field + ".sparkSpeed", errors);
                RequireNonNegative(profile.dustScale, field + ".dustScale", errors);
                RequireRange(profile.debrisCount, 0, 32, field + ".debrisCount", errors);
                RequirePositive(profile.seconds, field + ".seconds", errors);
            }

            foreach (string required in new[] { PenetratedOutcomeId, BlockedOutcomeId, RicochetedOutcomeId, GroundOutcomeId })
            {
                if (!outcomes.Contains(required))
                {
                    errors.Add($"Combat VFX catalog has no impact profile for outcome '{required}'.");
                }
            }
        }

        private void ValidateSurfaces(List<string> errors, HashSet<string> ids)
        {
            if (surfaceProfiles == null || surfaceProfiles.Length == 0)
            {
                errors.Add("Combat VFX catalog declares no surface profiles.");
                return;
            }

            HashSet<TerrainKind> terrains = new HashSet<TerrainKind>();
            foreach (SurfaceVfxProfile profile in surfaceProfiles)
            {
                if (!ValidateEntry(profile?.id, profile?.displayName, "Surface VFX profile", ids, errors))
                {
                    continue;
                }

                string field = $"Surface VFX profile '{profile.id}'";
                if (!profile.TryGetTerrain(out TerrainKind terrain))
                {
                    errors.Add($"{field}.terrain '{profile.terrain}' is not a known TerrainKind.");
                }
                else if (!terrains.Add(terrain))
                {
                    errors.Add($"{field}.terrain '{terrain}' is declared twice.");
                }

                if (!profile.dustTint.IsFinite())
                {
                    errors.Add($"{field}.dustTint must contain finite numbers.");
                }

                if (!profile.markTint.IsFinite())
                {
                    errors.Add($"{field}.markTint must contain finite numbers.");
                }

                RequireNonNegative(profile.dustRateMultiplier, field + ".dustRateMultiplier", errors);
                RequireRange(profile.markOpacityMultiplier, 0f, 2f, field + ".markOpacityMultiplier", errors);
            }

            foreach (TerrainKind terrain in Enum.GetValues(typeof(TerrainKind)))
            {
                if (!terrains.Contains(terrain))
                {
                    errors.Add($"Combat VFX catalog has no surface profile for TerrainKind.{terrain}.");
                }
            }
        }

        private bool ValidateEntry(
            string id,
            string displayName,
            string type,
            HashSet<string> ids,
            List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id))
            {
                errors.Add($"{type} has invalid id '{id}'. Use lowercase snake_case.");
                return false;
            }

            if (!ids.Add(id))
            {
                errors.Add($"Combat VFX catalog declares the id '{id}' twice.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                errors.Add($"{type} '{id}' has an empty displayName.");
            }

            return true;
        }

        private static bool IsKnownOutcome(string outcome)
        {
            return string.Equals(outcome, PenetratedOutcomeId, StringComparison.Ordinal) ||
                   string.Equals(outcome, BlockedOutcomeId, StringComparison.Ordinal) ||
                   string.Equals(outcome, RicochetedOutcomeId, StringComparison.Ordinal) ||
                   string.Equals(outcome, GroundOutcomeId, StringComparison.Ordinal);
        }

        private static float[] Bounds<T>(T[] profiles, Func<T, float> selector)
        {
            if (profiles == null || profiles.Length == 0)
            {
                return Array.Empty<float>();
            }

            float[] bounds = new float[profiles.Length];
            for (int index = 0; index < profiles.Length; index++)
            {
                bounds[index] = selector(profiles[index]);
            }

            return bounds;
        }

        private static readonly Regex IdPattern = new Regex("^[a-z0-9_]+$", RegexOptions.Compiled);

        private static void RequirePositive(float value, string field, List<string> errors)
        {
            if (!IsFinite(value) || value <= 0f)
            {
                errors.Add($"{field} must be finite and greater than zero.");
            }
        }

        private static void RequireNonNegative(float value, string field, List<string> errors)
        {
            if (!IsFinite(value) || value < 0f)
            {
                errors.Add($"{field} must be finite and non-negative.");
            }
        }

        private static void RequireRange(float value, float minimum, float maximum, string field, List<string> errors)
        {
            if (!IsFinite(value) || value < minimum || value > maximum)
            {
                errors.Add($"{field} must be between {minimum} and {maximum}.");
            }
        }

        private static void RequireRange(int value, int minimum, int maximum, string field, List<string> errors)
        {
            if (value < minimum || value > maximum)
            {
                errors.Add($"{field} must be between {minimum} and {maximum}.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
