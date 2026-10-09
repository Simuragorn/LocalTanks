using UnityEngine;

namespace LocalTanks
{
    public enum BotStrategicRequestState
    {
        Fallback,
        Queued,
        Requesting,
        Applied,
        Rejected
    }

    [RequireComponent(typeof(CombatTankAI), typeof(BotIdentityComponent), typeof(TeamMember))]
    public sealed class BotStrategicBrain : MonoBehaviour
    {
        private BotStrategicCoordinator coordinator;
        private CombatTankAI ai;
        private int snapshotSequence;

        public string BotId { get; private set; }
        public BotKnowledgeSnapshot CurrentSnapshot { get; private set; }
        public BotStrategicDecision CurrentDecision { get; private set; }
        public BotStrategicRequestState RequestState { get; private set; } = BotStrategicRequestState.Fallback;
        public string LastTrigger { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;
        public long LastDurationMilliseconds { get; private set; }
        public bool DecisionFromLlm { get; private set; }
        public float LastRequestGameTime { get; private set; } = float.NegativeInfinity;
        public CombatTankAI TacticalAi => ai;

        public void Configure(BotStrategicCoordinator owner, CombatTankAI tacticalAi, string botId)
        {
            coordinator = owner;
            ai = tacticalAi != null ? tacticalAi : GetComponent<CombatTankAI>();
            BotId = botId ?? string.Empty;
            snapshotSequence = 0;
        }

        public string NextSnapshotId(string battleId)
        {
            snapshotSequence++;
            return $"{battleId}:{BotId}:{snapshotSequence:0000}";
        }

        public void SetQueued(string trigger)
        {
            RequestState = BotStrategicRequestState.Queued;
            LastTrigger = trigger ?? string.Empty;
        }

        public void SetRequesting(BotKnowledgeSnapshot snapshot, string trigger)
        {
            CurrentSnapshot = snapshot;
            RequestState = BotStrategicRequestState.Requesting;
            LastTrigger = trigger ?? string.Empty;
            LastRequestGameTime = Time.time;
            LastError = string.Empty;
        }

        public void Apply(
            BotKnowledgeSnapshot snapshot,
            BotStrategicDecision decision,
            bool fromLlm,
            string trigger,
            string error,
            long durationMilliseconds)
        {
            CurrentSnapshot = snapshot;
            CurrentDecision = decision;
            DecisionFromLlm = fromLlm;
            LastTrigger = trigger ?? string.Empty;
            LastError = error ?? string.Empty;
            LastDurationMilliseconds = durationMilliseconds;
            RequestState = fromLlm ? BotStrategicRequestState.Applied :
                string.IsNullOrWhiteSpace(error) ? BotStrategicRequestState.Fallback : BotStrategicRequestState.Rejected;
            coordinator?.ApplyToTacticalAi(this, decision, snapshot);
        }

        public void RequestDecision(string trigger)
        {
            coordinator?.RequestDecision(this, trigger);
        }
    }
}
