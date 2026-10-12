using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    public sealed class CombatAiBehaviorAuditTests
    {
        [Test]
        public void Auditor_DetectsRapidStateThrashing()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                transitionWindowSeconds = 3f,
                maximumStateTransitions = 4
            });

            auditor.Record(Sample(0f, CombatAiState.Advance, Vector2.zero));
            auditor.Record(Sample(0.4f, CombatAiState.Engage, Vector2.zero));
            auditor.Record(Sample(0.8f, CombatAiState.Investigate, Vector2.zero));
            auditor.Record(Sample(1.2f, CombatAiState.Reposition, Vector2.zero));
            auditor.Record(Sample(1.6f, CombatAiState.Engage, Vector2.zero));

            Assert.That(auditor.Issues.Count(item => item.Kind == CombatAiAuditIssueKind.StateThrashing),
                Is.EqualTo(1));
        }

        [Test]
        public void Auditor_DetectsMovementStallAndNavigationDeadEnd()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                movementWindowSeconds = 2f,
                minimumMovementDistance = 0.25f,
                navigationDeadEndSeconds = 0.8f
            });

            auditor.Record(Sample(0f, CombatAiState.Advance, Vector2.zero));
            auditor.Record(Sample(1f, CombatAiState.Advance, Vector2.zero));
            auditor.Record(Sample(3.1f, CombatAiState.Advance, Vector2.zero));
            auditor.Record(Sample(4f, CombatAiState.Investigate, Vector2.zero, hasPath: false));
            auditor.Record(Sample(5f, CombatAiState.Investigate, Vector2.zero, hasPath: false));

            Assert.That(auditor.Issues.Any(item => item.Kind == CombatAiAuditIssueKind.MovementStall), Is.True);
            Assert.That(auditor.Issues.Any(item => item.Kind == CombatAiAuditIssueKind.NavigationDeadEnd), Is.True);
        }

        [Test]
        public void Auditor_AcceptsProgressingStableBot()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor();
            for (int index = 0; index < 40; index++)
            {
                auditor.Record(Sample(index * 0.25f, CombatAiState.Advance,
                    new Vector2(index * 0.08f, 0f)));
            }

            Assert.That(auditor.Issues, Is.Empty, auditor.BuildReport(10f));
        }

        [Test]
        public void Auditor_DoesNotReportStallWhileAgentExecutesRecoveryManeuver()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                movementWindowSeconds = 2f,
                minimumMovementDistance = 0.25f
            });
            auditor.Record(Sample(0f, CombatAiState.Engage, Vector2.zero));
            auditor.Record(new CombatAiAuditSample(
                1f, "bot-a", "Tester", CombatAiState.Engage, Vector2.zero,
                true, false, true, false, true, "enemy-a", true,
                FireBlockReason.Aiming, 0, 0, 0f, false, true));
            auditor.Record(new CombatAiAuditSample(
                3.2f, "bot-a", "Tester", CombatAiState.Engage, new Vector2(0.1f, 0f),
                true, false, true, false, true, "enemy-a", true,
                FireBlockReason.Aiming, 0, 0, 5f, false, true));
            auditor.Record(Sample(3.4f, CombatAiState.Engage, new Vector2(0.35f, 0f)));

            Assert.That(auditor.Issues.Any(item => item.Kind == CombatAiAuditIssueKind.MovementStall),
                Is.False);
        }

        [Test]
        public void Auditor_DetectsCirclingWithoutTranslation()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                circlingWindowSeconds = 3f,
                circlingRotationDegrees = 120f
            });
            for (int index = 0; index <= 8; index++)
            {
                auditor.Record(new CombatAiAuditSample(
                    index * 0.5f, "bot-a", "Тестер", CombatAiState.Advance, Vector2.zero,
                    true, false, true, false, true, string.Empty, false,
                    FireBlockReason.NoVisibleTarget, 0, 0, index * 25f));
            }

            Assert.That(auditor.Issues.Any(item => item.Kind == CombatAiAuditIssueKind.Circling), Is.True);
        }

        [Test]
        public void Auditor_TargetChangeRestartsCombatInactivityWindow()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                combatInactivitySeconds = 2f
            });
            auditor.Record(CombatSample(0f, "enemy-a"));
            auditor.Record(CombatSample(1.5f, "enemy-a"));
            auditor.Record(CombatSample(2f, "enemy-b"));
            auditor.Record(CombatSample(3f, "enemy-b"));

            Assert.That(auditor.Issues.Any(item => item.Kind == CombatAiAuditIssueKind.CombatInactivity), Is.False);

            auditor.Record(CombatSample(4.1f, "enemy-b"));
            Assert.That(auditor.Issues.Any(item => item.Kind == CombatAiAuditIssueKind.CombatInactivity), Is.True);
        }

        [Test]
        public void Auditor_TracksProgressingRepositionThroughMovementRulesInsteadOfCombatTimer()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                combatInactivitySeconds = 2f,
                movementWindowSeconds = 10f
            });
            for (int index = 0; index < 5; index++)
            {
                auditor.Record(new CombatAiAuditSample(
                    index,
                    "bot-a",
                    "Тестер",
                    CombatAiState.Reposition,
                    new Vector2(index * 0.2f, 0f),
                    true,
                    false,
                    true,
                    false,
                    true,
                    "enemy-a",
                    true,
                    FireBlockReason.ObstacleInLine,
                    0));
            }

            Assert.That(auditor.Issues.Any(item => item.Kind == CombatAiAuditIssueKind.CombatInactivity), Is.False);

            auditor.Record(CombatSample(4.1f, "enemy-a"));
            auditor.Record(CombatSample(6.2f, "enemy-a"));
            Assert.That(auditor.Issues.Any(item => item.Kind == CombatAiAuditIssueKind.CombatInactivity), Is.True);
        }

        [Test]
        public void Auditor_DetectsCoverCombatTaskThrashing()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                tacticalTransitionWindowSeconds = 4f,
                maximumTacticalTaskTransitions = 4
            });

            auditor.Record(TacticalSample(0f, TacticalMovementPurpose.None));
            auditor.Record(TacticalSample(0.5f, TacticalMovementPurpose.MoveToCover));
            auditor.Record(TacticalSample(1f, TacticalMovementPurpose.None));
            auditor.Record(TacticalSample(1.5f, TacticalMovementPurpose.MoveToCover));
            auditor.Record(TacticalSample(2f, TacticalMovementPurpose.None));

            Assert.That(auditor.Issues.Count(item => item.Kind == CombatAiAuditIssueKind.TacticalTaskThrashing),
                Is.EqualTo(1));
        }

        [Test]
        public void Auditor_DetectsRepeatedFiringLineCancellation()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                firingLineTransitionWindowSeconds = 3f,
                maximumFiringLineTransitions = 3
            });

            auditor.Record(TacticalSample(0f, TacticalMovementPurpose.None));
            auditor.Record(TacticalSample(0.4f, TacticalMovementPurpose.GainFiringLine));
            auditor.Record(TacticalSample(0.8f, TacticalMovementPurpose.None));
            auditor.Record(TacticalSample(1.2f, TacticalMovementPurpose.GainFiringLine));

            Assert.That(auditor.Issues.Count(item => item.Kind == CombatAiAuditIssueKind.FiringLineThrashing),
                Is.EqualTo(1));
        }

        [Test]
        public void Auditor_DoesNotJoinFiringLineTransitionsAcrossTargetChange()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                firingLineTransitionWindowSeconds = 4f,
                maximumFiringLineTransitions = 3
            });

            auditor.Record(TacticalSample(0f, TacticalMovementPurpose.GainFiringLine));
            auditor.Record(TacticalSample(1f, TacticalMovementPurpose.None));
            auditor.Record(TacticalSample(1.2f, TacticalMovementPurpose.GainFiringLine, "enemy-b"));
            auditor.Record(TacticalSample(1.4f, TacticalMovementPurpose.None, "enemy-b"));

            Assert.That(auditor.Issues.Count(item => item.Kind == CombatAiAuditIssueKind.FiringLineThrashing),
                Is.Zero);
        }

        [Test]
        public void Auditor_DoesNotTreatCoverPreemptionAsFiringLineThrashing()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                firingLineTransitionWindowSeconds = 4f,
                maximumFiringLineTransitions = 4
            });

            auditor.Record(TacticalSample(0f, TacticalMovementPurpose.None));
            auditor.Record(TacticalSample(0.5f, TacticalMovementPurpose.GainFiringLine));
            auditor.Record(TacticalSample(1f, TacticalMovementPurpose.None));
            auditor.Record(TacticalSample(1.5f, TacticalMovementPurpose.GainFiringLine));
            auditor.Record(TacticalSample(2f, TacticalMovementPurpose.MoveToCover));

            Assert.That(auditor.Issues.Count(item => item.Kind == CombatAiAuditIssueKind.FiringLineThrashing),
                Is.Zero);
        }

        [Test]
        public void Auditor_DetectsSustainedRearExposureAcrossCombatStateChanges()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                rearExposureAngleDegrees = 110f,
                rearExposureSeconds = 2f
            });

            auditor.Record(RearExposureSample(0f, 155f));
            auditor.Record(RearExposureSample(1f, 150f, CombatAiState.Reposition));
            auditor.Record(RearExposureSample(3.1f, 145f, CombatAiState.Engage));

            Assert.That(auditor.Issues.Count(item => item.Kind == CombatAiAuditIssueKind.RearExposedToVisibleTarget),
                Is.EqualTo(1));
        }

        [Test]
        public void Auditor_DoesNotReportRearExposureWhileBotStopsToAlignHull()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                rearExposureAngleDegrees = 110f,
                rearExposureSeconds = 2f
            });

            auditor.Record(RearExposureSample(0f, 155f, movementPaused: true));
            auditor.Record(RearExposureSample(3f, 145f, movementPaused: true));

            Assert.That(auditor.Issues.Count(item => item.Kind == CombatAiAuditIssueKind.RearExposedToVisibleTarget),
                Is.Zero);
        }

        [Test]
        public void Auditor_DetectsReverseModeThatOutlivesRetreatTask()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                reverseWithoutRetreatSeconds = 1f
            });

            auditor.Record(ReverseSample(0f, TacticalMovementPurpose.DefensiveRetreat));
            auditor.Record(ReverseSample(0.2f, TacticalMovementPurpose.None));
            auditor.Record(ReverseSample(1.3f, TacticalMovementPurpose.None));
            auditor.Record(ReverseSample(8f, TacticalMovementPurpose.None));

            Assert.That(auditor.Issues.Count(item => item.Kind == CombatAiAuditIssueKind.ReverseWithoutRetreatTask),
                Is.EqualTo(1));
        }

        [Test]
        public void Auditor_AllowsShortReverseFiringLineManeuver()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                reverseWithoutRetreatSeconds = 1f
            });

            auditor.Record(ReverseSample(0f, TacticalMovementPurpose.GainFiringLine));
            auditor.Record(ReverseSample(1.5f, TacticalMovementPurpose.GainFiringLine));

            Assert.That(auditor.Issues.Count(item => item.Kind == CombatAiAuditIssueKind.ReverseWithoutRetreatTask),
                Is.Zero);
        }

        [Test]
        public void Auditor_AllowsReversePeekFromCover()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                reverseWithoutRetreatSeconds = 1f
            });

            auditor.Record(ReverseSample(0f, TacticalMovementPurpose.PeekToFire));
            auditor.Record(ReverseSample(1.5f, TacticalMovementPurpose.PeekToFire));

            Assert.That(auditor.Issues.Count(item => item.Kind == CombatAiAuditIssueKind.ReverseWithoutRetreatTask),
                Is.Zero);
        }

        [Test]
        public void Auditor_DetectsDestroyedTankStillClassifiedAsLivingAlly()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor();
            auditor.Record(CloseCombatSample(0f, TacticalMovementPurpose.None,
                LineOfFireBlockType.Ally, true));
            auditor.Record(CloseCombatSample(0.2f, TacticalMovementPurpose.None,
                LineOfFireBlockType.Ally, true));

            Assert.That(auditor.Issues.Count(item =>
                item.Kind == CombatAiAuditIssueKind.DestroyedTankMisclassifiedAsAlly), Is.EqualTo(1));
        }

        [Test]
        public void Auditor_DetectsCloseTargetThatTurretCannotTrack()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                closeRangeTrackingFailureSeconds = 2f
            });
            auditor.Record(CloseCombatSample(0f));
            auditor.Record(CloseCombatSample(1f));
            auditor.Record(CloseCombatSample(3.1f));

            Assert.That(auditor.Issues.Count(item =>
                item.Kind == CombatAiAuditIssueKind.CloseRangeTrackingFailure), Is.EqualTo(1));
        }

        [Test]
        public void Auditor_AcceptsCloseTargetWhileBotBreaksOrbit()
        {
            CombatAiBehaviorAuditor auditor = new CombatAiBehaviorAuditor(new CombatAiAuditRules
            {
                closeRangeTrackingFailureSeconds = 1f
            });
            auditor.Record(CloseCombatSample(0f, TacticalMovementPurpose.BreakCloseOrbit));
            auditor.Record(CloseCombatSample(2f, TacticalMovementPurpose.BreakCloseOrbit));

            Assert.That(auditor.Issues.Any(item =>
                item.Kind == CombatAiAuditIssueKind.CloseRangeTrackingFailure), Is.False);
        }

        private static CombatAiAuditSample Sample(
            float time,
            CombatAiState state,
            Vector2 position,
            bool hasPath = true)
        {
            return new CombatAiAuditSample(
                time,
                "bot-a",
                "Тестер",
                state,
                position,
                true,
                false,
                true,
                false,
                hasPath,
                string.Empty,
                false,
                FireBlockReason.NoVisibleTarget,
                0);
        }

        private static CombatAiAuditSample CombatSample(float time, string targetId)
        {
            return new CombatAiAuditSample(
                time, "bot-a", "Тестер", CombatAiState.Engage, Vector2.zero,
                true, true, false, false, false, targetId, true,
                FireBlockReason.Aiming, 0);
        }

        private static CombatAiAuditSample TacticalSample(
            float time,
            TacticalMovementPurpose purpose,
            string targetId = "enemy-a")
        {
            return new CombatAiAuditSample(
                time, "bot-a", "Тестер", CombatAiState.Engage, new Vector2(time, 0f),
                true, false, true, false, true, targetId, true,
                FireBlockReason.Aiming, 0, 0, 0f, false, false, purpose);
        }

        private static CombatAiAuditSample RearExposureSample(
            float time,
            float angle,
            CombatAiState state = CombatAiState.Engage,
            bool movementPaused = false)
        {
            return new CombatAiAuditSample(
                time, "bot-a", "Тестер", state, new Vector2(time, 0f),
                true, movementPaused, true, false, true, "enemy-a", true,
                FireBlockReason.Aiming, 0, 0, 180f, false, false,
                TacticalMovementPurpose.None, angle, false, 1f);
        }

        private static CombatAiAuditSample ReverseSample(float time, TacticalMovementPurpose purpose)
        {
            return new CombatAiAuditSample(
                time, "bot-a", "Тестер", CombatAiState.Advance, new Vector2(time, 0f),
                true, false, true, false, true, string.Empty, false,
                FireBlockReason.NoVisibleTarget, 0, 0, 180f, false, false,
                purpose, 0f, true);
        }

        private static CombatAiAuditSample CloseCombatSample(
            float time,
            TacticalMovementPurpose purpose = TacticalMovementPurpose.None,
            LineOfFireBlockType blockType = LineOfFireBlockType.None,
            bool blockerDestroyed = false)
        {
            return new CombatAiAuditSample(
                time, "bot-a", "Tester", CombatAiState.Engage, Vector2.zero,
                true, true, false, false, false, "enemy-a", true,
                blockerDestroyed ? FireBlockReason.AllyInLine : FireBlockReason.Aiming,
                0, 0, 0f, false, false, purpose, 25f, false, 0f,
                blockType, blockerDestroyed, 2.2f, 25f, 30f);
        }
    }
}
