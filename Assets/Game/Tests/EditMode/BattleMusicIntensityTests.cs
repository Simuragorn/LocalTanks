using NUnit.Framework;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The music layer must not depend on the model or on chance. These tests pin the whole decision:
    /// thresholds, the hysteresis between them, the minimum mood time, the decay window and the fact
    /// that two identical event sequences produce an identical answer.
    /// </summary>
    public sealed class BattleMusicIntensityTests
    {
        [Test]
        public void SameEvents_ProduceTheSameDecision()
        {
            BattleMusicIntensity first = new BattleMusicIntensity();
            BattleMusicIntensity second = new BattleMusicIntensity();
            first.Reset(0f);
            second.Reset(0f);

            foreach (BattleMusicIntensity model in new[] { first, second })
            {
                model.Register(BattleAudioEventKind.Shot, 7f);
                model.Register(BattleAudioEventKind.Penetrated, 7.5f);
                model.Register(BattleAudioEventKind.Destroyed, 8f);
                model.Update(9f);
                model.Update(14f);
            }

            Assert.That(second.Mood, Is.EqualTo(first.Mood));
            Assert.That(second.Intensity, Is.EqualTo(first.Intensity).Within(0.0001f));
            Assert.That(second.LastReason, Is.EqualTo(first.LastReason));
        }

        [Test]
        public void Intensive_RequiresCrossingTheThreshold()
        {
            BattleMusicIntensity model = new BattleMusicIntensity();
            model.Reset(0f);
            model.Register(BattleAudioEventKind.Destroyed, 6f, 1f);
            model.Register(BattleAudioEventKind.Destroyed, 6f, 1f);

            Assert.That(model.Update(6f), Is.True);
            Assert.That(model.Mood, Is.EqualTo(BattleMusicMood.Intensive));
            Assert.That(model.Intensity, Is.GreaterThanOrEqualTo(BattleMusicIntensity.IntensiveThreshold));
        }

        [Test]
        public void QuietBattle_StaysRelaxed()
        {
            BattleMusicIntensity model = new BattleMusicIntensity();
            model.Reset(0f);
            model.Register(BattleAudioEventKind.Shot, 8f);

            Assert.That(model.Update(9f), Is.False);
            Assert.That(model.Mood, Is.EqualTo(BattleMusicMood.Relaxed));
        }

        [Test]
        public void MinimumMoodTime_HoldsTheCalmLayerThroughASuddenBurst()
        {
            BattleMusicIntensity model = new BattleMusicIntensity();
            model.Reset(0f);
            for (int index = 0; index < 4; index++)
            {
                model.Register(BattleAudioEventKind.Destroyed, 0f, 1f);
            }

            Assert.That(model.Update(1f), Is.False, "A burst cannot flip the music within the minimum mood time.");
            Assert.That(model.Mood, Is.EqualTo(BattleMusicMood.Relaxed));

            Assert.That(model.Update(6.5f), Is.True);
            Assert.That(model.Mood, Is.EqualTo(BattleMusicMood.Intensive));
        }

        [Test]
        public void Hysteresis_KeepsTheTenseLayerAboveTheRelaxedThreshold()
        {
            BattleMusicIntensity model = new BattleMusicIntensity();
            model.Reset(0f);
            model.Register(BattleAudioEventKind.Destroyed, 10f, 1f);
            model.Register(BattleAudioEventKind.Destroyed, 10f, 1f);

            Assert.That(model.Update(10f), Is.True);
            Assert.That(model.Mood, Is.EqualTo(BattleMusicMood.Intensive));

            // 1.0 lies between the relaxed and the intensive threshold, so the layer must not fall back.
            Assert.That(model.Update(16f), Is.False);
            Assert.That(model.Intensity, Is.LessThan(BattleMusicIntensity.IntensiveThreshold));
            Assert.That(model.Intensity, Is.GreaterThan(BattleMusicIntensity.RelaxedThreshold));
            Assert.That(model.Mood, Is.EqualTo(BattleMusicMood.Intensive));

            Assert.That(model.Update(18f), Is.True, "Below the relaxed threshold the calm layer returns.");
            Assert.That(model.Mood, Is.EqualTo(BattleMusicMood.Relaxed));
        }

        [Test]
        public void Events_LeaveTheWindowAndStopCounting()
        {
            BattleMusicIntensity model = new BattleMusicIntensity();
            model.Reset(0f);
            model.Register(BattleAudioEventKind.Destroyed, 0f, 3f);

            Assert.That(model.Evaluate(0f), Is.EqualTo(3f).Within(0.0001f));
            Assert.That(model.Evaluate(BattleMusicIntensity.WindowSeconds + 1f), Is.Zero);
            model.Update(BattleMusicIntensity.WindowSeconds + 1f);
            Assert.That(model.EventCount, Is.Zero, "Events older than the window are dropped, not accumulated.");
        }

        [Test]
        public void WeightlessEvent_DoesNotChangeAnything()
        {
            BattleMusicIntensity model = new BattleMusicIntensity();
            model.Reset(0f);

            model.Register(BattleAudioEventKind.Shot, 5f, 0f);

            Assert.That(model.EventCount, Is.Zero);
            Assert.That(model.Evaluate(5f), Is.Zero);
            Assert.That(model.LastReason, Does.Contain("без веса"));
        }

        [Test]
        public void Kinds_AreOrderedByHowMuchTheyTenseTheMusic()
        {
            Assert.That(BattleMusicIntensity.WeightFor(BattleAudioEventKind.Destroyed),
                Is.GreaterThan(BattleMusicIntensity.WeightFor(BattleAudioEventKind.Penetrated)));
            Assert.That(BattleMusicIntensity.WeightFor(BattleAudioEventKind.Penetrated),
                Is.GreaterThan(BattleMusicIntensity.WeightFor(BattleAudioEventKind.Shot)));
            Assert.That(BattleMusicIntensity.WeightFor(BattleAudioEventKind.Shot), Is.GreaterThan(0f));
        }

        [Test]
        public void Reset_StartsANewBattleCalm()
        {
            BattleMusicIntensity model = new BattleMusicIntensity();
            model.Register(BattleAudioEventKind.Destroyed, 10f, 1.5f);
            model.Register(BattleAudioEventKind.Destroyed, 10f, 1.5f);
            model.Update(12f);
            Assert.That(model.Mood, Is.EqualTo(BattleMusicMood.Intensive));

            model.Reset(20f);

            Assert.That(model.Mood, Is.EqualTo(BattleMusicMood.Relaxed));
            Assert.That(model.EventCount, Is.Zero);
            Assert.That(model.Intensity, Is.Zero);
        }
    }
}
