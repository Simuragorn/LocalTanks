using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LocalTanks;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    public static class TacticalMapDescriptorImporter
    {
        public const string SourcePath = "Assets/Game/GameData/Maps/river_crossing.json";
        public const string AssetPath = "Assets/Game/GameData/Generated/Tactical/river_crossing_tactical.asset";
        public const string DocumentPath = "Docs/MAP_TACTICS.md";

        [MenuItem("Local Tanks/Data/Rebuild Tactical Map Analysis")]
        public static TacticalMapDescriptorAsset Reimport()
        {
            TacticalMapSource source = LoadSource();
            TacticalMapDescriptor descriptor = TacticalMapAnalyzer.Analyze(source);
            EnsureFolder("Assets/Game/GameData/Generated/Tactical");
            TacticalMapDescriptorAsset asset = AssetDatabase.LoadAssetAtPath<TacticalMapDescriptorAsset>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TacticalMapDescriptorAsset>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            asset.descriptor = descriptor;
            string[] errors = asset.Validate();
            if (errors.Length > 0)
                throw new InvalidDataException("Tactical map asset validation failed:\n- " + string.Join("\n- ", errors));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            WriteIfChanged(DocumentPath, BuildDocument(descriptor));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return asset;
        }

        [MenuItem("Local Tanks/Data/Validate Tactical Map Analysis")]
        public static void ValidateMenu()
        {
            TacticalMapDescriptorAsset asset = Reimport();
            Debug.Log($"Local Tanks: tactical map '{asset.descriptor.mapId}' is valid " +
                      $"({asset.descriptor.lanes.Length} lanes, {asset.descriptor.sourceHash.Substring(0, 12)})." );
        }

        public static TacticalMapSource LoadSource()
        {
            string path = Path.GetFullPath(SourcePath);
            if (!File.Exists(path)) throw new FileNotFoundException("Battle map JSON was not found.", SourcePath);
            return TacticalMapSource.Parse(File.ReadAllText(path));
        }

        public static string BuildDocument(TacticalMapDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            StringBuilder text = new StringBuilder();
            text.AppendLine("# Тактический анализ карты").AppendLine();
            text.AppendLine("Этот документ генерируется из JSON карты тем же анализатором, данные которого будут передаваться локальной LLM. Ручное редактирование не требуется.").AppendLine();
            text.AppendLine($"- Карта: `{descriptor.mapId}` — {descriptor.displayName}; сцена `{descriptor.sceneId}`");
            text.AppendLine($"- Анализатор: версия {descriptor.analyzerVersion}");
            text.AppendLine($"- Хэш источника: `{descriptor.sourceHash}`");
            text.AppendLine($"- Размер: {descriptor.width}×{descriptor.height} клеток, клетка {Format(descriptor.cellSize)} м");
            text.AppendLine($"- Базы: A {FormatCell(descriptor.baseA)}, B {FormatCell(descriptor.baseB)}");
            text.AppendLine($"- Найдено направлений: {descriptor.lanes?.Length ?? 0}").AppendLine();
            text.AppendLine("| ID | Рабочее имя | Переправа | Длина | Ширина | Укрытия | Маскировка | Прострел | ЛТ | СТ | ТТ | ПТ | САУ |");
            text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (TacticalLaneDescriptor lane in descriptor.lanes ?? Array.Empty<TacticalLaneDescriptor>())
            {
                TacticalClassSuitability score = lane.suitability ?? new TacticalClassSuitability();
                text.Append("| `").Append(lane.id).Append("` | ").Append(lane.provisionalName)
                    .Append(" | ").Append(FormatCell(lane.crossingCell))
                    .Append(" | ").Append(Format(lane.lengthWorld)).Append(" м")
                    .Append(" | ").Append(Format(lane.averageWidthWorld)).Append(" м")
                    .Append(" | ").Append(Percent(lane.hardCoverDensity))
                    .Append(" | ").Append(Percent(lane.concealmentDensity))
                    .Append(" | ").Append(Format(lane.averageSightlineWorld)).Append(" м")
                    .Append(" | ").Append(Percent(score.lightTank))
                    .Append(" | ").Append(Percent(score.mediumTank))
                    .Append(" | ").Append(Percent(score.heavyTank))
                    .Append(" | ").Append(Percent(score.tankDestroyer))
                    .Append(" | ").Append(Percent(score.artillery)).AppendLine(" |");
            }

            text.AppendLine();
            foreach (TacticalLaneDescriptor lane in descriptor.lanes ?? Array.Empty<TacticalLaneDescriptor>())
            {
                text.AppendLine($"## {lane.provisionalName} (`{lane.id}`)").AppendLine();
                text.AppendLine($"- Вход A: {FormatCell(lane.entryA)}; оборона A: {FormatCell(lane.defenseA)}.");
                text.AppendLine($"- Вход B: {FormatCell(lane.entryB)}; оборона B: {FormatCell(lane.defenseB)}.");
                text.AppendLine($"- Обязательная переправа: {(lane.hasMandatoryCrossing ? "да" : "нет")}; узких точек: {lane.chokePointCount}.");
                text.AppendLine($"- Средняя стоимость движения: {Format(lane.averageNavigationCost)}; максимальный прострел: {Format(lane.maximumSightlineWorld)} м.");
                string terrain = string.Join(", ", (lane.terrainShares ?? Array.Empty<TacticalTerrainShare>())
                    .OrderByDescending(item => item.share)
                    .Select(item => $"{item.terrainKind} {Percent(item.share)}"));
                text.AppendLine($"- Покрытие маршрута: {terrain}.");
                string connections = lane.connections == null || lane.connections.Length == 0
                    ? "нет близких поперечных связей"
                    : string.Join(", ", lane.connections.Select(item =>
                        $"{item.targetLaneId} через {Format(item.transferDistanceWorld)} м"));
                text.AppendLine($"- Переброска: {connections}.").AppendLine();
            }

            return text.ToString().TrimEnd() + Environment.NewLine;
        }

        private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Percent(float value) => Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
        private static string FormatCell(Vector2Int cell) => $"({cell.x}, {cell.y})";

        private static void WriteIfChanged(string path, string content)
        {
            string absolute = Path.GetFullPath(path);
            if (File.Exists(absolute) && File.ReadAllText(absolute) == content) return;
            File.WriteAllText(absolute, content, new UTF8Encoding(false));
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            int separator = assetPath.LastIndexOf('/');
            string parent = assetPath.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, assetPath.Substring(separator + 1));
        }
    }

    internal sealed class TacticalMapDescriptorPostprocessor : AssetPostprocessor
    {
        private static bool scheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (scheduled || !importedAssets.Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths)
                    .Any(path => path == TacticalMapDescriptorImporter.SourcePath))
                return;
            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                try { TacticalMapDescriptorImporter.Reimport(); }
                catch (Exception exception) { Debug.LogException(exception); }
            };
        }
    }
}
