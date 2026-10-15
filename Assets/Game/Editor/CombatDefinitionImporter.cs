using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    [Serializable]
    public sealed class MobilityJson
    {
        public float maxForwardSpeed;
        public float maxReverseSpeed;
        public float acceleration;
        public float groundResistance;
        public float braking;
        public float hullTurnSpeed;
        public float turretTurnSpeed;
    }

    [Serializable]
    public sealed class ArmorJson
    {
        public float front;
        public float left;
        public float right;
        public float rear;
    }

    [Serializable]
    public sealed class VisionJson
    {
        public float viewRange;
        public float stationaryConcealment;
        public float movementRevealPenalty;
        public float firingRevealPenalty;
        public float firingRevealDuration;
        public float guaranteedDetectionRange;
    }

    [Serializable]
    public sealed class GunHandlingJson
    {
        public float minimumDispersionDegrees;
        public float maximumDispersionDegrees;
        public float aimingTimeSeconds;
        public float movementDispersionDegrees;
        public float hullTraverseDispersionDegrees;
        public float turretTraverseDispersionDegrees;
        public float shotDispersionDegrees;
    }

    [Serializable]
    public sealed class TankAudioJson
    {
        public string engineClipId;
        public string tracksClipId;
        public string shotClipId;
        public string reloadClipId;
        public string turretClipId;
        public string destructionClipId;
        public float engineVolume = 0.45f;
        public float tracksVolume = 0.2f;
        public float shotVolume = 0.9f;
        public float reloadVolume = 0.36f;
        public float turretVolume = 0.22f;
        public float destructionVolume = 0.78f;
        public float enginePitch = 1f;
        public float tracksPitch = 1f;
        public float shotPitch = 1f;
        public float reloadPitch = 1f;
        public float turretPitch = 1f;
        public float destructionPitch = 1f;
    }

    [Serializable]
    public sealed class ShellAudioJson
    {
        public string penetrationClipId;
        public string blockedClipId;
        public string ricochetClipId;
        public float penetrationVolume = 0.28f;
        public float blockedVolume = 0.2f;
        public float ricochetVolume = 0.2f;
        public float pitch = 1f;
    }

    [Serializable]
    public sealed class TankDefinitionJson
    {
        public int schemaVersion;
        public string id;
        public string displayName;
        public string vehicleClass;
        public string nation;
        public int tier;
        public bool availableInGame;
        public int maxHitPoints;
        public float massTonnes;
        public float enginePowerHp;
        public string weaponId;
        public MobilityJson mobility;
        public ArmorJson armor;
        public VisionJson vision;
        public GunHandlingJson gunHandling;
        public TankAudioJson audio;
    }

    [Serializable]
    public sealed class WeaponDefinitionJson
    {
        public int schemaVersion;
        public string id;
        public string displayName;
        public float caliberMm;
        public float reloadSeconds;
        public int magazineSize = 1;
        public float shotIntervalSeconds;
        public string shellId;
    }

    [Serializable]
    public sealed class ShellDefinitionJson
    {
        public int schemaVersion;
        public string id;
        public string displayName;
        public int damage;
        public float penetration;
        public float speed;
        public float radius;
        public float lifetimeSeconds;
        public float maximumRange;
        public float ricochetAngle;
        public float ricochetSpeedMultiplier;
        public float ricochetPenetrationMultiplier;
        public int maximumRicochets;
        public ShellAudioJson audio;
    }

    public sealed class CombatDefinitionSet
    {
        public CombatDefinitionSet(
            TankDefinitionJson[] tanks,
            WeaponDefinitionJson[] weapons,
            ShellDefinitionJson[] shells)
        {
            Tanks = tanks;
            Weapons = weapons;
            Shells = shells;
        }

        public TankDefinitionJson[] Tanks { get; }
        public WeaponDefinitionJson[] Weapons { get; }
        public ShellDefinitionJson[] Shells { get; }
    }

    public static class CombatDefinitionValidator
    {
        public static string[] Validate(CombatDefinitionSet definitions)
        {
            List<string> errors = new List<string>();
            HashSet<string> allIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> weaponIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> shellIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (ShellDefinitionJson shell in definitions.Shells)
            {
                ValidateCommon(shell.schemaVersion, shell.id, shell.displayName, "Shell", allIds, errors);
                shellIds.Add(shell.id ?? string.Empty);
                RequirePositive(shell.damage, $"Shell '{shell.id}'.damage", errors);
                RequirePositive(shell.penetration, $"Shell '{shell.id}'.penetration", errors);
                RequirePositive(shell.speed, $"Shell '{shell.id}'.speed", errors);
                RequirePositive(shell.radius, $"Shell '{shell.id}'.radius", errors);
                RequirePositive(shell.lifetimeSeconds, $"Shell '{shell.id}'.lifetimeSeconds", errors);
                RequirePositive(shell.maximumRange, $"Shell '{shell.id}'.maximumRange", errors);
                RequireRange(shell.ricochetAngle, 0f, 90f, $"Shell '{shell.id}'.ricochetAngle", errors);
                RequireRange(shell.ricochetSpeedMultiplier, float.Epsilon, 1f, $"Shell '{shell.id}'.ricochetSpeedMultiplier", errors);
                RequireRange(shell.ricochetPenetrationMultiplier, float.Epsilon, 1f, $"Shell '{shell.id}'.ricochetPenetrationMultiplier", errors);
                RequireRange(shell.maximumRicochets, 0, 8, $"Shell '{shell.id}'.maximumRicochets", errors);
                if (shell.audio != null)
                {
                    RequireText(shell.audio.penetrationClipId, $"Shell '{shell.id}'.audio.penetrationClipId", errors);
                    RequireText(shell.audio.blockedClipId, $"Shell '{shell.id}'.audio.blockedClipId", errors);
                    RequireText(shell.audio.ricochetClipId, $"Shell '{shell.id}'.audio.ricochetClipId", errors);
                    RequireRange(shell.audio.penetrationVolume, 0f, 1f, $"Shell '{shell.id}'.audio.penetrationVolume", errors);
                    RequireRange(shell.audio.blockedVolume, 0f, 1f, $"Shell '{shell.id}'.audio.blockedVolume", errors);
                    RequireRange(shell.audio.ricochetVolume, 0f, 1f, $"Shell '{shell.id}'.audio.ricochetVolume", errors);
                    RequireRange(shell.audio.pitch, 0.25f, 3f, $"Shell '{shell.id}'.audio.pitch", errors);
                }
            }

            foreach (WeaponDefinitionJson weapon in definitions.Weapons)
            {
                ValidateCommon(weapon.schemaVersion, weapon.id, weapon.displayName, "Weapon", allIds, errors);
                weaponIds.Add(weapon.id ?? string.Empty);
                RequirePositive(weapon.caliberMm, $"Weapon '{weapon.id}'.caliberMm", errors);
                RequirePositive(weapon.reloadSeconds, $"Weapon '{weapon.id}'.reloadSeconds", errors);
                RequireRange(weapon.magazineSize, 1, 100, $"Weapon '{weapon.id}'.magazineSize", errors);
                RequireNonNegative(weapon.shotIntervalSeconds, $"Weapon '{weapon.id}'.shotIntervalSeconds", errors);
                if (string.IsNullOrWhiteSpace(weapon.shellId) || !shellIds.Contains(weapon.shellId))
                {
                    errors.Add($"Weapon '{weapon.id}' references missing shell '{weapon.shellId}'.");
                }
            }

            foreach (TankDefinitionJson tank in definitions.Tanks)
            {
                ValidateCommon(tank.schemaVersion, tank.id, tank.displayName, "Tank", allIds, errors);
                if (HasParentheticalSuffix(tank.displayName))
                {
                    errors.Add(
                        $"Tank '{tank.id}'.displayName must contain only the main player-facing designation without a parenthetical variant suffix.");
                }
                RequirePositive(tank.maxHitPoints, $"Tank '{tank.id}'.maxHitPoints", errors);
                RequirePositive(tank.massTonnes, $"Tank '{tank.id}'.massTonnes", errors);
                RequirePositive(tank.enginePowerHp, $"Tank '{tank.id}'.enginePowerHp", errors);
                if (!Enum.TryParse(tank.vehicleClass, false, out VehicleClass _))
                {
                    errors.Add($"Tank '{tank.id}' has unknown vehicleClass '{tank.vehicleClass}'.");
                }
                if (!Enum.TryParse(tank.nation, false, out TankNation _))
                {
                    errors.Add($"Tank '{tank.id}' has unknown nation '{tank.nation}'.");
                }
                RequireRange(tank.tier, 1, 10, $"Tank '{tank.id}'.tier", errors);
                if (string.IsNullOrWhiteSpace(tank.weaponId) || !weaponIds.Contains(tank.weaponId))
                {
                    errors.Add($"Tank '{tank.id}' references missing weapon '{tank.weaponId}'.");
                }

                bool hasAudio = HasAudio(tank.audio);
                if (tank.availableInGame && !hasAudio)
                {
                    errors.Add($"Tank '{tank.id}' is available in game but is missing audio.");
                }
                else if (hasAudio)
                {
                    RequireText(tank.audio.engineClipId, $"Tank '{tank.id}'.audio.engineClipId", errors);
                    RequireText(tank.audio.tracksClipId, $"Tank '{tank.id}'.audio.tracksClipId", errors);
                    RequireText(tank.audio.shotClipId, $"Tank '{tank.id}'.audio.shotClipId", errors);
                    RequireText(tank.audio.reloadClipId, $"Tank '{tank.id}'.audio.reloadClipId", errors);
                    RequireText(tank.audio.turretClipId, $"Tank '{tank.id}'.audio.turretClipId", errors);
                    RequireText(tank.audio.destructionClipId, $"Tank '{tank.id}'.audio.destructionClipId", errors);
                    RequireRange(tank.audio.engineVolume, 0f, 1f, $"Tank '{tank.id}'.audio.engineVolume", errors);
                    RequireRange(tank.audio.tracksVolume, 0f, 1f, $"Tank '{tank.id}'.audio.tracksVolume", errors);
                    RequireRange(tank.audio.shotVolume, 0f, 1f, $"Tank '{tank.id}'.audio.shotVolume", errors);
                    RequireRange(tank.audio.reloadVolume, 0f, 1f, $"Tank '{tank.id}'.audio.reloadVolume", errors);
                    RequireRange(tank.audio.turretVolume, 0f, 1f, $"Tank '{tank.id}'.audio.turretVolume", errors);
                    RequireRange(tank.audio.destructionVolume, 0f, 1f, $"Tank '{tank.id}'.audio.destructionVolume", errors);
                    RequireRange(tank.audio.enginePitch, 0.25f, 3f, $"Tank '{tank.id}'.audio.enginePitch", errors);
                    RequireRange(tank.audio.tracksPitch, 0.25f, 3f, $"Tank '{tank.id}'.audio.tracksPitch", errors);
                    RequireRange(tank.audio.shotPitch, 0.25f, 3f, $"Tank '{tank.id}'.audio.shotPitch", errors);
                    RequireRange(tank.audio.reloadPitch, 0.25f, 3f, $"Tank '{tank.id}'.audio.reloadPitch", errors);
                    RequireRange(tank.audio.turretPitch, 0.25f, 3f, $"Tank '{tank.id}'.audio.turretPitch", errors);
                    RequireRange(tank.audio.destructionPitch, 0.25f, 3f, $"Tank '{tank.id}'.audio.destructionPitch", errors);
                }

                if (tank.mobility == null)
                {
                    errors.Add($"Tank '{tank.id}' is missing mobility.");
                }
                else
                {
                    RequireNonNegative(tank.mobility.maxForwardSpeed, $"Tank '{tank.id}'.mobility.maxForwardSpeed", errors);
                    RequireNonNegative(tank.mobility.maxReverseSpeed, $"Tank '{tank.id}'.mobility.maxReverseSpeed", errors);
                    RequireNonNegative(tank.mobility.acceleration, $"Tank '{tank.id}'.mobility.acceleration", errors);
                    RequireNonNegative(tank.mobility.groundResistance, $"Tank '{tank.id}'.mobility.groundResistance", errors);
                    RequireNonNegative(tank.mobility.braking, $"Tank '{tank.id}'.mobility.braking", errors);
                    RequireNonNegative(tank.mobility.hullTurnSpeed, $"Tank '{tank.id}'.mobility.hullTurnSpeed", errors);
                    RequireNonNegative(tank.mobility.turretTurnSpeed, $"Tank '{tank.id}'.mobility.turretTurnSpeed", errors);
                }

                if (tank.armor == null)
                {
                    errors.Add($"Tank '{tank.id}' is missing armor.");
                }
                else
                {
                    RequireNonNegative(tank.armor.front, $"Tank '{tank.id}'.armor.front", errors);
                    RequireNonNegative(tank.armor.left, $"Tank '{tank.id}'.armor.left", errors);
                    RequireNonNegative(tank.armor.right, $"Tank '{tank.id}'.armor.right", errors);
                    RequireNonNegative(tank.armor.rear, $"Tank '{tank.id}'.armor.rear", errors);
                }

                if (tank.vision == null)
                {
                    errors.Add($"Tank '{tank.id}' is missing vision.");
                }
                else
                {
                    RequirePositive(tank.vision.viewRange, $"Tank '{tank.id}'.vision.viewRange", errors);
                    RequireRange(tank.vision.stationaryConcealment, 0f, 0.95f, $"Tank '{tank.id}'.vision.stationaryConcealment", errors);
                    RequireRange(tank.vision.movementRevealPenalty, 0f, 0.95f, $"Tank '{tank.id}'.vision.movementRevealPenalty", errors);
                    RequireRange(tank.vision.firingRevealPenalty, 0f, 0.95f, $"Tank '{tank.id}'.vision.firingRevealPenalty", errors);
                    RequireNonNegative(tank.vision.firingRevealDuration, $"Tank '{tank.id}'.vision.firingRevealDuration", errors);
                    RequirePositive(tank.vision.guaranteedDetectionRange, $"Tank '{tank.id}'.vision.guaranteedDetectionRange", errors);
                    if (tank.vision.guaranteedDetectionRange > tank.vision.viewRange)
                    {
                        errors.Add($"Tank '{tank.id}'.vision.guaranteedDetectionRange cannot exceed viewRange.");
                    }
                }

                if (tank.gunHandling == null)
                {
                    errors.Add($"Tank '{tank.id}' is missing gunHandling.");
                }
                else
                {
                    RequireNonNegative(tank.gunHandling.minimumDispersionDegrees, $"Tank '{tank.id}'.gunHandling.minimumDispersionDegrees", errors);
                    RequirePositive(tank.gunHandling.maximumDispersionDegrees, $"Tank '{tank.id}'.gunHandling.maximumDispersionDegrees", errors);
                    RequirePositive(tank.gunHandling.aimingTimeSeconds, $"Tank '{tank.id}'.gunHandling.aimingTimeSeconds", errors);
                    RequireNonNegative(tank.gunHandling.movementDispersionDegrees, $"Tank '{tank.id}'.gunHandling.movementDispersionDegrees", errors);
                    RequireNonNegative(tank.gunHandling.hullTraverseDispersionDegrees, $"Tank '{tank.id}'.gunHandling.hullTraverseDispersionDegrees", errors);
                    RequireNonNegative(tank.gunHandling.turretTraverseDispersionDegrees, $"Tank '{tank.id}'.gunHandling.turretTraverseDispersionDegrees", errors);
                    RequireNonNegative(tank.gunHandling.shotDispersionDegrees, $"Tank '{tank.id}'.gunHandling.shotDispersionDegrees", errors);
                    if (tank.gunHandling.maximumDispersionDegrees < tank.gunHandling.minimumDispersionDegrees)
                    {
                        errors.Add($"Tank '{tank.id}'.gunHandling.maximumDispersionDegrees cannot be below minimumDispersionDegrees.");
                    }
                }
            }

            HashSet<string> referencedWeapons = definitions.Tanks
                .Select(item => item.weaponId ?? string.Empty)
                .ToHashSet(StringComparer.Ordinal);
            foreach (string unused in weaponIds.Where(id => !referencedWeapons.Contains(id)))
                errors.Add($"Weapon '{unused}' is not referenced by any tank.");

            HashSet<string> referencedShells = definitions.Weapons
                .Select(item => item.shellId ?? string.Empty)
                .ToHashSet(StringComparer.Ordinal);
            foreach (string unused in shellIds.Where(id => !referencedShells.Contains(id)))
                errors.Add($"Shell '{unused}' is not referenced by any weapon.");

            return errors.ToArray();
        }

        private static void ValidateCommon(
            int schemaVersion,
            string id,
            string displayName,
            string type,
            HashSet<string> allIds,
            List<string> errors)
        {
            if (schemaVersion != 1)
            {
                errors.Add($"{type} '{id}' uses unsupported schemaVersion {schemaVersion}.");
            }

            if (!IsValidId(id))
            {
                errors.Add($"{type} has invalid id '{id}'. Use lowercase snake_case.");
            }
            else if (!allIds.Add(id))
            {
                errors.Add($"Duplicate definition id '{id}'.");
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                errors.Add($"{type} '{id}' has an empty displayName.");
            }
        }

        private static bool IsValidId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id[0] < 'a' || id[0] > 'z')
            {
                return false;
            }

            foreach (char character in id)
            {
                if ((character < 'a' || character > 'z') &&
                    (character < '0' || character > '9') &&
                    character != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasParentheticalSuffix(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return false;
            }

            string trimmed = displayName.TrimEnd();
            return trimmed.EndsWith(")", StringComparison.Ordinal) &&
                   trimmed.LastIndexOf(" (", StringComparison.Ordinal) >= 0;
        }

        private static bool HasAudio(TankAudioJson audio)
        {
            return audio != null &&
                   (!string.IsNullOrWhiteSpace(audio.engineClipId) ||
                    !string.IsNullOrWhiteSpace(audio.tracksClipId) ||
                    !string.IsNullOrWhiteSpace(audio.shotClipId) ||
                    !string.IsNullOrWhiteSpace(audio.reloadClipId) ||
                    !string.IsNullOrWhiteSpace(audio.turretClipId) ||
                    !string.IsNullOrWhiteSpace(audio.destructionClipId));
        }

        private static void RequireText(string value, string field, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{field} must not be empty.");
            }
        }

        private static void RequirePositive(float value, string field, List<string> errors)
        {
            if (!IsFinite(value) || value <= 0f)
            {
                errors.Add($"{field} must be finite and greater than zero.");
            }
        }

        private static void RequirePositive(int value, string field, List<string> errors)
        {
            if (value <= 0)
            {
                errors.Add($"{field} must be greater than zero.");
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

    public static class CombatDefinitionImporter
    {
        public const string DefinitionsRoot = "Assets/Game/GameData/Definitions";
        public const string GeneratedRoot = "Assets/Game/GameData/Generated";
        public const string DatabasePath = GeneratedRoot + "/CombatDatabase.asset";

        [MenuItem("Local Tanks/Data/Reimport Combat Definitions")]
        public static void Reimport()
        {
            CombatDefinitionSet source = LoadAndValidate();
            EnsureFolder(GeneratedRoot);
            EnsureFolder(GeneratedRoot + "/Tanks");
            EnsureFolder(GeneratedRoot + "/Weapons");
            EnsureFolder(GeneratedRoot + "/Shells");

            Dictionary<string, ShellDefinition> shells = source.Shells
                .OrderBy(item => item.id, StringComparer.Ordinal)
                .ToDictionary(item => item.id, ImportShell, StringComparer.Ordinal);
            Dictionary<string, WeaponDefinition> weapons = source.Weapons
                .OrderBy(item => item.id, StringComparer.Ordinal)
                .ToDictionary(item => item.id, item => ImportWeapon(item, shells[item.shellId]), StringComparer.Ordinal);
            Dictionary<string, TankDefinition> tanks = source.Tanks
                .OrderBy(item => item.id, StringComparer.Ordinal)
                .ToDictionary(item => item.id, item => ImportTank(item, weapons[item.weaponId]), StringComparer.Ordinal);

            DeleteStaleGeneratedAssets(GeneratedRoot + "/Shells", shells.Values.Select(AssetDatabase.GetAssetPath));
            DeleteStaleGeneratedAssets(GeneratedRoot + "/Weapons", weapons.Values.Select(AssetDatabase.GetAssetPath));
            DeleteStaleGeneratedAssets(GeneratedRoot + "/Tanks", tanks.Values.Select(AssetDatabase.GetAssetPath));

            CombatDatabase database = LoadOrCreate<CombatDatabase>(DatabasePath);
            database.shells = shells.Values.ToArray();
            database.weapons = weapons.Values.ToArray();
            database.tanks = tanks.Values.ToArray();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            TankBalanceDocumentGenerator.Write(source);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Local Tanks: imported {tanks.Count} tank(s), {weapons.Count} weapon(s), and {shells.Count} shell(s).");
        }

        private static void DeleteStaleGeneratedAssets(string folder, IEnumerable<string> retainedPaths)
        {
            HashSet<string> retained = new HashSet<string>(retainedPaths, StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!retained.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
        }

        [MenuItem("Local Tanks/Data/Validate Combat Definitions")]
        public static void ValidateMenu()
        {
            CombatDefinitionSet source = LoadAndValidate();
            Debug.Log($"Local Tanks: combat definitions are valid ({source.Tanks.Length} tank(s), {source.Weapons.Length} weapon(s), {source.Shells.Length} shell(s)).");
        }

        public static CombatDefinitionSet LoadAndValidate()
        {
            TankDefinitionJson[] tanks = ApplyDerivedVision(LoadAll<TankDefinitionJson>(
                DefinitionsRoot + "/Tanks", "viewRange", "guaranteedDetectionRange"));
            WeaponDefinitionJson[] weapons = LoadAll<WeaponDefinitionJson>(DefinitionsRoot + "/Weapons");
            ShellDefinitionJson[] shells = LoadAll<ShellDefinitionJson>(DefinitionsRoot + "/Shells");
            CombatDefinitionSet source = new CombatDefinitionSet(tanks, weapons, shells);
            string[] errors = CombatDefinitionValidator.Validate(source);
            if (errors.Length > 0)
            {
                throw new InvalidDataException("Combat definition validation failed:\n- " + string.Join("\n- ", errors));
            }

            return source;
        }

        /// <summary>
        /// View range and guaranteed detection are derived, not authored: the vehicle class decides the
        /// range and the guarantee is a fixed share of it (ADR-064). Computing them here means the validator,
        /// the generated asset and every consumer read one value that comes from one place — the table — and a
        /// hand-written range cannot creep back into the data.
        /// </summary>
        private static TankDefinitionJson[] ApplyDerivedVision(TankDefinitionJson[] tanks)
        {
            foreach (TankDefinitionJson tank in tanks)
            {
                if (tank.vision == null)
                {
                    continue;
                }

                // The class travels as text and is validated on its own; when it cannot be read there is no row
                // to look up, so the range stays unset and the validator reports the class itself.
                if (!Enum.TryParse(tank.vehicleClass, false, out VehicleClass vehicleClass))
                {
                    continue;
                }

                tank.vision.viewRange = VisionRangeTable.UnitsFor(vehicleClass);
                tank.vision.guaranteedDetectionRange =
                    VisionRangeTable.GuaranteedDetectionUnits(tank.vision.viewRange);
            }

            return tanks;
        }

        /// <summary>
        /// Loads every definition in a directory. A field that must no longer be authored is named by the
        /// caller and rejected by its text, because <see cref="JsonUtility"/> ignores unknown members: without
        /// this check a restored field would simply be ignored and the value would look derived while the file
        /// still claimed to set it.
        /// </summary>
        private static T[] LoadAll<T>(string assetDirectory, params string[] forbiddenFields)
        {
            string absoluteDirectory = Path.GetFullPath(assetDirectory);
            if (!Directory.Exists(absoluteDirectory))
            {
                throw new DirectoryNotFoundException($"Combat definition directory was not found: {assetDirectory}");
            }

            List<T> items = new List<T>();
            foreach (string file in Directory.GetFiles(absoluteDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.Ordinal))
            {
                string json = File.ReadAllText(file);
                foreach (string field in forbiddenFields)
                {
                    if (json.Contains($"\"{field}\""))
                    {
                        throw new InvalidDataException(
                            $"'{file}' declares '{field}', which is derived from the vehicle class. " +
                            "Remove the field: its value comes from VisionRangeTable (see Docs/SPRINT_009.md, стадия 0).");
                    }
                }

                T item;
                try
                {
                    item = JsonUtility.FromJson<T>(json);
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException($"Invalid JSON in '{file}'.", exception);
                }

                if (item == null)
                {
                    throw new InvalidDataException($"JSON file '{file}' did not produce a definition.");
                }

                items.Add(item);
            }

            return items.ToArray();
        }

        private static ShellDefinition ImportShell(ShellDefinitionJson source)
        {
            string path = $"{GeneratedRoot}/Shells/{source.id}.asset";
            ShellDefinition target = LoadOrCreate<ShellDefinition>(path);
            target.id = source.id;
            target.displayName = source.displayName;
            target.damage = source.damage;
            target.penetration = source.penetration;
            target.speed = source.speed;
            target.radius = source.radius;
            target.lifetimeSeconds = source.lifetimeSeconds;
            target.maximumRange = source.maximumRange;
            target.ricochetAngle = source.ricochetAngle;
            target.ricochetSpeedMultiplier = source.ricochetSpeedMultiplier;
            target.ricochetPenetrationMultiplier = source.ricochetPenetrationMultiplier;
            target.maximumRicochets = source.maximumRicochets;
            target.audio = source.audio == null
                ? null
                : new ShellAudioProfile
                {
                    penetrationClipId = source.audio.penetrationClipId,
                    blockedClipId = source.audio.blockedClipId,
                    ricochetClipId = source.audio.ricochetClipId,
                    penetrationVolume = source.audio.penetrationVolume,
                    blockedVolume = source.audio.blockedVolume,
                    ricochetVolume = source.audio.ricochetVolume,
                    pitch = source.audio.pitch
                };
            EditorUtility.SetDirty(target);
            return target;
        }

        private static WeaponDefinition ImportWeapon(WeaponDefinitionJson source, ShellDefinition shell)
        {
            string path = $"{GeneratedRoot}/Weapons/{source.id}.asset";
            WeaponDefinition target = LoadOrCreate<WeaponDefinition>(path);
            target.id = source.id;
            target.displayName = source.displayName;
            target.caliberMm = source.caliberMm;
            target.reloadSeconds = source.reloadSeconds;
            target.magazineSize = source.magazineSize;
            target.shotIntervalSeconds = source.shotIntervalSeconds;
            target.shell = shell;
            EditorUtility.SetDirty(target);
            return target;
        }

        private static TankDefinition ImportTank(TankDefinitionJson source, WeaponDefinition weapon)
        {
            string path = $"{GeneratedRoot}/Tanks/{source.id}.asset";
            TankDefinition target = LoadOrCreate<TankDefinition>(path);
            target.id = source.id;
            target.displayName = source.displayName;
            target.vehicleClass = Enum.Parse<VehicleClass>(source.vehicleClass, false);
            target.nation = Enum.Parse<TankNation>(source.nation, false);
            target.tier = source.tier;
            target.availableInGame = source.availableInGame;
            target.maxHitPoints = source.maxHitPoints;
            target.massTonnes = source.massTonnes;
            target.enginePowerHp = source.enginePowerHp;
            target.weapon = weapon;
            target.maxForwardSpeed = source.mobility.maxForwardSpeed;
            target.maxReverseSpeed = source.mobility.maxReverseSpeed;
            target.acceleration = source.mobility.acceleration;
            target.groundResistance = source.mobility.groundResistance;
            target.braking = source.mobility.braking;
            target.hullTurnSpeed = source.mobility.hullTurnSpeed;
            target.turretTurnSpeed = source.mobility.turretTurnSpeed;
            target.minimumDispersionDegrees = source.gunHandling.minimumDispersionDegrees;
            target.maximumDispersionDegrees = source.gunHandling.maximumDispersionDegrees;
            target.aimingTimeSeconds = source.gunHandling.aimingTimeSeconds;
            target.movementDispersionDegrees = source.gunHandling.movementDispersionDegrees;
            target.hullTraverseDispersionDegrees = source.gunHandling.hullTraverseDispersionDegrees;
            target.turretTraverseDispersionDegrees = source.gunHandling.turretTraverseDispersionDegrees;
            target.shotDispersionDegrees = source.gunHandling.shotDispersionDegrees;
            target.viewRange = source.vision.viewRange;
            target.stationaryConcealment = source.vision.stationaryConcealment;
            target.movementRevealPenalty = source.vision.movementRevealPenalty;
            target.firingRevealPenalty = source.vision.firingRevealPenalty;
            target.firingRevealDuration = source.vision.firingRevealDuration;
            target.guaranteedDetectionRange = source.vision.guaranteedDetectionRange;
            target.armor = new ArmorProfile(
                source.armor.front,
                source.armor.left,
                source.armor.right,
                source.armor.rear);
            target.audio = source.audio == null ||
                           (string.IsNullOrWhiteSpace(source.audio.engineClipId) &&
                            string.IsNullOrWhiteSpace(source.audio.tracksClipId) &&
                            string.IsNullOrWhiteSpace(source.audio.shotClipId) &&
                            string.IsNullOrWhiteSpace(source.audio.reloadClipId) &&
                            string.IsNullOrWhiteSpace(source.audio.turretClipId) &&
                            string.IsNullOrWhiteSpace(source.audio.destructionClipId))
                ? null
                : new TankAudioProfile
                {
                    engineClipId = source.audio.engineClipId,
                    tracksClipId = source.audio.tracksClipId,
                    shotClipId = source.audio.shotClipId,
                    reloadClipId = source.audio.reloadClipId,
                    turretClipId = source.audio.turretClipId,
                    destructionClipId = source.audio.destructionClipId,
                    engineVolume = source.audio.engineVolume,
                    tracksVolume = source.audio.tracksVolume,
                    shotVolume = source.audio.shotVolume,
                    reloadVolume = source.audio.reloadVolume,
                    turretVolume = source.audio.turretVolume,
                    destructionVolume = source.audio.destructionVolume,
                    enginePitch = source.audio.enginePitch,
                    tracksPitch = source.audio.tracksPitch,
                    shotPitch = source.audio.shotPitch,
                    reloadPitch = source.audio.reloadPitch,
                    turretPitch = source.audio.turretPitch,
                    destructionPitch = source.audio.destructionPitch
                };
            EditorUtility.SetDirty(target);
            return target;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string parent = assetPath.Substring(0, assetPath.LastIndexOf('/'));
            string name = assetPath.Substring(assetPath.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    internal sealed class CombatDefinitionAssetPostprocessor : AssetPostprocessor
    {
        private static bool importScheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (importScheduled ||
                !importedAssets.Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths)
                    .Any(IsCombatJson))
            {
                return;
            }

            importScheduled = true;
            EditorApplication.delayCall += () =>
            {
                importScheduled = false;
                try
                {
                    CombatDefinitionImporter.Reimport();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
        }

        private static bool IsCombatJson(string assetPath)
        {
            return assetPath.StartsWith(CombatDefinitionImporter.DefinitionsRoot + "/", StringComparison.Ordinal) &&
                   assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }
    }
}
