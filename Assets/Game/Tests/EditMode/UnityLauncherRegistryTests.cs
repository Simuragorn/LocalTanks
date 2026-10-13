using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Keeps the operation registry of the Unity launcher honest.
    /// </summary>
    /// <remarks>
    /// <c>Tools/Unity/unity-operations.json</c> is the single place that says what an agent may ask Unity to
    /// do and whether the answer needs a graphics device; <c>Tools/Unity/Invoke-UnityBackground.ps1</c> reads
    /// it to pick the mode. Both facts are invisible to the compiler, so they are checked here instead: a
    /// renamed editor method, a new diagnostic that nobody registered, or an image-producing operation marked
    /// headless all fail this suite rather than a run at the end of a long task.
    /// </remarks>
    public sealed class UnityLauncherRegistryTests
    {
        private const string RegistryRelativePath = "Tools/Unity/unity-operations.json";

        /// <summary>
        /// Diagnostics belong in the registry because they are what an agent runs to look at something; a
        /// diagnostic nobody can name from the launcher is a diagnostic nobody runs.
        /// </summary>
        private const string DiagnosticsMenuRoot = "Local Tanks/Diagnostics/";

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".exr" };

        [Serializable]
        private sealed class OperationRegistry
        {
            public int schemaVersion;
            public OperationEntry[] operations;
        }

        [Serializable]
        private sealed class OperationEntry
        {
            public string name;
            public string kind;
            public string testPlatform;
            public string executeMethod;
            public bool requiresGraphics;
            public int timeoutSeconds;
            public string description;
            public string[] outputs;
        }

        private static string ProjectRoot
        {
            get { return Path.GetDirectoryName(Application.dataPath); }
        }

        private static string RegistryPath
        {
            get { return Path.Combine(ProjectRoot, RegistryRelativePath); }
        }

        private static OperationEntry[] LoadOperations()
        {
            Assert.That(File.Exists(RegistryPath), Is.True,
                $"The launcher registry is missing: {RegistryRelativePath}. Agents would have no way to name a Unity operation.");

            OperationRegistry registry = JsonUtility.FromJson<OperationRegistry>(File.ReadAllText(RegistryPath));
            Assert.That(registry, Is.Not.Null, $"{RegistryRelativePath} could not be parsed.");
            Assert.That(registry.operations, Is.Not.Null.And.Not.Empty, $"{RegistryRelativePath} lists no operations.");
            Assert.That(registry.schemaVersion, Is.GreaterThan(0),
                $"{RegistryRelativePath} does not declare a schema version, so a reader cannot tell which shape it has.");
            return registry.operations;
        }

        [Test]
        public void EveryOperation_HasAUniqueNameAndAKnownKind()
        {
            OperationEntry[] operations = LoadOperations();

            string[] duplicates = operations
                .GroupBy(entry => entry.name)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            Assert.That(duplicates, Is.Empty, "These operation names appear more than once: " + string.Join(", ", duplicates));

            foreach (OperationEntry entry in operations)
            {
                Assert.That(entry.name, Is.Not.Null.And.Not.Empty, "An operation has no name.");
                Assert.That(new[] { "Tests", "ExecuteMethod" }, Does.Contain(entry.kind),
                    $"Operation '{entry.name}' has kind '{entry.kind}', which the launcher does not understand.");
                Assert.That(entry.description, Is.Not.Null.And.Not.Empty,
                    $"Operation '{entry.name}' has no description, so nothing tells an agent what it does.");
                Assert.That(entry.timeoutSeconds, Is.GreaterThan(0),
                    $"Operation '{entry.name}' has no timeout; a hung run would hold the queue forever.");

                if (entry.kind == "Tests")
                {
                    Assert.That(new[] { "EditMode", "PlayMode" }, Does.Contain(entry.testPlatform),
                        $"Operation '{entry.name}' is a test run with platform '{entry.testPlatform}'.");
                    Assert.That(entry.executeMethod, Is.Null.Or.Empty,
                        $"Operation '{entry.name}' is a test run and must not name an editor entry point.");
                }
                else
                {
                    Assert.That(entry.executeMethod, Is.Not.Null.And.Not.Empty,
                        $"Operation '{entry.name}' has no editor entry point.");
                }
            }
        }

        [Test]
        public void EveryExecuteMethod_ResolvesToAPublicStaticParameterlessMethod()
        {
            OperationEntry[] operations = LoadOperations();
            List<string> broken = new List<string>();

            foreach (OperationEntry entry in operations.Where(item => item.kind == "ExecuteMethod"))
            {
                if (ResolveEntryPoint(entry.executeMethod) == null)
                {
                    broken.Add($"{entry.name} -> {entry.executeMethod}");
                }
            }

            Assert.That(broken, Is.Empty,
                "These registry entries point at an editor method that does not exist as a public static " +
                "method without parameters, which is what -executeMethod accepts: " + string.Join("; ", broken));
        }

        [Test]
        public void EveryOperationThatWritesAnImage_RequiresAGraphicsDevice()
        {
            OperationEntry[] operations = LoadOperations();
            List<string> wrong = new List<string>();

            foreach (OperationEntry entry in operations)
            {
                bool writesImage = (entry.outputs ?? new string[0]).Any(IsImagePath);
                if (writesImage && !entry.requiresGraphics)
                {
                    wrong.Add(entry.name);
                }
            }

            Assert.That(wrong, Is.Empty,
                "A graphics device is the only thing that can draw a picture, so an operation that writes an " +
                "image cannot be registered as headless: " + string.Join(", ", wrong) +
                ". Its run would produce an empty file and look like a rendering bug.");
        }

        [Test]
        public void EveryGraphicalOperation_DeclaresAFileForItsEvidence()
        {
            OperationEntry[] operations = LoadOperations();
            List<string> silent = operations
                .Where(entry => entry.requiresGraphics)
                .Where(entry => entry.outputs == null || entry.outputs.Length == 0)
                .Select(entry => entry.name)
                .ToList();

            Assert.That(silent, Is.Empty,
                "A graphical run costs a graphics device and a queued slot, so it has to leave a file behind " +
                "that the caller can read: " + string.Join(", ", silent));
        }

        [Test]
        public void BothTestPlatforms_AreRegisteredHeadless()
        {
            OperationEntry[] operations = LoadOperations();

            foreach (string platform in new[] { "EditMode", "PlayMode" })
            {
                OperationEntry entry = operations.FirstOrDefault(item => item.name == platform + "Tests");
                Assert.That(entry, Is.Not.Null, $"The registry has no '{platform}Tests' entry.");
                Assert.That(entry.kind, Is.EqualTo("Tests"), $"'{platform}Tests' is not a test run.");
                Assert.That(entry.testPlatform, Is.EqualTo(platform), $"'{platform}Tests' runs the wrong platform.");
                Assert.That(entry.requiresGraphics, Is.False,
                    $"'{platform}Tests' must run headless: a suite proves what it proves from data, logs and " +
                    "telemetry, and a graphics device only makes it slower and puts a window in the way.");
            }
        }

        [Test]
        public void EveryDiagnosticsMenuItem_IsRegistered()
        {
            OperationEntry[] operations = LoadOperations();
            HashSet<string> registered = new HashSet<string>(
                operations.Where(entry => entry.kind == "ExecuteMethod").Select(entry => entry.executeMethod),
                StringComparer.Ordinal);

            List<string> found = new List<string>();
            List<string> missing = new List<string>();
            foreach (MethodInfo method in AllMethods())
            {
                string path = MenuItemPath(method);
                if (path == null || !path.StartsWith(DiagnosticsMenuRoot, StringComparison.Ordinal))
                {
                    continue;
                }

                found.Add(path);
                string entryPoint = method.DeclaringType.FullName + "." + method.Name;
                if (!registered.Contains(entryPoint))
                {
                    missing.Add($"{path} ({entryPoint})");
                }
            }

            Assert.That(found, Is.Not.Empty,
                "No diagnostic menu item was found at all, which means this check stopped seeing the editor menu " +
                "and no longer protects anything.");
            Assert.That(missing, Is.Empty,
                "These diagnostics are not in " + RegistryRelativePath + ", so an agent cannot run them from the " +
                "launcher and does not know whether they need a graphics device: " + string.Join("; ", missing));
        }

        private static bool IsImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string extension = Path.GetExtension(path);
            return extension != null &&
                   ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private static MethodInfo ResolveEntryPoint(string entryPoint)
        {
            if (string.IsNullOrWhiteSpace(entryPoint))
            {
                return null;
            }

            int split = entryPoint.LastIndexOf('.');
            if (split <= 0)
            {
                return null;
            }

            string typeName = entryPoint.Substring(0, split);
            string methodName = entryPoint.Substring(split + 1);
#pragma warning disable UAC0005
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
#pragma warning restore UAC0005
            foreach (Assembly assembly in assemblies)
            {
                Type type = assembly.GetType(typeName, false);
                if (type == null)
                {
                    continue;
                }

                MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (method != null)
                {
                    return method;
                }
            }

            return null;
        }

        private static IEnumerable<MethodInfo> AllMethods()
        {
            // The analyzer would rather see TypeCache, but it indexes types by attribute, not methods, and the
            // coverage check below is exactly a question about methods. AppDomain is the only enumeration
            // that can answer it.
#pragma warning disable UAC0005
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
#pragma warning restore UAC0005
            foreach (Assembly assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException error)
                {
                    types = error.Types.Where(type => type != null).ToArray();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                  BindingFlags.Static | BindingFlags.Instance |
                                                  BindingFlags.DeclaredOnly);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    foreach (MethodInfo method in methods)
                    {
                        yield return method;
                    }
                }
            }
        }

        /// <summary>
        /// Path of a <see cref="MenuItem"/> attribute, read reflectively because the field name is not part of
        /// any public API. Returning null for an attribute that exists would make the coverage check pass by
        /// accident, so the caller asserts that something was found at all.
        /// </summary>
        private static string MenuItemPath(MethodInfo method)
        {
            object attribute = method.GetCustomAttributes(typeof(MenuItem), false).FirstOrDefault();
            if (attribute == null)
            {
                return null;
            }

            FieldInfo field = typeof(MenuItem).GetField("menuItem",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                return field.GetValue(attribute) as string;
            }

            PropertyInfo property = typeof(MenuItem).GetProperty("menuItem",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return property != null ? property.GetValue(attribute, null) as string : null;
        }
    }
}
