using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Keeps the committed combat VFX profiles honest.
    /// </summary>
    /// <remarks>
    /// The profile file is data, and data goes wrong quietly: a caliber band that leaves a gun without a
    /// look, a surface table that never mentions mud, a wreck flag on a live band. Every check runs over
    /// the committed file itself, and the same list of problems is used by the menu command, by the asset
    /// builder and by an Edit Mode test, so a broken table cannot be discovered only by watching a battle.
    /// </remarks>
    public static class CombatVfxProfileValidator
    {
        public const string SourcePath = CombatVfxCatalog.SourcePath;
        public const string WeaponFolder = "Assets/Game/GameData/Definitions/Weapons";
        public const string TankFolder = "Assets/Game/GameData/Definitions/Tanks";

        [MenuItem("Local Tanks/Data/Validate Combat Vfx Profiles")]
        public static void ValidateMenu()
        {
            CombatVfxCatalog catalog = LoadSource();
            string[] errors = CollectErrors();
            if (errors.Length > 0)
            {
                throw new InvalidDataException(
                    "Combat VFX profiles are invalid:\n- " + string.Join("\n- ", errors));
            }

            Debug.Log($"Local Tanks: combat VFX profiles are valid " +
                      $"({catalog.weaponProfiles.Length} caliber bands, {catalog.movementProfiles.Length} mass bands, " +
                      $"{catalog.damageProfiles.Length} damage bands, {catalog.impactProfiles.Length} impacts, " +
                      $"{catalog.surfaceProfiles.Length} surfaces).");
        }

        public static CombatVfxCatalog LoadSource()
        {
            string path = Path.GetFullPath(SourcePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Combat VFX profile JSON was not found.", SourcePath);
            }

            return CombatVfxCatalog.Parse(File.ReadAllText(path));
        }

        /// <summary>Every problem found in the committed catalog and in the fleet it must cover.</summary>
        public static string[] CollectErrors()
        {
            List<string> errors = new List<string>();
            CombatVfxCatalog catalog = LoadSource();
            errors.AddRange(catalog.Validate());

            foreach (string duplicate in CombatVfxEffects.All()
                         .GroupBy(effect => effect, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1)
                         .Select(group => group.Key))
            {
                errors.Add($"The generated effect list declares '{duplicate}' more than once.");
            }

            if (catalog.weaponProfiles == null || catalog.weaponProfiles.Length == 0)
            {
                return errors.ToArray();
            }

            // A gun whose caliber is zero or missing would silently fall into the first band, which is the
            // smallest effect in the game. That is exactly the kind of mistake that looks like a bug in the
            // visual layer rather than in the gun data.
            foreach ((string weaponId, float caliberMm) in WeaponCalibers())
            {
                if (caliberMm <= 0f)
                {
                    errors.Add($"Weapon '{weaponId}' has no positive caliberMm, so no effect band can be " +
                               "chosen for it.");
                    continue;
                }

                if (catalog.ResolveWeapon(caliberMm) == null)
                {
                    errors.Add($"Weapon '{weaponId}' has caliber {caliberMm:0.#} mm, which resolves to no " +
                               "weapon VFX band.");
                }
            }

            foreach ((string tankId, float massTonnes) in TankMasses())
            {
                if (massTonnes <= 0f)
                {
                    errors.Add($"Tank '{tankId}' has no positive massTonnes, so no movement VFX band can be " +
                               "chosen for it.");
                }
                else if (catalog.ResolveMovement(massTonnes) == null)
                {
                    errors.Add($"Tank '{tankId}' has mass {massTonnes:0.###} t, which resolves to no movement " +
                               "VFX band.");
                }
            }

            return errors.ToArray();
        }

        /// <summary>Caliber of every committed weapon definition, read from the JSON sources.</summary>
        public static IEnumerable<(string WeaponId, float CaliberMm)> WeaponCalibers()
        {
            string folder = Path.GetFullPath(WeaponFolder);
            if (!Directory.Exists(folder))
            {
                yield break;
            }

            foreach (string file in Directory.GetFiles(folder, "*.json").OrderBy(path => path, StringComparer.Ordinal))
            {
                WeaponDefinitionJson weapon = JsonUtility.FromJson<WeaponDefinitionJson>(File.ReadAllText(file));
                if (weapon != null)
                {
                    yield return (weapon.id, weapon.caliberMm);
                }
            }
        }

        /// <summary>Mass of every committed tank definition, read from the JSON sources.</summary>
        public static IEnumerable<(string TankId, float MassTonnes)> TankMasses()
        {
            string folder = Path.GetFullPath(TankFolder);
            if (!Directory.Exists(folder))
            {
                yield break;
            }

            foreach (string file in Directory.GetFiles(folder, "*.json").OrderBy(path => path, StringComparer.Ordinal))
            {
                TankDefinitionJson tank = JsonUtility.FromJson<TankDefinitionJson>(File.ReadAllText(file));
                if (tank != null)
                {
                    yield return (tank.id, tank.massTonnes);
                }
            }
        }
    }

    /// <summary>Revalidates the profiles the moment the committed file changes.</summary>
    internal sealed class CombatVfxProfilePostprocessor : AssetPostprocessor
    {
        private static bool scheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (scheduled || !importedAssets.Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths)
                    .Any(path => path == CombatVfxProfileValidator.SourcePath))
            {
                return;
            }

            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                string[] errors = CombatVfxProfileValidator.CollectErrors();
                if (errors.Length > 0)
                {
                    Debug.LogError("Combat VFX profiles are invalid:\n- " + string.Join("\n- ", errors));
                }
            };
        }
    }
}
