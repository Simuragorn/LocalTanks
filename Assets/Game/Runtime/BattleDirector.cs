using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    [Serializable]
    public sealed class TankPrefabBinding
    {
        public string tankId;
        public GameObject prefab;
    }

    [Serializable]
    public sealed class VehicleClassIconBinding
    {
        public VehicleClass vehicleClass;
        public Sprite icon;
    }

    public sealed class BattleDirector : MonoBehaviour
    {
        [SerializeField] private BattleScenario scenario;
        [SerializeField] private TacticalMapDescriptorAsset tacticalMap;
        [SerializeField] private CombatDatabase combatDatabase;
        [SerializeField] private NavigationMap navigationMap;
        [SerializeField] private TeamVisionSystem visionSystem;
        [SerializeField] private CameraFollow2D cameraFollow;
        [SerializeField] private CaptureBase[] bases;
        [SerializeField] private TankPrefabBinding[] tankPrefabs;
        [SerializeField] private VehicleClassIconBinding[] classIcons;

        private BattleVisibilitySettings visibilitySettings;
        private readonly List<TankVisibilityPresenter> spawnedVisibilityPresenters = new List<TankVisibilityPresenter>();

        private readonly List<TeamMember> spawnedMembers = new List<TeamMember>();
        private readonly Dictionary<TankHealth, TeamId> trackedHealth = new Dictionary<TankHealth, TeamId>();
        private readonly Dictionary<string, BotIdentity> botIdentities = new Dictionary<string, BotIdentity>(StringComparer.Ordinal);
        private readonly Dictionary<string, TeamMember> membersByEntityId = new Dictionary<string, TeamMember>(StringComparer.Ordinal);
        private readonly Dictionary<TeamMember, string> entityIdsByMember = new Dictionary<TeamMember, string>();
        private bool spawned;

        public event Action<TeamMember> TankSpawned;
        public event Action<TeamId, int, int> LivingCountsChanged;

        public IReadOnlyList<TeamMember> SpawnedMembers => spawnedMembers;
        public TeamMember PlayerMember { get; private set; }
        public TacticalMapDescriptor TacticalMap => tacticalMap != null ? tacticalMap.descriptor : null;
        public BattleStrategicPlanner StrategicPlanner { get; private set; }
        public BotStrategicCoordinator StrategicCoordinator { get; private set; }
        public BattleDiagnosticsOverlay DiagnosticsOverlay { get; private set; }
        public BattleScenario Scenario => scenario;
        public CombatDatabase CombatDatabase => combatDatabase;
        public NavigationMap NavigationMap => navigationMap;
        public TeamVisionSystem VisionSystem => visionSystem;
        public IReadOnlyList<CaptureBase> Bases => bases ?? Array.Empty<CaptureBase>();
        public int TeamAAlive { get; private set; }
        public int TeamBAlive { get; private set; }
        public IReadOnlyDictionary<string, BotIdentity> BotIdentities => botIdentities;
        public IReadOnlyList<TankDefinition> SpawnableTankDefinitions => (combatDatabase?.tanks ?? Array.Empty<TankDefinition>())
            .Where(item => item != null && item.availableInGame && CanSpawnTank(item.id))
            .OrderBy(item => item.tier)
            .ThenBy(item => item.displayName, StringComparer.Ordinal)
            .ToArray();

        public bool CanSpawnTank(string tankId)
        {
            return !string.IsNullOrWhiteSpace(tankId) && tankPrefabs != null &&
                   tankPrefabs.Any(item => item != null && item.tankId == tankId && item.prefab != null);
        }

        public void OverrideScenario(BattleScenario battleScenario)
        {
            if (spawned) throw new InvalidOperationException("Cannot replace a scenario after tanks were spawned.");
            scenario = battleScenario != null
                ? battleScenario
                : throw new ArgumentNullException(nameof(battleScenario));
        }

        private void Start()
        {
            SpawnAll();
        }

        private void OnDestroy()
        {
            foreach (TankHealth health in trackedHealth.Keys.ToArray())
            {
                if (health != null)
                {
                    health.Destroyed -= OnTankDestroyed;
                }
            }
        }

        public void Configure(
            BattleScenario battleScenario,
            CombatDatabase database,
            NavigationMap map,
            TeamVisionSystem teamVision,
            CameraFollow2D follow,
            CaptureBase[] captureBases,
            TankPrefabBinding[] prefabBindings,
            VehicleClassIconBinding[] iconBindings,
            TacticalMapDescriptorAsset tacticalMapDescriptor)
        {
            scenario = battleScenario;
            tacticalMap = tacticalMapDescriptor;
            combatDatabase = database;
            navigationMap = map;
            visionSystem = teamVision;
            cameraFollow = follow;
            bases = captureBases;
            tankPrefabs = prefabBindings;
            classIcons = iconBindings;
        }

        public void SpawnAll()
        {
            if (spawned)
            {
                return;
            }

            ValidateConfiguration();
            spawned = true;
            BattleScenarioEntry[] entries = scenario.entries
                .OrderBy(item => item.team)
                .ThenBy(item => item.spawnSlot)
                .ToArray();
            BuildBotIdentities(entries);
            for (int index = 0; index < entries.Length; index++)
            {
                Spawn(entries[index], index);
            }

            if (scenario.kind != BattleScenarioKind.AiDuelLab)
            {
                StrategicPlanner = GetComponent<BattleStrategicPlanner>();
                if (StrategicPlanner == null) StrategicPlanner = gameObject.AddComponent<BattleStrategicPlanner>();
                StrategicPlanner.Configure(
                    TacticalMap,
                    scenario,
                    combatDatabase,
                    botIdentities,
                    observeLocalRuntime: scenario.UsesRemoteStrategy);
                StrategicCoordinator = GetComponent<BotStrategicCoordinator>();
                if (StrategicCoordinator == null) StrategicCoordinator = gameObject.AddComponent<BotStrategicCoordinator>();
                StrategicCoordinator.Configure(this, StrategicPlanner, scenario.UsesRemoteStrategy);
            }
            DiagnosticsOverlay = GetComponent<BattleDiagnosticsOverlay>();
            if (DiagnosticsOverlay == null) DiagnosticsOverlay = gameObject.AddComponent<BattleDiagnosticsOverlay>();
            DiagnosticsOverlay.Configure(this);

            // A batch run that asked for a recording gets one; in a normal battle this returns null and
            // costs nothing. Same idea for the simulation rate: a test that asked to run fast gets a
            // controller, and a played battle keeps the real-time settings.
            BattleTelemetry.AttachWhenRequested(this);
            BattleSimulationClock.AttachWhenRequested(this);
            // Command line overrides are applied after spawning, so they cover every member that exists
            // when the first frame runs. Components that appear later apply them for themselves.
            LocalTanksOverrides.ApplyToLoadedScenes();

            LivingCountsChanged?.Invoke(TeamId.Neutral, TeamAAlive, TeamBAlive);
        }

        private void Spawn(BattleScenarioEntry entry, int decisionIndex)
        {
            CaptureBase spawnBase = bases.First(item => item != null && item.BaseId == entry.baseId);
            TankPrefabBinding binding = tankPrefabs.First(item => item != null && item.tankId == entry.tankId);
            Vector2 position = spawnBase.GetSpawnPosition(entry.spawnSlot);
            Quaternion rotation = Quaternion.Euler(0f, 0f, entry.team == TeamId.TeamA ? -90f : 90f);
            GameObject tank = Instantiate(binding.prefab, position, rotation, transform);
            tank.name = $"{entry.team}_{entry.id}";

            PlayerTankInput input = tank.GetComponent<PlayerTankInput>();
            TankMotor motor = tank.GetComponent<TankMotor>();
            TankHealth health = tank.GetComponent<TankHealth>();
            TankDefinition definition = health != null ? health.Definition : null;

            TerrainMotorModifier terrain = tank.GetComponent<TerrainMotorModifier>();
            if (terrain == null) terrain = tank.AddComponent<TerrainMotorModifier>();
            terrain.Configure(navigationMap, motor);

            TankVisibilityPresenter visibility = tank.GetComponent<TankVisibilityPresenter>();
            if (visibility == null) visibility = tank.AddComponent<TankVisibilityPresenter>();
            ApplyVisibilitySettings(visibility);
            spawnedVisibilityPresenters.Add(visibility);
            Sprite classIcon = ResolveClassIcon(definition != null ? definition.vehicleClass : VehicleClass.HeavyTank);
            TankClassIconPresenter iconPresenter = tank.GetComponent<TankClassIconPresenter>();
            if (iconPresenter == null) iconPresenter = tank.AddComponent<TankClassIconPresenter>();
            iconPresenter.Configure(classIcon, entry.team == TeamId.TeamA, 1.7f);
            TeamMember member = tank.GetComponent<TeamMember>();
            if (member == null) member = tank.AddComponent<TeamMember>();
            member.Configure(entry.team, entry.playerControlled);
            membersByEntityId.Add(entry.id, member);
            entityIdsByMember.Add(member, entry.id);

            if (entry.playerControlled)
            {
                if (input != null) input.enabled = true;
                PlayerMember = member;
                cameraFollow?.Configure(tank.transform);
                cameraFollow?.GetComponent<PlayerAimReticle>()?.Configure(tank.transform);
            }
            else
            {
                if (input != null) input.enabled = false;
                TankHealthBar healthBar = tank.GetComponent<TankHealthBar>();
                if (healthBar == null) healthBar = tank.AddComponent<TankHealthBar>();
                healthBar.Configure(1.45f, entry.team == TeamId.TeamA);
                BotIdentityComponent botIdentity = tank.GetComponent<BotIdentityComponent>();
                if (botIdentity == null) botIdentity = tank.AddComponent<BotIdentityComponent>();
                botIdentity.Configure(botIdentities[entry.id]);
                BotIdentityPresenter identityPresenter = tank.GetComponent<BotIdentityPresenter>();
                if (identityPresenter == null) identityPresenter = tank.AddComponent<BotIdentityPresenter>();
                identityPresenter.Configure();

                NavigationAgent navigation = tank.GetComponent<NavigationAgent>();
                if (navigation == null) navigation = tank.AddComponent<NavigationAgent>();
                navigation.Configure(navigationMap, motor, Array.Empty<Vector2>(), 0, false);
                Vector2[] route = entry.routeCells.Select(navigationMap.CellToWorld).ToArray();
                CombatTankAI ai = tank.GetComponent<CombatTankAI>();
                if (ai == null) ai = tank.AddComponent<CombatTankAI>();
                ai.Configure(
                    visionSystem,
                    navigation,
                    entry.lane,
                    entry.role,
                    route,
                    decisionIndex * 0.031f);
            }

            visibility.RefreshVisuals();

            spawnedMembers.Add(member);
            if (entry.team == TeamId.TeamA) TeamAAlive++;
            if (entry.team == TeamId.TeamB) TeamBAlive++;
            if (health != null)
            {
                trackedHealth[health] = entry.team;
                health.Destroyed += OnTankDestroyed;
            }

            TankSpawned?.Invoke(member);
        }

        private void BuildBotIdentities(IEnumerable<BattleScenarioEntry> entries)
        {
            BotProfileContext[] contexts = entries
                .Where(entry => entry != null && !entry.playerControlled)
                .Select(entry =>
                {
                    TankDefinition definition = combatDatabase.FindTank(entry.tankId);
                    return new BotProfileContext
                    {
                        entityId = entry.id,
                        team = entry.team.ToString(),
                        tankId = entry.tankId,
                        vehicleClass = definition != null ? definition.vehicleClass.ToString() : string.Empty,
                        nation = definition != null ? definition.nation.ToString() : string.Empty,
                        lane = entry.lane.ToString(),
                        role = entry.role.ToString()
                    };
                })
                .ToArray();
            botIdentities.Clear();
            foreach (BotIdentity identity in BotPersonalityGenerator.GenerateRoster(scenario.id, contexts))
            {
                botIdentities.Add(identity.entityId, identity);
            }
        }

        public string GetEntityId(TeamMember member)
        {
            return member != null && entityIdsByMember.TryGetValue(member, out string entityId)
                ? entityId
                : string.Empty;
        }

        public TeamMember FindMember(string entityId)
        {
            return !string.IsNullOrWhiteSpace(entityId) && membersByEntityId.TryGetValue(entityId, out TeamMember member)
                ? member
                : null;
        }

        private void OnTankDestroyed(TankHealth health)
        {
            if (health == null || !trackedHealth.TryGetValue(health, out TeamId team))
            {
                return;
            }

            health.Destroyed -= OnTankDestroyed;
            trackedHealth.Remove(health);
            if (team == TeamId.TeamA) TeamAAlive = Mathf.Max(0, TeamAAlive - 1);
            if (team == TeamId.TeamB) TeamBAlive = Mathf.Max(0, TeamBAlive - 1);
            LivingCountsChanged?.Invoke(team, TeamAAlive, TeamBAlive);
        }

        private Sprite ResolveClassIcon(VehicleClass vehicleClass)
        {
            VehicleClassIconBinding binding = classIcons?.FirstOrDefault(item => item != null && item.vehicleClass == vehicleClass);
            return binding != null ? binding.icon : null;
        }

        /// <summary>
        /// Scene-owned visibility presentation. A scene without the component keeps unseen tanks
        /// invisible, which is the production behaviour.
        /// </summary>
        private BattleVisibilitySettings ResolveVisibilitySettings()
        {
            if (visibilitySettings == null)
            {
                visibilitySettings = FindAnyObjectByType<BattleVisibilitySettings>();
            }

            return visibilitySettings;
        }

        private void ApplyVisibilitySettings(TankVisibilityPresenter presenter)
        {
            if (presenter == null)
            {
                return;
            }

            BattleVisibilitySettings settings = ResolveVisibilitySettings();
            presenter.ConfigureHiddenPresentation(
                settings != null && settings.ShowHiddenTanksForTesting,
                settings != null ? settings.HiddenTankAlpha : 0.28f);
        }

        /// <summary>
        /// Reapplies the scene visibility presentation to every spawned tank. A scene controller
        /// that changes the setting after the tanks exist calls this instead of relying on the
        /// undefined order between its own Awake and <see cref="SpawnAll"/>.
        /// </summary>
        public void ReapplyVisibilitySettings()
        {
            visibilitySettings = FindAnyObjectByType<BattleVisibilitySettings>();
            foreach (TankVisibilityPresenter presenter in spawnedVisibilityPresenters)
            {
                ApplyVisibilitySettings(presenter);
            }
        }

        private void ValidateConfiguration()
        {
            if (scenario == null) throw new InvalidOperationException("BattleDirector has no scenario.");
            if (tacticalMap == null || tacticalMap.Validate().Length > 0)
                throw new InvalidOperationException("BattleDirector has no valid tactical map descriptor.");
            if (!string.Equals(tacticalMap.descriptor.sceneId, scenario.sceneId, StringComparison.Ordinal))
                throw new InvalidOperationException("Battle scenario and tactical map descriptor refer to different maps.");
            string[] errors = scenario.Validate(combatDatabase);
            if (errors.Length > 0)
                throw new InvalidOperationException("Battle scenario is invalid:\n- " + string.Join("\n- ", errors));
            if (navigationMap == null || visionSystem == null || cameraFollow == null)
                throw new InvalidOperationException("BattleDirector runtime systems are not configured.");
            foreach (string baseId in scenario.entries.Select(item => item.baseId).Distinct())
            {
                if (bases == null || !bases.Any(item => item != null && item.BaseId == baseId))
                    throw new InvalidOperationException($"BattleDirector cannot find base '{baseId}'.");
            }

            foreach (string tankId in scenario.entries.Select(item => item.tankId).Distinct())
            {
                if (tankPrefabs == null || !tankPrefabs.Any(item => item != null && item.tankId == tankId && item.prefab != null))
                    throw new InvalidOperationException($"BattleDirector cannot find prefab for '{tankId}'.");
            }
        }
    }
}
