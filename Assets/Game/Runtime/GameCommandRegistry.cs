using System;

namespace LocalTanks
{
    public enum GameCommandGroup
    {
        Battle,
        Interface,
        Development
    }

    [Serializable]
    public sealed class GameCommandInfo
    {
        public string id;
        public string binding;
        public string description;
        public GameCommandGroup group;

        public GameCommandInfo(string commandId, string keyBinding, string commandDescription, GameCommandGroup commandGroup)
        {
            id = commandId;
            binding = keyBinding;
            description = commandDescription;
            group = commandGroup;
        }
    }

    public static class GameCommandRegistry
    {
        private static readonly GameCommandInfo[] RegisteredCommands =
        {
            new GameCommandInfo("move", "W / S", "Движение вперёд и назад", GameCommandGroup.Battle),
            new GameCommandInfo("turn", "A / D", "Поворот корпуса", GameCommandGroup.Battle),
            new GameCommandInfo("aim", "Мышь", "Наведение башни и орудия", GameCommandGroup.Battle),
            new GameCommandInfo("fire", "ЛКМ", "Огонь; удерживание ведёт непрерывную стрельбу", GameCommandGroup.Battle),
            new GameCommandInfo("zoom", "Колесо мыши", "Изменение масштаба камеры", GameCommandGroup.Battle),
            new GameCommandInfo("help", "F1", "Открыть или закрыть эту справку", GameCommandGroup.Interface),
            new GameCommandInfo("bot_selection", "Tab / Shift+Tab", "Следующий или предыдущий живой бот при открытом F4", GameCommandGroup.Interface),
            new GameCommandInfo("close_window", "Esc", "Закрыть верхнее диагностическое окно", GameCommandGroup.Interface),
            new GameCommandInfo("navigation_debug", "F2", "Показать навигационную сетку и маршруты", GameCommandGroup.Development),
            new GameCommandInfo("vision_debug", "F3", "Показать диагностику обнаружения", GameCommandGroup.Development),
            new GameCommandInfo("ai_debug", "F4", "Открыть инспектор ИИ", GameCommandGroup.Development),
            new GameCommandInfo("time_scale", "F5", "Переключить скорость времени ×1 / ×3", GameCommandGroup.Development),
            new GameCommandInfo("map_analysis", "F6", "Показать оценку карты и планы команд", GameCommandGroup.Development)
        };

        public static GameCommandInfo[] Commands => (GameCommandInfo[])RegisteredCommands.Clone();
    }
}
