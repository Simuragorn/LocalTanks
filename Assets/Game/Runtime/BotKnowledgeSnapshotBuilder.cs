using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public static class BotKnowledgeSnapshotBuilder
    {
        public static BotKnowledgeSnapshot Build(
            string snapshotId,
            BattleDirector director,
            CombatTankAI ai,
            BattleStrategicPlanner planner,
            float gameTimeSeconds)
        {
            if (director == null) throw new ArgumentNullException(nameof(director));
            if (ai == null) throw new ArgumentNullException(nameof(ai));
            TeamMember selfMember = ai.GetComponent<TeamMember>();
            TankHealth selfHealth = ai.GetComponent<TankHealth>();
            TankDefinition definition = selfMember?.Definition;
            BotIdentityComponent identityComponent = ai.GetComponent<BotIdentityComponent>();
            if (selfMember == null || selfHealth == null || definition == null || identityComponent?.Identity == null)
                throw new InvalidOperationException("Bot knowledge snapshot requires a configured living battle member.");

            string botId = director.GetEntityId(selfMember);
            TeamBattlePlan teamPlan = selfMember.Team == TeamId.TeamA ? planner?.TeamAPlan : planner?.TeamBPlan;
            TeamPlanAssignment assignment = teamPlan?.assignments?.FirstOrDefault(item => item != null && item.botId == botId);
            BotEnemyKnowledge[] enemies = director.SpawnedMembers
                .Where(item => item != null && item.Team != TeamId.Neutral && item.Team != selfMember.Team)
                .Select(item => BuildEnemy(director, selfMember.Team, item, gameTimeSeconds))
                .OrderBy(item => item.memberId, StringComparer.Ordinal)
                .ToArray();
            BotAllyKnowledge[] allies = director.SpawnedMembers
                .Where(item => item != null && item != selfMember && item.Team == selfMember.Team)
                .Select(item => BuildAlly(director, item))
                .OrderBy(item => item.memberId, StringComparer.Ordinal)
                .ToArray();
            BotKnowledgeSnapshot snapshot = new BotKnowledgeSnapshot
            {
                snapshotId = snapshotId,
                battleId = director.Scenario != null ? director.Scenario.id : string.Empty,
                botId = botId,
                teamId = selfMember.Team.ToString(),
                gameTimeSeconds = Mathf.Max(0f, gameTimeSeconds),
                alliedAlive = selfMember.Team == TeamId.TeamA ? director.TeamAAlive : director.TeamBAlive,
                enemyAlive = selfMember.Team == TeamId.TeamA ? director.TeamBAlive : director.TeamAAlive,
                identity = identityComponent.Identity,
                mentalState = CloneMentalState(identityComponent.MentalState),
                self = BuildSelf(ai, selfHealth, definition),
                allies = allies,
                enemies = enemies,
                bases = director.Bases.Where(item => item != null)
                    .OrderBy(item => item.BaseId, StringComparer.Ordinal)
                    .Select(BuildBase)
                    .ToArray(),
                plan = BuildPlan(teamPlan, assignment),
                knownLaneIds = RuntimeTacticalMapAnalyzer.SelectOperationalLanes(director.TacticalMap)
                    .Select(item => item.id)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray(),
                knownZoneIds = Array.Empty<string>(),
                knownTargetIds = enemies
                    .Where(item => item.isAlive && item.contactState != BotContactState.Unknown)
                    .Select(item => item.memberId)
                    .ToArray(),
                allyIds = allies.Where(item => item.isAlive).Select(item => item.memberId).ToArray()
            };
            snapshot.snapshotHash = BotKnowledgeSnapshotHasher.Compute(snapshot);
            string[] errors = BotKnowledgeSnapshotValidator.Validate(snapshot);
            if (errors.Length > 0)
                throw new InvalidOperationException("Generated bot knowledge snapshot is invalid:\n- " + string.Join("\n- ", errors));
            return snapshot;
        }

        private static BotVehicleKnowledge BuildSelf(
            CombatTankAI ai,
            TankHealth health,
            TankDefinition definition)
        {
            WeaponDefinition weapon = definition.weapon;
            ShellDefinition shell = weapon != null ? weapon.shell : null;
            Vector2 position = ai.transform.position;
            return new BotVehicleKnowledge
            {
                tankId = definition.id,
                displayName = definition.displayName,
                vehicleClass = definition.vehicleClass.ToString(),
                nation = definition.nation.ToString(),
                tier = definition.tier,
                currentHitPoints = health.CurrentHitPoints,
                maximumHitPoints = health.MaximumHitPoints,
                positionX = position.x,
                positionY = position.y,
                hullRotationDegrees = ai.transform.eulerAngles.z,
                preferredDistance = ai.PreferredDistance,
                tacticalState = ai.State.ToString(),
                fireBlockReason = ai.CurrentFireBlockReason.ToString(),
                armor = definition.armor,
                maximumForwardSpeed = definition.maxForwardSpeed,
                maximumReverseSpeed = definition.maxReverseSpeed,
                hullTurnSpeed = definition.hullTurnSpeed,
                turretTurnSpeed = definition.turretTurnSpeed,
                viewRange = definition.viewRange,
                stationaryConcealment = definition.stationaryConcealment,
                minimumDispersionDegrees = definition.minimumDispersionDegrees,
                maximumDispersionDegrees = definition.maximumDispersionDegrees,
                aimingTimeSeconds = definition.aimingTimeSeconds,
                weaponId = weapon != null ? weapon.id : string.Empty,
                reloadSeconds = weapon != null ? weapon.reloadSeconds : 0f,
                shellId = shell != null ? shell.id : string.Empty,
                shellDamage = shell != null ? shell.damage : 0,
                shellPenetration = shell != null ? shell.penetration : 0f,
                shellSpeed = shell != null ? shell.speed : 0f
            };
        }

        private static BotAllyKnowledge BuildAlly(BattleDirector director, TeamMember member)
        {
            TankDefinition tank = member.Definition;
            TankHealth health = member.Health;
            Vector2 position = member.transform.position;
            BotIdentity identity = member.GetComponent<BotIdentityComponent>()?.Identity;
            return new BotAllyKnowledge
            {
                memberId = director.GetEntityId(member),
                nickname = identity?.nickname ?? (member.IsPlayerControlled ? "Игрок" : string.Empty),
                tankId = tank != null ? tank.id : string.Empty,
                vehicleClass = tank != null ? tank.vehicleClass.ToString() : string.Empty,
                isPlayer = member.IsPlayerControlled,
                isAlive = member.IsAlive,
                currentHitPoints = health != null ? health.CurrentHitPoints : 0,
                maximumHitPoints = health != null ? health.MaximumHitPoints : 0,
                positionX = position.x,
                positionY = position.y
            };
        }

        private static BotEnemyKnowledge BuildEnemy(
            BattleDirector director,
            TeamId viewingTeam,
            TeamMember enemy,
            float gameTimeSeconds)
        {
            string memberId = director.GetEntityId(enemy);
            TankDefinition tank = enemy.Definition;
            TankHealth health = enemy.Health;
            BotEnemyKnowledge result = new BotEnemyKnowledge
            {
                memberId = memberId,
                tankId = tank != null ? tank.id : string.Empty,
                vehicleClass = tank != null ? tank.vehicleClass.ToString() : string.Empty,
                isAlive = enemy.IsAlive,
                maximumHitPoints = health != null ? health.MaximumHitPoints : 0,
                currentHitPoints = -1,
                contactState = BotContactState.Unknown,
                secondsSinceSeen = -1f
            };
            if (!enemy.IsAlive) return result;

            TeamVisionSystem vision = director.VisionSystem;
            bool visible = vision != null && vision.IsVisibleTo(viewingTeam, enemy);
            if (visible)
            {
                Vector2 position = enemy.transform.position;
                result.contactState = BotContactState.Visible;
                result.hasKnownPosition = true;
                result.positionIsCurrent = true;
                result.positionX = position.x;
                result.positionY = position.y;
                result.secondsSinceSeen = 0f;
                result.currentHitPoints = health != null ? health.CurrentHitPoints : 0;
            }
            else if (vision != null && vision.TryGetContact(viewingTeam, enemy, out VisionContact contact) &&
                     contact.HasMemory)
            {
                result.contactState = BotContactState.Remembered;
                result.hasKnownPosition = true;
                result.positionIsCurrent = false;
                result.positionX = contact.LastKnownPosition.x;
                result.positionY = contact.LastKnownPosition.y;
                result.secondsSinceSeen = Mathf.Max(0f, gameTimeSeconds - contact.LastSeenTime);
            }
            return result;
        }

        private static BotBaseKnowledge BuildBase(CaptureBase captureBase)
        {
            return new BotBaseKnowledge
            {
                baseId = captureBase.BaseId,
                ownerTeamId = captureBase.Owner.ToString(),
                state = captureBase.State.ToString(),
                capturingTeamId = captureBase.CapturingTeam.ToString(),
                captureProgress = captureBase.Progress,
                capturingTankCount = captureBase.CapturingTankCount,
                remainingSeconds = captureBase.RemainingSeconds
            };
        }

        private static BotPlanKnowledge BuildPlan(TeamBattlePlan teamPlan, TeamPlanAssignment assignment)
        {
            return new BotPlanKnowledge
            {
                teamSummary = teamPlan?.summary ?? string.Empty,
                primaryLaneId = teamPlan?.primaryLaneId ?? string.Empty,
                secondaryLaneId = teamPlan?.secondaryLaneId ?? string.Empty,
                assignedLaneId = assignment?.laneId ?? string.Empty,
                assignedTask = assignment != null ? assignment.task.ToString() : string.Empty,
                assignmentPriority = assignment?.priority ?? 0f,
                assignmentRationale = assignment?.rationale ?? string.Empty,
                isReserve = assignment != null && teamPlan != null && assignment.botId == teamPlan.reserveBotId
            };
        }

        private static BotMentalState CloneMentalState(BotMentalState source)
        {
            return source == null ? new BotMentalState() : new BotMentalState
            {
                morale = source.morale,
                stress = source.stress,
                boredom = source.boredom,
                planConfidence = source.planConfidence
            };
        }
    }
}
