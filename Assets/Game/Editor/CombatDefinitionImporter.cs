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
        public string weaponId;
        public MobilityJson mobility;
        public ArmorJson armor;
        public VisionJson vision;
        public GunHandlingJson gunHandling;
    }

    [Serializable]
    public sealed class WeaponDefinitionJson
    {
        public int schemaVersion;
        public string id;
        public string displayName;
        public float reloadSeconds;
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
            }

            foreach (WeaponDefinitionJson weapon in definitions.Weapons)
            {
                ValidateCommon(weapon.schemaVersion, weapon.id, weapon.displayName, "Weapon", allIds, errors);
                weaponIds.Add(weapon.id ?? string.Empty);
                RequirePositive(weapon.reloadSeconds, $"Weapon '{weapon.id}'.reloadSeconds", errors);
                if (string.IsNullOrWhiteSpace(weapon.shellId) || !shellIds.Contains(weapon.shellId))
                {
                    errors.Add($"Weapon '{weapon.id}' references missing shell '{weapon.shellId}'.");
                }
            }

            foreach (TankDefinitionJson tank in definitions.Tanks)
            {
                ValidateCommon(tank.schemaVersion, tank.id, tank.displayName, "Tank", allIds, errors);
                RequirePositive(tank.maxHitPoints, $"Tank '{tank.id}'.maxHitPoints", errors);
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

        [MenuItem("Local Tanks/Data/Validate Combat Definitions")]
        public static void ValidateMenu()
        {
            CombatDefinitionSet source = LoadAndValidate();
            Debug.Log($"Local Tanks: combat definitions are valid ({source.Tanks.Length} tank(s), {source.Weapons.Length} weapon(s), {source.Shells.Length} shell(s)).");
        }

        public static CombatDefinitionSet LoadAndValidate()
        {
            TankDefinitionJson[] tanks = LoadAll<TankDefinitionJson>(DefinitionsRoot + "/Tanks");
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

        private static T[] LoadAll<T>(string assetDirectory)
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
            EditorUtility.SetDirty(target);
            return target;
        }

        private static WeaponDefinition ImportWeapon(WeaponDefinitionJson source, ShellDefinition shell)
        {
            string path = $"{GeneratedRoot}/Weapons/{source.id}.asset";
            WeaponDefinition target = LoadOrCreate<WeaponDefinition>(path);
            target.id = source.id;
            target.displayName = source.displayName;
            target.reloadSeconds = source.reloadSeconds;
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
