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
        [SerializeField] private CombatDatabase combatDatabase;
        [SerializeField] private NavigationMap navigationMap;
        [SerializeField] private TeamVisionSystem visionSystem;
        [SerializeField] private CameraFollow2D cameraFollow;
        [SerializeField] private CaptureBase[] bases;
        [SerializeField] private TankPrefabBinding[] tankPrefabs;
        [SerializeField] private VehicleClassIconBinding[] classIcons;

        private readonly List<TeamMember> spawnedMembers = new List<TeamMember>();
        private readonly Dictionary<TankHealth, TeamId> trackedHealth = new Dictionary<TankHealth, TeamId>();
        private bool spawned;

        public event Action<TeamMember> TankSpawned;
        public event Action<TeamId, int, int> LivingCountsChanged;

        public IReadOnlyList<TeamMember> SpawnedMembers => spawnedMembers;
        public TeamMember PlayerMember { get; private set; }
        public int TeamAAlive { get; private set; }
        public int TeamBAlive { get; private set; }

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
            VehicleClassIconBinding[] iconBindings)
        {
            scenario = battleScenario;
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
            for (int index = 0; index < entries.Length; index++)
            {
                Spawn(entries[index], index);
            }

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
            Sprite classIcon = ResolveClassIcon(definition != null ? definition.vehicleClass : VehicleClass.HeavyTank);
            TankClassIconPresenter iconPresenter = tank.GetComponent<TankClassIconPresenter>();
            if (iconPresenter == null) iconPresenter = tank.AddComponent<TankClassIconPresenter>();
            iconPresenter.Configure(classIcon, entry.team == TeamId.TeamA, 1.7f);
            TeamMember member = tank.GetComponent<TeamMember>();
            if (member == null) member = tank.AddComponent<TeamMember>();
            member.Configure(entry.team, entry.playerControlled);

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

        private void ValidateConfiguration()
        {
            if (scenario == null) throw new InvalidOperationException("BattleDirector has no scenario.");
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
