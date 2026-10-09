using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace LocalTanks.Editor
{
    public static class TankBalanceDocumentGenerator
    {
        public const string DocumentPath = "Docs/TANK_BALANCE.md";

        [MenuItem("Local Tanks/Data/Update Tank Balance Document")]
        public static void WriteCurrent()
        {
            Write(CombatDefinitionImporter.LoadAndValidate());
        }

        public static void Write(CombatDefinitionSet definitions)
        {
            string content = GenerateMarkdown(definitions);
            string absolutePath = Path.GetFullPath(DocumentPath);
            if (File.Exists(absolutePath) && Normalize(File.ReadAllText(absolutePath)) == Normalize(content))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? ".");
            File.WriteAllText(absolutePath, content, new UTF8Encoding(false));
        }

        public static string GenerateMarkdown(CombatDefinitionSet definitions)
        {
            StringBuilder output = new StringBuilder();
            output.AppendLine("# Балансные параметры техники");
            output.AppendLine();
            output.AppendLine("Этот документ автоматически собирается из боевых JSON-данных. Исходники находятся в `Assets/Game/GameData/Definitions`; вручную числа в этой таблице не редактируются. После изменения или добавления техники выполните `Local Tanks > Data > Reimport Combat Definitions` — импортёр обновит и игровые ассеты, и этот документ.");
            output.AppendLine();
            output.AppendLine("Перед изменением параметров сравнивайте машину со всем актуальным набором ниже. Исторические характеристики задают ориентир, но HP, урон, перезарядка, обзор, маскировка, динамика и сведение могут отклоняться ради понятного баланса.");
            output.AppendLine();

            TankDefinitionJson[] tanks = definitions.Tanks.OrderBy(item => item.id, StringComparer.Ordinal).ToArray();
            output.AppendLine("## Роль, прочность и вооружение");
            output.AppendLine();
            output.AppendLine("| Техника | Нация | Класс | В игре | HP | Орудие | Перезарядка, с | Урон | Пробитие, мм |");
            output.AppendLine("|---|---|---|:---:|---:|---|---:|---:|---:|");
            foreach (TankDefinitionJson tank in tanks)
            {
                WeaponDefinitionJson weapon = definitions.Weapons.Single(item => item.id == tank.weaponId);
                ShellDefinitionJson shell = definitions.Shells.Single(item => item.id == weapon.shellId);
                output.AppendLine($"| {Escape(tank.displayName)} (`{tank.id}`) | {tank.nation} | {tank.vehicleClass} | {(tank.availableInGame ? "да" : "нет")} | {tank.maxHitPoints} | {Escape(weapon.displayName)} | {F(weapon.reloadSeconds)} | {shell.damage} | {F(shell.penetration)} |");
            }

            output.AppendLine();
            output.AppendLine("## Мобильность и броня");
            output.AppendLine();
            output.AppendLine("| Техника | Вперёд | Назад | Разгон | Сопротивление | Торможение | Поворот корпуса, °/с | Поворот башни, °/с | Броня Л/Б/К, мм |");
            output.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach (TankDefinitionJson tank in tanks)
            {
                output.AppendLine($"| {Escape(tank.displayName)} | {F(tank.mobility.maxForwardSpeed)} | {F(tank.mobility.maxReverseSpeed)} | {F(tank.mobility.acceleration)} | {F(tank.mobility.groundResistance)} | {F(tank.mobility.braking)} | {F(tank.mobility.hullTurnSpeed)} | {F(tank.mobility.turretTurnSpeed)} | {F(tank.armor.front)} / {F(Math.Max(tank.armor.left, tank.armor.right))} / {F(tank.armor.rear)} |");
            }

            output.AppendLine();
            output.AppendLine("## Сведение и разброс");
            output.AppendLine();
            output.AppendLine("Все значения разброса указаны как максимальное угловое отклонение в градусах. При полном сведении `0°` траектория идеально совпадает с направлением ствола. Штраф башни намеренно меньше штрафов движения и поворота корпуса.");
            output.AppendLine();
            output.AppendLine("| Техника | Мин., ° | Макс., ° | Сведение, с | Движение, ° | Корпус, ° | Башня, ° | После выстрела, ° |");
            output.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (TankDefinitionJson tank in tanks)
            {
                GunHandlingJson gun = tank.gunHandling;
                output.AppendLine($"| {Escape(tank.displayName)} | {F(gun.minimumDispersionDegrees)} | {F(gun.maximumDispersionDegrees)} | {F(gun.aimingTimeSeconds)} | {F(gun.movementDispersionDegrees)} | {F(gun.hullTraverseDispersionDegrees)} | {F(gun.turretTraverseDispersionDegrees)} | {F(gun.shotDispersionDegrees)} |");
            }

            output.AppendLine();
            output.AppendLine("## Снаряды");
            output.AppendLine();
            output.AppendLine("| Техника | Снаряд | Скорость | Радиус | Дальность | Угол рикошета | Скорость после рикошета | Пробитие после рикошета | Рикошеты |");
            output.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (TankDefinitionJson tank in tanks)
            {
                WeaponDefinitionJson weapon = definitions.Weapons.Single(item => item.id == tank.weaponId);
                ShellDefinitionJson shell = definitions.Shells.Single(item => item.id == weapon.shellId);
                output.AppendLine($"| {Escape(tank.displayName)} | {Escape(shell.displayName)} | {F(shell.speed)} | {F(shell.radius)} | {F(shell.maximumRange)} | {F(shell.ricochetAngle)}° | {F(shell.ricochetSpeedMultiplier)} | {F(shell.ricochetPenetrationMultiplier)} | {shell.maximumRicochets} |");
            }

            output.AppendLine();
            output.AppendLine("## Обзор и маскировка");
            output.AppendLine();
            output.AppendLine("| Техника | Обзор | Маскировка стоя | Штраф движения | Штраф выстрела | Раскрытие, с | Гарантированное обнаружение |");
            output.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (TankDefinitionJson tank in tanks)
            {
                VisionJson vision = tank.vision;
                output.AppendLine($"| {Escape(tank.displayName)} | {F(vision.viewRange)} | {F(vision.stationaryConcealment)} | {F(vision.movementRevealPenalty)} | {F(vision.firingRevealPenalty)} | {F(vision.firingRevealDuration)} | {F(vision.guaranteedDetectionRange)} |");
            }

            output.AppendLine();
            output.AppendLine("## Правило изменения баланса");
            output.AppendLine();
            output.AppendLine("1. Сначала определить соседей новой или изменяемой машины по классу, уровню защиты, калибру и роли.");
            output.AppendLine("2. Сопоставить её минимум с двумя существующими машинами из таблиц, если набор это позволяет.");
            output.AppendLine("3. Исторические данные использовать для относительного характера машины, а игровые отклонения явно фиксировать в `Docs/TANK_REFERENCE.md`.");
            output.AppendLine("4. После правки JSON переимпортировать определения и запустить Edit Mode тест, проверяющий актуальность этого документа.");
            return output.ToString();
        }

        public static string Normalize(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").TrimEnd() + "\n";
        }

        private static string F(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|");
        }
    }
}
