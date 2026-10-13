using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Turns the events a battle already raises into sound. It owns no game rule: it reads the arena's
    /// audio profile by map ID, forwards combat events to the music layer and plays the battle flow
    /// signals. Scenes need no new references, because the component is attached by
    /// <see cref="BattleDirector"/> the same way the diagnostics overlay is.
    /// </summary>
    public sealed class BattleAudioDirector : MonoBehaviour
    {
        private readonly Dictionary<CaptureBase, BaseCaptureState> baseStates = new Dictionary<CaptureBase, BaseCaptureState>();
        private readonly List<System.Action> unsubscribers = new List<System.Action>();

        private BattleDirector director;
        private WotAudioClipLibrary library;
        private BattleAudioProfile profile;
        private bool battleEnded;
        private string status = "звук боя не подключён";

        public BattleMusicController Music { get; private set; }
        public BattleFlowAudioController Flow { get; private set; }
        public CrewVoiceController Crew { get; private set; }
        public BattleAudioProfile Profile => profile;
        public WotAudioClipLibrary Library => library;
        public bool HasProfile => profile != null;
        public string Status => status;

        /// <summary>
        /// Attaches the audio layer to a battle. Called from <see cref="BattleDirector.Awake"/>, so the
        /// loading track covers the moment between the scene waking up and the tanks appearing.
        /// </summary>
        public static BattleAudioDirector Attach(BattleDirector battleDirector, bool startLoadingMusic = true)
        {
            if (battleDirector == null)
            {
                return null;
            }

            BattleAudioDirector audio = battleDirector.gameObject.GetComponent<BattleAudioDirector>();
            if (audio == null)
            {
                audio = battleDirector.gameObject.AddComponent<BattleAudioDirector>();
            }

            audio.Configure(battleDirector, startLoadingMusic);
            return audio;
        }

        public void Configure(BattleDirector battleDirector, bool startLoadingMusic)
        {
            Unsubscribe();
            director = battleDirector;
            library = WotAudioClipLibrary.Shared;

            // The map never changes between Awake and Start, so the profile can be resolved here even in
            // a laboratory that replaces its scenario at runtime.
            string mapId = ResolveMapId();
            profile = BattleAudioProfileCatalog.Load().Find(mapId);
            if (profile == null)
            {
                status = $"для арены '{mapId}' нет профиля звука";
                return;
            }

            Music ??= gameObject.AddComponent<BattleMusicController>();
            Music.Configure(profile, library);
            Flow ??= gameObject.AddComponent<BattleFlowAudioController>();
            Flow.Configure(profile, library);
            Subscribe();
            status = "звук боя готов";

            if (startLoadingMusic)
            {
                Music.BeginLoading();
            }
        }

        /// <summary>
        /// Starts the battle layer. Called at the end of spawning, so the loading track ends exactly when
        /// the machines exist and the announcement belongs to the first battle frame.
        /// </summary>
        public void BeginBattle()
        {
            if (profile == null && director != null)
            {
                // A scene whose map ID only became known after Awake gets its second chance here.
                Configure(director, false);
            }

            // Crew voice depends on the player's machine, which only exists once the battle spawned, and
            // it is not tied to an arena: a map without an audio profile still gets its crew lines. The
            // component goes on that machine, because its own projectiles look for their crew there.
            Crew = CrewVoiceController.AttachTo(
                director != null ? director.PlayerMember : null,
                library,
                null,
                director != null ? director.SpawnedMembers : null,
                director != null ? director.Bases : null);

            if (profile == null)
            {
                status = Crew != null ? "звучит только экипаж: у арены нет профиля звука" : status;
                return;
            }

            Music.BeginBattle(Time.time);
            Flow.StartAmbience();
            Crew?.PlayBattleStart();
            status = "бой звучит";
        }

        public void RegisterEvent(BattleAudioEventKind kind)
        {
            Music?.RegisterEvent(kind, Time.time);
        }

        /// <summary>Stops every source; used when a battle scene is torn down.</summary>
        public void StopAll()
        {
            Music?.Stop();
            Flow?.StopAll();
            Crew?.StopAll();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            StopAll();
        }

        private string ResolveMapId()
        {
            if (director != null && director.TacticalMap != null && !string.IsNullOrWhiteSpace(director.TacticalMap.mapId))
            {
                return director.TacticalMap.mapId;
            }

            return director != null && director.Scenario != null ? director.Scenario.sceneId : string.Empty;
        }

        private void Subscribe()
        {
            if (director == null)
            {
                return;
            }

            director.TankSpawned += OnTankSpawned;
            System.Action<TeamId, int, int> livingCounts = OnLivingCountsChanged;
            director.LivingCountsChanged += livingCounts;
            unsubscribers.Add(() => director.LivingCountsChanged -= livingCounts);

            foreach (TeamMember member in director.SpawnedMembers)
            {
                HookMember(member);
            }

            foreach (CaptureBase captureBase in director.Bases)
            {
                if (captureBase == null)
                {
                    continue;
                }

                CaptureBase captured = captureBase;
                System.Action<CaptureBase> changed = OnBaseChanged;
                System.Action<CaptureBase, TeamId, TeamId> capturedHandler = OnBaseCaptured;
                captured.Changed += changed;
                captured.Captured += capturedHandler;
                baseStates[captured] = captured.State;
                unsubscribers.Add(() =>
                {
                    if (captured == null) return;
                    captured.Changed -= changed;
                    captured.Captured -= capturedHandler;
                });
            }
        }

        private void Unsubscribe()
        {
            if (director != null)
            {
                director.TankSpawned -= OnTankSpawned;
            }

            foreach (System.Action unsubscriber in unsubscribers)
            {
                unsubscriber();
            }

            unsubscribers.Clear();
            baseStates.Clear();
        }

        private void OnTankSpawned(TeamMember member)
        {
            HookMember(member);
        }

        private void HookMember(TeamMember member)
        {
            if (member == null)
            {
                return;
            }

            // A battle that spawns machines late still has to give their crew the new victims to report.
            Crew?.Watch(member);

            WeaponController weapon = member.GetComponent<WeaponController>();
            if (weapon != null)
            {
                System.Action<WeaponController> fired = _ => RegisterEvent(BattleAudioEventKind.Shot);
                weapon.Fired += fired;
                unsubscribers.Add(() => weapon.Fired -= fired);
            }

            TankArmor armor = member.GetComponentInChildren<TankArmor>();
            if (armor != null)
            {
                System.Action<TankArmor, ArmorImpactObservation> impact = (_, observation) =>
                    RegisterEvent(WeightedEventFor(observation.Outcome), Time.time, ObservationWeight(observation));
                armor.ImpactResolved += impact;
                unsubscribers.Add(() => armor.ImpactResolved -= impact);
            }

            TankHealth health = member.GetComponent<TankHealth>();
            if (health != null)
            {
                System.Action<TankHealth> destroyed = _ => RegisterEvent(BattleAudioEventKind.Destroyed);
                health.Destroyed += destroyed;
                unsubscribers.Add(() => health.Destroyed -= destroyed);
            }
        }

        private static BattleAudioEventKind WeightedEventFor(ImpactOutcome outcome)
        {
            switch (outcome)
            {
                case ImpactOutcome.Penetrated: return BattleAudioEventKind.Penetrated;
                case ImpactOutcome.Blocked: return BattleAudioEventKind.Blocked;
                default: return BattleAudioEventKind.Ricochet;
            }
        }

        private static float ObservationWeight(ArmorImpactObservation observation)
        {
            // A heavy hit tenses the music more than a scratch, but the scale stays bounded so one
            // enormous shell cannot pin the layer to intensive forever.
            float baseWeight = BattleMusicIntensity.WeightFor(WeightedEventFor(observation.Outcome));
            return Mathf.Clamp(baseWeight + observation.Damage * 0.01f, 0f, 1.5f);
        }

        private void RegisterEvent(BattleAudioEventKind kind, float time, float weight)
        {
            if (Music == null || Music.State != BattleMusicState.Battle)
            {
                return;
            }

            Music.IntensityModel.Register(kind, time, weight);
        }

        private void OnBaseChanged(CaptureBase captureBase)
        {
            if (captureBase == null)
            {
                return;
            }

            BaseCaptureState previous = baseStates.TryGetValue(captureBase, out BaseCaptureState stored)
                ? stored
                : BaseCaptureState.Neutral;
            baseStates[captureBase] = captureBase.State;
            Flow?.ApplyCaptureState(captureBase.BaseId, captureBase.State);
            if (captureBase.State == BaseCaptureState.Contested && previous != BaseCaptureState.Contested)
            {
                RegisterEvent(BattleAudioEventKind.CaptureContested);
            }
        }

        private void OnBaseCaptured(CaptureBase captureBase, TeamId previousOwner, TeamId newOwner)
        {
            // The completion line is crew voice, so the crew controller owns it; this only feeds the
            // intensity model that decides which music layer plays.
            RegisterEvent(BattleAudioEventKind.Captured);
        }

        private void OnLivingCountsChanged(TeamId team, int teamAAlive, int teamBAlive)
        {
            if (battleEnded || (teamAAlive > 0 && teamBAlive > 0))
            {
                return;
            }

            battleEnded = true;
            // Without a player-controlled machine — a laboratory duel — the home side is Team A, which is
            // also the ally side everywhere else in the interface.
            TeamId playerTeam = director != null && director.PlayerMember != null
                ? director.PlayerMember.Team
                : TeamId.TeamA;
            TeamId surviving = teamAAlive > 0 ? TeamId.TeamA : teamBAlive > 0 ? TeamId.TeamB : TeamId.Neutral;
            Flow?.PlayBattleEnd(false);
            Music?.EnterResult(playerTeam, surviving);
            status = surviving == TeamId.Neutral
                ? "бой окончен: ничья"
                : $"бой окончен: победила команда {surviving}";
        }
    }
}
