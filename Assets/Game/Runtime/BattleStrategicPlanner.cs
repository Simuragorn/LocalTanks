using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public enum StrategicPlanningState
    {
        NotConfigured,
        FallbackReady,
        WaitingForProvider,
        RequestingMapAnalysis,
        RequestingTeamAPlan,
        RequestingTeamBPlan,
        Ready,
        PartialFallback
    }

    [Serializable]
    public sealed class StrategicPlanningDiagnostics
    {
        public string providerName = "Deterministic";
        public bool mapAnalysisFromLlm;
        public bool teamAPlanFromLlm;
        public bool teamBPlanFromLlm;
        public long mapDurationMilliseconds;
        public long teamADurationMilliseconds;
        public long teamBDurationMilliseconds;
        public string mapError;
        public string teamAError;
        public string teamBError;
    }

    public sealed class BattleStrategicPlanner : MonoBehaviour
    {
        private TacticalMapDescriptor descriptor;
        private BattleScenario scenario;
        private CombatDatabase database;
        private IReadOnlyDictionary<string, BotIdentity> identities;
        private LocalLlmRuntime observedRuntime;
        private bool remotePlanningStarted;

        public StrategicPlanningState State { get; private set; } = StrategicPlanningState.NotConfigured;
        public MapStrategicAnalysis MapAnalysis { get; private set; }
        public TeamBattlePlan TeamAPlan { get; private set; }
        public TeamBattlePlan TeamBPlan { get; private set; }
        public StrategicPlanningDiagnostics Diagnostics { get; private set; } = new StrategicPlanningDiagnostics();
        public bool HasUsablePlans => MapAnalysis != null && TeamAPlan != null && TeamBPlan != null;
        public bool RemotePlanningFinished => State == StrategicPlanningState.Ready || State == StrategicPlanningState.PartialFallback;

        public event Action<BattleStrategicPlanner> PlanningChanged;

        public void Configure(
            TacticalMapDescriptor tacticalDescriptor,
            BattleScenario battleScenario,
            CombatDatabase combatDefinitions,
            IReadOnlyDictionary<string, BotIdentity> botIdentities,
            ILocalLlmProvider provider = null)
        {
            descriptor = tacticalDescriptor ?? throw new ArgumentNullException(nameof(tacticalDescriptor));
            scenario = battleScenario ?? throw new ArgumentNullException(nameof(battleScenario));
            database = combatDefinitions ?? throw new ArgumentNullException(nameof(combatDefinitions));
            identities = botIdentities ?? throw new ArgumentNullException(nameof(botIdentities));

            MapAnalysis = StrategicMapFallback.Create(descriptor);
            TeamAPlan = StrategicTeamPlanFallback.Create(descriptor, scenario, database, identities, TeamId.TeamA);
            TeamBPlan = StrategicTeamPlanFallback.Create(descriptor, scenario, database, identities, TeamId.TeamB);
            Diagnostics = new StrategicPlanningDiagnostics();
            State = StrategicPlanningState.FallbackReady;
            PlanningChanged?.Invoke(this);

            if (provider != null)
            {
                RunWithProvider(provider);
                return;
            }

            ObserveRuntime(LocalLlmRuntime.Instance);
        }

        public void RunWithProvider(ILocalLlmProvider provider)
        {
            if (provider == null || remotePlanningStarted || !HasUsablePlans) return;
            remotePlanningStarted = true;
            Diagnostics.providerName = provider.ProviderName;
            StartCoroutine(GenerateRemotePlans(provider));
        }

        private void ObserveRuntime(LocalLlmRuntime runtime)
        {
            if (runtime == null) return;
            observedRuntime = runtime;
            observedRuntime.StateChanged += OnRuntimeStateChanged;
            if (runtime.State == LocalLlmRuntimeState.Ready)
            {
                RunWithProvider(runtime.Provider);
            }
            else if (runtime.State == LocalLlmRuntimeState.Checking ||
                     runtime.State == LocalLlmRuntimeState.Starting ||
                     runtime.State == LocalLlmRuntimeState.Disabled)
            {
                State = StrategicPlanningState.WaitingForProvider;
                PlanningChanged?.Invoke(this);
            }
        }

        private void OnRuntimeStateChanged(LocalLlmRuntimeState state, string message)
        {
            if (state == LocalLlmRuntimeState.Ready)
            {
                RunWithProvider(observedRuntime?.Provider);
            }
            else if (state == LocalLlmRuntimeState.Unavailable || state == LocalLlmRuntimeState.Disabled)
            {
                State = StrategicPlanningState.FallbackReady;
                if (state == LocalLlmRuntimeState.Unavailable) Diagnostics.mapError = message;
                PlanningChanged?.Invoke(this);
            }
        }

        private IEnumerator GenerateRemotePlans(ILocalLlmProvider provider)
        {
            State = StrategicPlanningState.RequestingMapAnalysis;
            PlanningChanged?.Invoke(this);
            LocalLlmResponse mapResponse = null;
            yield return ExecuteProvider(provider, StrategicPromptBuilder.BuildMapAnalysisRequest(descriptor),
                response => mapResponse = response);
            if (mapResponse != null) Diagnostics.mapDurationMilliseconds = mapResponse.DurationMilliseconds;
            if (TryAcceptMapAnalysis(mapResponse, out string mapError)) Diagnostics.mapAnalysisFromLlm = true;
            else Diagnostics.mapError = mapError;

            State = StrategicPlanningState.RequestingTeamAPlan;
            PlanningChanged?.Invoke(this);
            LocalLlmResponse teamAResponse = null;
            yield return ExecuteProvider(provider,
                StrategicPromptBuilder.BuildTeamPlanRequest(
                    descriptor, MapAnalysis, scenario, database, identities, TeamId.TeamA),
                response => teamAResponse = response);
            if (teamAResponse != null) Diagnostics.teamADurationMilliseconds = teamAResponse.DurationMilliseconds;
            if (TryAcceptTeamPlan(teamAResponse, TeamId.TeamA, out TeamBattlePlan teamA, out string teamAError))
            {
                TeamAPlan = teamA;
                Diagnostics.teamAPlanFromLlm = true;
            }
            else Diagnostics.teamAError = teamAError;

            State = StrategicPlanningState.RequestingTeamBPlan;
            PlanningChanged?.Invoke(this);
            LocalLlmResponse teamBResponse = null;
            yield return ExecuteProvider(provider,
                StrategicPromptBuilder.BuildTeamPlanRequest(
                    descriptor, MapAnalysis, scenario, database, identities, TeamId.TeamB),
                response => teamBResponse = response);
            if (teamBResponse != null) Diagnostics.teamBDurationMilliseconds = teamBResponse.DurationMilliseconds;
            if (TryAcceptTeamPlan(teamBResponse, TeamId.TeamB, out TeamBattlePlan teamB, out string teamBError))
            {
                TeamBPlan = teamB;
                Diagnostics.teamBPlanFromLlm = true;
            }
            else Diagnostics.teamBError = teamBError;

            State = Diagnostics.mapAnalysisFromLlm && Diagnostics.teamAPlanFromLlm && Diagnostics.teamBPlanFromLlm
                ? StrategicPlanningState.Ready
                : StrategicPlanningState.PartialFallback;
            PlanningChanged?.Invoke(this);
        }

        private bool TryAcceptMapAnalysis(LocalLlmResponse response, out string error)
        {
            if (response == null || !response.Succeeded)
            {
                error = response?.Error ?? "Map analysis provider returned no response.";
                return false;
            }

            if (!MapStrategicAnalysisSchema.TryParse(response.Content, out MapStrategicAnalysis candidate, out error))
                return false;
            string[] errors = MapStrategicAnalysisValidator.Validate(candidate, descriptor);
            if (errors.Length > 0)
            {
                error = string.Join(" ", errors);
                return false;
            }

            MapAnalysis = candidate;
            error = string.Empty;
            return true;
        }

        private bool TryAcceptTeamPlan(
            LocalLlmResponse response,
            TeamId team,
            out TeamBattlePlan accepted,
            out string error)
        {
            accepted = null;
            if (response == null || !response.Succeeded)
            {
                error = response?.Error ?? $"{team} plan provider returned no response.";
                return false;
            }

            if (!TeamBattlePlanSchema.TryParse(response.Content, out TeamBattlePlan candidate, out error))
                return false;
            string[] errors = TeamBattlePlanValidator.Validate(candidate, BuildValidationContext(team));
            if (errors.Length > 0)
            {
                error = string.Join(" ", errors);
                return false;
            }

            accepted = candidate;
            error = string.Empty;
            return true;
        }

        private TeamPlanValidationContext BuildValidationContext(TeamId team)
        {
            List<string> bots = new List<string>();
            foreach (BattleScenarioEntry entry in scenario.entries ?? Array.Empty<BattleScenarioEntry>())
            {
                if (entry != null && entry.team == team && !entry.playerControlled) bots.Add(entry.id);
            }
            bots.Sort(StringComparer.Ordinal);
            List<string> lanes = new List<string>();
            foreach (TacticalLaneDescriptor lane in descriptor.lanes ?? Array.Empty<TacticalLaneDescriptor>())
            {
                if (lane != null) lanes.Add(lane.id);
            }
            lanes.Sort(StringComparer.Ordinal);
            return new TeamPlanValidationContext
            {
                scenarioId = scenario.id,
                mapSourceHash = descriptor.sourceHash,
                teamId = team.ToString(),
                knownLaneIds = lanes.ToArray(),
                expectedBotIds = bots.ToArray()
            };
        }

        private static IEnumerator ExecuteProvider(
            ILocalLlmProvider provider,
            LocalLlmRequest request,
            Action<LocalLlmResponse> completed)
        {
            IEnumerator routine;
            try
            {
                routine = provider.Generate(request, completed);
            }
            catch (Exception exception)
            {
                completed?.Invoke(LocalLlmResponse.Failure($"Provider failed before request: {exception.Message}"));
                yield break;
            }

            if (routine == null)
            {
                completed?.Invoke(LocalLlmResponse.Failure("Provider returned no request coroutine."));
                yield break;
            }

            while (true)
            {
                bool moved;
                object current = null;
                try
                {
                    moved = routine.MoveNext();
                    if (moved) current = routine.Current;
                }
                catch (Exception exception)
                {
                    completed?.Invoke(LocalLlmResponse.Failure($"Provider request failed: {exception.Message}"));
                    yield break;
                }

                if (!moved) yield break;
                yield return current;
            }
        }

        private void OnDestroy()
        {
            if (observedRuntime != null) observedRuntime.StateChanged -= OnRuntimeStateChanged;
        }
    }
}
