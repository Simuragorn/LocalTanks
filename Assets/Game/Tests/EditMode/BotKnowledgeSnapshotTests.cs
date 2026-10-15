using System.Linq;
using NUnit.Framework;

namespace LocalTanks.Tests
{
    public sealed class BotKnowledgeSnapshotTests
    {
        [Test]
        public void Validator_RejectsCurrentInformationForRememberedEnemy()
        {
            BotKnowledgeSnapshot snapshot = ValidSnapshot();
            snapshot.enemies[0].contactState = BotContactState.Remembered;
            snapshot.enemies[0].positionIsCurrent = true;
            snapshot.enemies[0].currentHitPoints = 50;

            string[] errors = BotKnowledgeSnapshotValidator.Validate(snapshot);

            Assert.That(errors.Any(item => item.Contains("current position")), Is.True);
            Assert.That(errors.Any(item => item.Contains("current hit points")), Is.True);
        }

        [Test]
        public void SnapshotHash_IsStableAndChangesWithKnowledge()
        {
            BotKnowledgeSnapshot snapshot = ValidSnapshot();

            string first = BotKnowledgeSnapshotHasher.Compute(snapshot);
            snapshot.snapshotHash = first;
            string second = BotKnowledgeSnapshotHasher.Compute(snapshot);
            snapshot.enemyAlive--;
            string changed = BotKnowledgeSnapshotHasher.Compute(snapshot);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(changed, Is.Not.EqualTo(first));
        }

        [Test]
        public void Fallback_UsesTeamAssignmentAndNeverInventsReference()
        {
            BotKnowledgeSnapshot snapshot = ValidSnapshot();
            snapshot.plan.assignedTask = TeamPlanTask.Scout.ToString();
            snapshot.plan.assignedLaneId = "lane_south";

            BotStrategicDecision decision = BotStrategicFallback.Create(snapshot);
            string[] errors = BotStrategicDecisionValidator.Validate(decision, Context(snapshot));

            Assert.That(decision.intent, Is.EqualTo(BotStrategicIntentType.ScoutLane));
            Assert.That(decision.laneId, Is.EqualTo("lane_south"));
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void DecisionContract_RejectsUnsupportedIntentAndNumericEnumText()
        {
            BotKnowledgeSnapshot snapshot = ValidSnapshot();
            BotStrategicDecision decision = BotStrategicFallback.Create(snapshot);
            decision.intent = BotStrategicIntentType.AngleArmor;
            BotDecisionValidationContext context = Context(snapshot);

            string[] errors = BotStrategicDecisionValidator.Validate(decision, context);
            bool parsed = BotStrategicDecisionSchema.TryParse(
                "{\"schemaVersion\":1,\"requestId\":\"snapshot-1\",\"botId\":\"bot-a\",\"intent\":\"1\",\"laneId\":\"\",\"zoneId\":\"\",\"targetId\":\"\",\"allyId\":\"\",\"desiredDistance\":10,\"urgency\":0.5,\"durationSeconds\":10,\"explanation\":\"x\",\"message\":\"\",\"chatChannel\":\"None\"}",
                out _, out _);

            Assert.That(errors, Has.Some.Contains("not available"));
            Assert.That(parsed, Is.False);
            string schema = BotStrategicDecisionSchema.BuildJsonSchema(context.allowedIntents);
            Assert.That(schema, Does.Contain("\"AdvanceLane\""));
            Assert.That(schema, Does.Not.Contain("\"AngleArmor\""));
        }

        [Test]
        public void DeploymentPolicy_RejectsChangingLaneBeforeEnemyContact()
        {
            BotKnowledgeSnapshot snapshot = ValidSnapshot();
            snapshot.enemies[0].contactState = BotContactState.Unknown;
            snapshot.enemies[0].hasKnownPosition = false;
            snapshot.enemies[0].positionIsCurrent = false;
            snapshot.enemies[0].currentHitPoints = -1;
            snapshot.knownTargetIds = new string[0];
            BotStrategicDecision decision = BotStrategicFallback.Create(snapshot);
            decision.laneId = "lane_north";

            string[] errors = BotStrategicDeploymentPolicy.Validate(decision, snapshot);

            Assert.That(errors, Has.Some.Contains("must keep deployment lane"));
            snapshot.enemies[0].contactState = BotContactState.Visible;
            snapshot.enemies[0].hasKnownPosition = true;
            Assert.That(BotStrategicDeploymentPolicy.Validate(decision, snapshot), Is.Empty,
                "A real contact should allow tactical redeployment.");
        }

        private static BotKnowledgeSnapshot ValidSnapshot()
        {
            return new BotKnowledgeSnapshot
            {
                snapshotId = "snapshot-1",
                battleId = "battle-1",
                botId = "bot-a",
                teamId = "TeamA",
                alliedAlive = 7,
                enemyAlive = 7,
                identity = new BotIdentity
                {
                    entityId = "bot-a",
                    nickname = "Дозор",
                    personality = new BotPersonalityProfile { aggression = 0.5f, discipline = 0.7f }
                },
                mentalState = new BotMentalState(),
                self = new BotVehicleKnowledge
                {
                    tankId = "tank-a",
                    currentHitPoints = 100,
                    maximumHitPoints = 100,
                    preferredDistance = 10f,
                    viewRange = 30f
                },
                allies = new[]
                {
                    new BotAllyKnowledge { memberId = "ally-a", isAlive = true }
                },
                enemies = new[]
                {
                    new BotEnemyKnowledge
                    {
                        memberId = "enemy-a",
                        isAlive = true,
                        contactState = BotContactState.Visible,
                        hasKnownPosition = true,
                        positionIsCurrent = true,
                        currentHitPoints = 80,
                        maximumHitPoints = 100
                    }
                },
                bases = new BotBaseKnowledge[0],
                plan = new BotPlanKnowledge
                {
                    assignedLaneId = "lane_center",
                    assignedTask = TeamPlanTask.Assault.ToString()
                },
                knownLaneIds = new[] { "lane_north", "lane_center", "lane_south" },
                knownZoneIds = new string[0],
                knownTargetIds = new[] { "enemy-a" },
                allyIds = new[] { "ally-a" }
            };
        }

        private static BotDecisionValidationContext Context(BotKnowledgeSnapshot snapshot)
        {
            return new BotDecisionValidationContext
            {
                requestId = snapshot.snapshotId,
                botId = snapshot.botId,
                knownLaneIds = snapshot.knownLaneIds,
                knownZoneIds = snapshot.knownZoneIds,
                knownTargetIds = snapshot.knownTargetIds,
                allyIds = snapshot.allyIds,
                allowedIntents = BotStrategicCoordinator.SupportedIntents,
                allowChat = false,
                maximumDesiredDistance = 50f
            };
        }
    }
}
