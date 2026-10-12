using System.Linq;
using LocalTanks.Editor;
using NUnit.Framework;

namespace LocalTanks.Tests
{
    public sealed class TankAudioDefinitionTests
    {
        [Test]
        public void AvailableTierOneTanks_UseTopConfigurationWoTEvents()
        {
            CombatDefinitionSet definitions = CombatDefinitionImporter.LoadAndValidate();
            TankDefinitionJson ms1 = definitions.Tanks.Single(item => item.id == "ms_1");
            TankDefinitionJson leichttraktor = definitions.Tanks.Single(item => item.id == "leichttraktor");

            Assert.That(ms1.audio.engineClipId, Is.EqualTo("engine_eng_gaz_aa_pc"));
            Assert.That(leichttraktor.audio.engineClipId, Is.EqualTo(ms1.audio.engineClipId));
            Assert.That(ms1.audio.shotClipId, Is.EqualTo("gun_wpn_small_pc"));
            Assert.That(leichttraktor.audio.shotClipId, Is.EqualTo("gun_wpn_automatic_pc"));
            Assert.That(leichttraktor.audio.turretClipId, Is.Not.EqualTo(ms1.audio.turretClipId));
            Assert.That(leichttraktor.audio.enginePitch, Is.GreaterThan(ms1.audio.enginePitch));
            Assert.That(leichttraktor.audio.shotPitch, Is.GreaterThan(ms1.audio.shotPitch));
        }

        [Test]
        public void Validator_RequiresAudioForPlayableTank()
        {
            CombatDefinitionSet definitions = CombatDefinitionImporter.LoadAndValidate();
            TankDefinitionJson tank = definitions.Tanks.Single(item => item.id == "ms_1");
            TankAudioJson previous = tank.audio;
            tank.audio = null;

            string[] errors = CombatDefinitionValidator.Validate(definitions);
            tank.audio = previous;

            Assert.That(errors.Any(error => error.Contains("available in game") && error.Contains("missing audio")),
                Is.True);
        }
    }
}
