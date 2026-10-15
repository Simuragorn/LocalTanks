using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Keeps a generated team plan spread over the broad operational directions while still allowing a
    /// vehicle class to avoid a lane for which the runtime map analysis rates it markedly unsuitable.
    /// </summary>
    public static class StrategicLaneDistributionPolicy
    {
        private const float RelativeSuitabilityFloor = 0.65f;

        /// <summary>
        /// Creates the battle-local scenario used by spawning and planning. Authored lanes remain useful
        /// preferences, but bots (the player is excluded) are redistributed when those preferences leave
        /// one operational direction overloaded.
        /// </summary>
        public static BattleScenario CreateBalancedRuntimeScenario(
            BattleScenario source,
            TacticalMapDescriptor descriptor,
            CombatDatabase database)
        {
            if (source == null || source.kind != BattleScenarioKind.StandardBattle || descriptor == null || database == null)
                return source;

            BattleScenario runtime = UnityEngine.Object.Instantiate(source);
            runtime.name = source.name;
            runtime.hideFlags = HideFlags.DontSave;
            runtime.entries = (source.entries ?? Array.Empty<BattleScenarioEntry>())
                .Select(CloneEntry)
                .ToArray();
            TacticalLaneDescriptor[] lanes = RuntimeTacticalMapAnalyzer.SelectOperationalLanes(descriptor);
            if (lanes.Length <= 1) return runtime;

            foreach (TeamId team in new[] { TeamId.TeamA, TeamId.TeamB })
            {
                BattleScenarioEntry[] bots = runtime.entries
                    .Where(item => item != null && item.team == team && !item.playerControlled)
                    .OrderBy(item => item.id, StringComparer.Ordinal)
                    .ToArray();
                int[] assignments = FindBestAssignments(bots, database, descriptor, lanes);
                for (int index = 0; index < bots.Length; index++)
                    bots[index].lane = LaneKind(assignments[index], lanes.Length);
            }

            return runtime;
        }

        public static string[] Validate(
            TeamBattlePlan plan,
            TacticalMapDescriptor descriptor,
            BattleScenario scenario,
            CombatDatabase database,
            TeamId team)
        {
            if (plan == null || descriptor == null || scenario == null || database == null)
                return new[] { "Lane distribution validation is missing battle data." };

            TacticalLaneDescriptor[] lanes = RuntimeTacticalMapAnalyzer.SelectOperationalLanes(descriptor);
            BattleScenarioEntry[] bots = (scenario.entries ?? Array.Empty<BattleScenarioEntry>())
                .Where(item => item != null && item.team == team && !item.playerControlled)
                .OrderBy(item => item.id, StringComparer.Ordinal)
                .ToArray();
            if (lanes.Length <= 1 || bots.Length <= 1) return Array.Empty<string>();

            Dictionary<string, int> laneIndices = lanes
                .Select((lane, index) => new { lane.id, index })
                .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
            Dictionary<string, int[]> eligibility = bots.ToDictionary(
                bot => bot.id,
                bot => EligibleLaneIndices(bot, database.FindTank(bot.tankId), descriptor, lanes),
                StringComparer.Ordinal);
            Distribution optimum = FindBestDistribution(bots, eligibility, lanes.Length);
            List<string> errors = new List<string>();

            if (!laneIndices.ContainsKey(plan.primaryLaneId))
                errors.Add($"Team plan primary lane '{plan.primaryLaneId}' is not an operational direction.");
            if (!laneIndices.ContainsKey(plan.secondaryLaneId))
                errors.Add($"Team plan secondary lane '{plan.secondaryLaneId}' is not an operational direction.");

            int[] counts = new int[lanes.Length];
            foreach (TeamPlanAssignment assignment in plan.assignments ?? Array.Empty<TeamPlanAssignment>())
            {
                if (assignment == null || !laneIndices.TryGetValue(assignment.laneId, out int laneIndex))
                {
                    if (assignment != null)
                        errors.Add($"Bot '{assignment.botId}' is assigned outside the operational directions.");
                    continue;
                }

                counts[laneIndex]++;
                if (eligibility.TryGetValue(assignment.botId, out int[] allowed) &&
                    Array.IndexOf(allowed, laneIndex) < 0)
                {
                    errors.Add(
                        $"Bot '{assignment.botId}' cannot use lane '{assignment.laneId}' with its vehicle class.");
                }
            }

            int used = counts.Count(value => value > 0);
            int imbalance = counts.Max() - counts.Min();
            if (used < optimum.UsedLaneCount || imbalance > optimum.Imbalance)
            {
                errors.Add(
                    $"Team plan concentrates bots on too few directions: uses {used}/{optimum.UsedLaneCount}, " +
                    $"lane imbalance {imbalance}/{optimum.Imbalance}.");
            }

            return errors.ToArray();
        }

        private static int[] EligibleLaneIndices(
            BattleScenarioEntry entry,
            TankDefinition tank,
            TacticalMapDescriptor descriptor,
            IReadOnlyList<TacticalLaneDescriptor> lanes)
        {
            float[] scores = lanes.Select(lane => Suitability(lane?.suitability, tank?.vehicleClass)).ToArray();
            float best = scores.Length > 0 ? scores.Max() : 0f;
            HashSet<int> result = new HashSet<int>();
            for (int index = 0; index < scores.Length; index++)
            {
                if (best <= 0f || scores[index] >= best * RelativeSuitabilityFloor)
                    result.Add(index);
            }

            TacticalLaneDescriptor authored = RuntimeTacticalMapAnalyzer.SelectLane(descriptor, entry.lane);
            for (int index = 0; index < lanes.Count; index++)
            {
                if (authored != null && string.Equals(authored.id, lanes[index].id, StringComparison.Ordinal))
                    result.Add(index);
            }

            if (result.Count == 0)
            {
                for (int index = 0; index < lanes.Count; index++) result.Add(index);
            }

            return result.OrderBy(index => index).ToArray();
        }

        private static float Suitability(TacticalClassSuitability suitability, VehicleClass? vehicleClass)
        {
            if (suitability == null || !vehicleClass.HasValue) return 1f;
            return vehicleClass.Value switch
            {
                VehicleClass.LightTank => suitability.lightTank,
                VehicleClass.MediumTank => suitability.mediumTank,
                VehicleClass.HeavyTank => suitability.heavyTank,
                VehicleClass.TankDestroyer => suitability.tankDestroyer,
                VehicleClass.Artillery => suitability.artillery,
                _ => 1f
            };
        }

        private static Distribution FindBestDistribution(
            IReadOnlyList<BattleScenarioEntry> bots,
            IReadOnlyDictionary<string, int[]> eligibility,
            int laneCount)
        {
            int[] counts = new int[laneCount];
            Distribution best = new Distribution(0, int.MaxValue);
            Search(0);
            return best;

            void Search(int botIndex)
            {
                if (botIndex >= bots.Count)
                {
                    int used = counts.Count(value => value > 0);
                    int imbalance = counts.Max() - counts.Min();
                    if (used > best.UsedLaneCount || used == best.UsedLaneCount && imbalance < best.Imbalance)
                        best = new Distribution(used, imbalance);
                    return;
                }

                int[] allowed = eligibility[bots[botIndex].id];
                foreach (int laneIndex in allowed)
                {
                    counts[laneIndex]++;
                    Search(botIndex + 1);
                    counts[laneIndex]--;
                }
            }
        }

        private static int[] FindBestAssignments(
            IReadOnlyList<BattleScenarioEntry> bots,
            CombatDatabase database,
            TacticalMapDescriptor descriptor,
            IReadOnlyList<TacticalLaneDescriptor> lanes)
        {
            if (bots.Count == 0) return Array.Empty<int>();
            Dictionary<string, int[]> eligibility = bots.ToDictionary(
                bot => bot.id,
                bot => EligibleLaneIndices(bot, database.FindTank(bot.tankId), descriptor, lanes),
                StringComparer.Ordinal);
            int[] counts = new int[lanes.Count];
            int[] current = new int[bots.Count];
            int[] bestAssignments = new int[bots.Count];
            int bestUsed = -1;
            int bestImbalance = int.MaxValue;
            float bestScore = float.NegativeInfinity;
            int bestAuthoredMatches = -1;
            Search(0, 0f, 0);
            return bestAssignments;

            void Search(int botIndex, float score, int authoredMatches)
            {
                if (botIndex >= bots.Count)
                {
                    int used = counts.Count(value => value > 0);
                    int imbalance = counts.Max() - counts.Min();
                    bool better = used > bestUsed ||
                                  used == bestUsed && imbalance < bestImbalance ||
                                  used == bestUsed && imbalance == bestImbalance && score > bestScore + 0.0001f ||
                                  used == bestUsed && imbalance == bestImbalance &&
                                  Mathf.Abs(score - bestScore) <= 0.0001f && authoredMatches > bestAuthoredMatches;
                    if (!better) return;
                    bestUsed = used;
                    bestImbalance = imbalance;
                    bestScore = score;
                    bestAuthoredMatches = authoredMatches;
                    Array.Copy(current, bestAssignments, current.Length);
                    return;
                }

                BattleScenarioEntry bot = bots[botIndex];
                TankDefinition tank = database.FindTank(bot.tankId);
                TacticalLaneDescriptor authored = RuntimeTacticalMapAnalyzer.SelectLane(descriptor, bot.lane);
                foreach (int laneIndex in eligibility[bot.id])
                {
                    TacticalLaneDescriptor lane = lanes[laneIndex];
                    bool keepsAuthored = authored != null && string.Equals(authored.id, lane.id, StringComparison.Ordinal);
                    current[botIndex] = laneIndex;
                    counts[laneIndex]++;
                    Search(
                        botIndex + 1,
                        score + Suitability(lane.suitability, tank?.vehicleClass) + RolePreference(bot.role, laneIndex, lanes.Count),
                        authoredMatches + (keepsAuthored ? 1 : 0));
                    counts[laneIndex]--;
                }
            }
        }

        private static float RolePreference(CombatAiRole role, int laneIndex, int laneCount)
        {
            bool center = laneIndex == laneCount / 2;
            return role switch
            {
                CombatAiRole.Assault when center => 0.08f,
                CombatAiRole.Flank when !center => 0.08f,
                CombatAiRole.Support when center => 0.03f,
                _ => 0f
            };
        }

        private static BattleLane LaneKind(int laneIndex, int laneCount)
        {
            if (laneCount <= 1) return BattleLane.Center;
            if (laneIndex <= 0) return BattleLane.North;
            if (laneIndex >= laneCount - 1) return BattleLane.South;
            return BattleLane.Center;
        }

        private static BattleScenarioEntry CloneEntry(BattleScenarioEntry source)
        {
            if (source == null) return null;
            return new BattleScenarioEntry
            {
                id = source.id,
                tankId = source.tankId,
                team = source.team,
                playerControlled = source.playerControlled,
                baseId = source.baseId,
                spawnSlot = source.spawnSlot,
                lane = source.lane,
                role = source.role
            };
        }

        private readonly struct Distribution
        {
            public Distribution(int usedLaneCount, int imbalance)
            {
                UsedLaneCount = usedLaneCount;
                Imbalance = imbalance;
            }

            public int UsedLaneCount { get; }
            public int Imbalance { get; }
        }
    }
}
