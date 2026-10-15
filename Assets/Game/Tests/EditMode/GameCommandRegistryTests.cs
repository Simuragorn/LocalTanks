using System.Linq;
using NUnit.Framework;

namespace LocalTanks.Tests
{
    public sealed class GameCommandRegistryTests
    {
        [Test]
        public void Registry_HasUniqueIdsAndRequiredDiagnosticsCommands()
        {
            GameCommandInfo[] commands = GameCommandRegistry.Commands;

            Assert.That(commands, Is.Not.Empty);
            Assert.That(commands.Select(item => item.id).Distinct().Count(), Is.EqualTo(commands.Length));
            Assert.That(commands, Has.All.Matches<GameCommandInfo>(item =>
                !string.IsNullOrWhiteSpace(item.binding) && !string.IsNullOrWhiteSpace(item.description)));
            Assert.That(commands.Single(item => item.id == "help").binding, Is.EqualTo("F1"));
            Assert.That(commands.Single(item => item.id == "map_analysis").binding, Is.EqualTo("F6"));
            Assert.That(commands.Single(item => item.id == "performance").binding, Is.EqualTo("F8"));
            Assert.That(commands.Single(item => item.id == "bot_selection").binding, Does.Contain("Tab"));
            Assert.That(commands.Single(item => item.id == "reload").binding, Is.EqualTo("R"));
            Assert.That(commands.Single(item => item.id == "aim_mode").binding, Is.EqualTo("Shift"));
            Assert.That(commands.Single(item => item.id == "duel_restart").binding, Is.EqualTo("Ctrl+R"));
            Assert.That(commands.Select(item => item.binding).Distinct().Count(), Is.EqualTo(commands.Length));
        }

        [Test]
        public void Commands_ReturnsACopy()
        {
            GameCommandInfo[] first = GameCommandRegistry.Commands;
            GameCommandInfo[] second = GameCommandRegistry.Commands;

            Assert.That(first, Is.Not.SameAs(second));
            first[0] = null;
            Assert.That(GameCommandRegistry.Commands[0], Is.Not.Null);
        }
    }
}
