using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Overrides component fields from the command line, so a parameter can be swept without rebuilding.
    /// <para>
    /// Tuning movement once meant changing a number, rebuilding the scene, running one battle and hoping
    /// the difference was not noise. The attempt to pick an avoidance radius failed exactly that way: the
    /// two runs differed by about as much as two runs of identical code. The cure is to run many values
    /// and several seeds in one process, which needs the values to be settable from outside the build.
    /// </para>
    /// <para>
    /// Fields are named as <c>Component.field</c> or just <c>field</c>, and several may be given:
    /// <c>-localtanksParam NavigationAgent.avoidanceRadius=0.9 avoidanceRadius=1.2</c>. Only serialised
    /// fields of existing components are touched; a name that matches nothing is reported rather than
    /// ignored, because a silent typo would look like a parameter that has no effect.
    /// </para>
    /// </summary>
    public static class LocalTanksOverrides
    {
        /// <summary>Command line switch introducing one override: <c>-localtanksParam a.b=1</c>.</summary>
        public const string SwitchName = "-localtanksParam";

        /// <summary>Command line switch giving the seed for a run: <c>-localtanksSeed 1911</c>.</summary>
        public const string SeedSwitchName = "-localtanksSeed";

        private sealed class Override
        {
            public string TypeName;
            public string FieldName;
            public string Raw;
            public bool Reported;
        }

        private static readonly List<Override> Pending = new List<Override>();
        private static readonly List<Override> Transient = new List<Override>();
        private static readonly HashSet<string> Reported = new HashSet<string>();
        private static bool parsed;

        /// <summary>
        /// Sets a field for everything created from now on, without a command line and without a rebuild.
        /// A sweep calls this before each run, and a tuning slider calls it as it is dragged, which is the
        /// whole point of having a single path for runtime tuning.
        /// </summary>
        public static void SetTransient(string name, float value)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            int dot = name.LastIndexOf('.');
            Transient.RemoveAll(item => string.Equals(item.FieldName, dot > 0 ? name.Substring(dot + 1) : name,
                StringComparison.OrdinalIgnoreCase));
            Transient.Add(new Override
            {
                TypeName = dot > 0 ? name.Substring(0, dot) : null,
                FieldName = dot > 0 ? name.Substring(dot + 1) : name,
                Raw = value.ToString("R", CultureInfo.InvariantCulture)
            });
        }

        /// <summary>Forgets a field set by <see cref="SetTransient"/>.</summary>
        public static void ClearTransient(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            int dot = name.LastIndexOf('.');
            string field = dot > 0 ? name.Substring(dot + 1) : name;
            Transient.RemoveAll(item =>
                string.Equals(item.FieldName, field, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Whether anything will be overridden, from either the command line or a caller.</summary>
        public static bool HasAny => Pending.Count > 0 || Transient.Count > 0;

        /// <summary>Names currently overridden, for a diagnostic display or a log line.</summary>
        public static IEnumerable<string> Active()
        {
            return Pending.Concat(Transient).Select(Describe).Distinct().ToArray();
        }

        /// <summary>Reads the command line once and remembers what was asked for.</summary>
        public static void ParseCommandLine()
        {
            if (parsed) return;
            parsed = true;

            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index], SwitchName, StringComparison.OrdinalIgnoreCase)) continue;
                ParseAssignment(arguments[index + 1]);
            }

            if (Pending.Count > 0)
            {
                Debug.Log($"Parameter overrides requested: " +
                          string.Join(", ", Pending.Select(item => Describe(item))));
            }
        }

        private static void ParseAssignment(string assignment)
        {
            int equals = assignment.IndexOf('=');
            if (equals <= 0 || equals == assignment.Length - 1)
            {
                Debug.LogWarning($"Ignoring override '{assignment}': expected a name=value pair.");
                return;
            }

            string name = assignment.Substring(0, equals);
            string raw = assignment.Substring(equals + 1);
            int dot = name.LastIndexOf('.');
            Pending.Add(new Override
            {
                TypeName = dot > 0 ? name.Substring(0, dot) : null,
                FieldName = dot > 0 ? name.Substring(dot + 1) : name,
                Raw = raw
            });
        }

        private static string Describe(Override item) =>
            string.IsNullOrEmpty(item.TypeName) ? $"{item.FieldName}={item.Raw}" : $"{item.TypeName}.{item.FieldName}={item.Raw}";

        /// <summary>Seed requested for this run, or the given fallback.</summary>
        public static int RequestedSeed(int fallback)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index], SeedSwitchName, StringComparison.OrdinalIgnoreCase)) continue;
                return int.TryParse(arguments[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int value)
                    ? value
                    : fallback;
            }

            return fallback;
        }

        /// <summary>
        /// Applies every requested override to the components in the loaded scenes. Called once the battle
        /// scene is up, before the battle starts, so values are in force from the first frame.
        /// </summary>
        public static void ApplyToLoadedScenes()
        {
            ParseCommandLine();
            if (!HasAny) return;

            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int applied = ApplyTo(behaviours);
            foreach (Override item in Pending.Where(entry => !entry.Reported))
            {
                Debug.LogWarning($"Override '{Describe(item)}' matched no serialised field on any " +
                                 "loaded component; check the name.");
            }

            Debug.Log($"Applied {applied} parameter override(s).");
        }

        /// <summary>
        /// Applies overrides to the given components and returns how many fields were set. Components that
        /// appear later in the battle call this for themselves, so late spawns honour the same values.
        /// </summary>
        public static int ApplyTo(IEnumerable<MonoBehaviour> behaviours)
        {
            ParseCommandLine();
            if (!HasAny) return 0;

            int applied = 0;
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null) continue;
                applied += ApplyToComponent(behaviour);
            }

            return applied;
        }

        /// <summary>Applies the overrides that name this component's type.</summary>
        public static int ApplyToComponent(MonoBehaviour behaviour)
        {
            if (behaviour == null || !HasAny) return 0;

            Type type = behaviour.GetType();
            int applied = 0;
            foreach (Override item in Pending.Concat(Transient).ToArray())
            {
                if (!string.IsNullOrEmpty(item.TypeName) &&
                    !string.Equals(item.TypeName, type.Name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.TypeName, type.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                FieldInfo field = FindField(type, item.FieldName);
                if (field == null) continue;
                if (!TryConvert(item.Raw, field.FieldType, out object value))
                {
                    WarnOnce($"Override '{Describe(item)}' does not fit {type.Name}.{field.Name} " +
                             $"({field.FieldType.Name}).");
                    continue;
                }

                item.Reported = true;
                field.SetValue(behaviour, value);
                applied++;
            }

            return applied;
        }

        /// <summary>
        /// Finds a field by name, including private serialised ones, walking base types. Private fields are
        /// exactly where tuning constants live, so refusing to touch them would make this useless.
        /// </summary>
        private static FieldInfo FindField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && !field.IsInitOnly) return field;
            }

            return null;
        }

        private static bool TryConvert(string raw, Type target, out object value)
        {
            value = null;
            try
            {
                if (target == typeof(float)) value = float.Parse(raw, CultureInfo.InvariantCulture);
                else if (target == typeof(int)) value = int.Parse(raw, CultureInfo.InvariantCulture);
                else if (target == typeof(bool)) value = bool.Parse(raw);
                else if (target == typeof(string)) value = raw;
                else if (target == typeof(double)) value = double.Parse(raw, CultureInfo.InvariantCulture);
                else if (target.IsEnum) value = Enum.Parse(target, raw, ignoreCase: true);
                else if (target == typeof(Vector2))
                {
                    string[] parts = raw.Split(',');
                    if (parts.Length != 2) return false;
                    value = new Vector2(
                        float.Parse(parts[0], CultureInfo.InvariantCulture),
                        float.Parse(parts[1], CultureInfo.InvariantCulture));
                }
                else return false;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void WarnOnce(string message)
        {
            if (Reported.Add(message)) Debug.LogWarning(message);
        }
    }
}
