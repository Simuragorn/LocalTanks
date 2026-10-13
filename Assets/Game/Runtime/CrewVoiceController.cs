using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Plays the crew voice of the player's own vehicle. It is deliberately attached to the battle rather
    /// than to a tank, because the lines describe what the player's crew heard and saw: their own shot
    /// results, their kills, their loss of a base and the loss of their machine.
    /// </summary>
    /// <remarks>
    /// Only the player's crew speaks. Bots are thirteen other virtual players, and giving every one of
    /// them a radio would turn a battle into noise while telling the player nothing new; the same reason
    /// keeps bot quick commands in text form.
    ///
    /// A kill needs two steps rather than one. `TankArmor.ResolveImpact` applies the damage and raises
    /// `TankHealth.Destroyed` while it runs, so a machine destroyed by the player reports itself before
    /// the projectile knows the outcome. The projectile therefore marks its victim before resolving and
    /// reports the result afterwards, and the generic "an enemy was destroyed" line stays silent for a
    /// victim the player is shooting at that very moment.
    /// </remarks>
    public sealed class CrewVoiceController : MonoBehaviour
    {
        private readonly List<System.Action> unsubscribers = new List<System.Action>();
        private readonly Dictionary<CrewVoiceRole, float> lastPlayedAt = new Dictionary<CrewVoiceRole, float>();

        private WotAudioClipLibrary library;
        private CrewVoiceProfileCatalog catalog;
        private AudioSource voiceSource;
        private TeamMember playerMember;
        private TeamId playerTeam = TeamId.Neutral;
        private TankNation playerNation = TankNation.USSR;
        private TeamMember markedVictim;
        private float lastLineAt = float.NegativeInfinity;
        private bool playerDestroyed;

        public bool IsActive => playerMember != null;
        public string Language { get; private set; } = string.Empty;
        public string LastClipId { get; private set; } = string.Empty;
        public string LastRole { get; private set; } = "реплик ещё не было";
        public int PlayCount { get; private set; }

        /// <summary>The player's own shell destroyed the machine the crew just shot at.</summary>
        public int SuppressedDuplicates { get; private set; }

        /// <summary>The same phrase came up again inside its cooldown, as an automatic gun keeps hitting.</summary>
        public int RepeatSuppressedCount { get; private set; }

        /// <summary>The crew would have talked over itself, so the line waited for nothing and was dropped.</summary>
        public int SpeechSuppressedCount { get; private set; }

        /// <summary>The crew voice level: the profile's own volume under the player's `Voice` channel.</summary>
        public float Volume => catalog != null
            ? catalog.volume * PlayerSettingsStore.VoiceMultiplier
            : 0f;

        public void Configure(
            BattleDirector battleDirector,
            WotAudioClipLibrary clipLibrary,
            CrewVoiceProfileCatalog crewCatalog = null)
        {
            List<TeamMember> members = new List<TeamMember>();
            List<CaptureBase> bases = new List<CaptureBase>();
            if (battleDirector != null)
            {
                members.AddRange(battleDirector.SpawnedMembers);
                foreach (CaptureBase captureBase in battleDirector.Bases)
                {
                    if (captureBase != null) bases.Add(captureBase);
                }
            }

            Configure(battleDirector != null ? battleDirector.PlayerMember : null, clipLibrary, crewCatalog, members, bases);
            if (battleDirector != null)
            {
                battleDirector.TankSpawned += OnTankSpawned;
                unsubscribers.Add(() => battleDirector.TankSpawned -= OnTankSpawned);
            }
        }

        /// <summary>
        /// Puts the crew on the machine that hears it. The component belongs to the player's tank rather
        /// than to the battle, because the projectile of that machine finds its own crew by walking the
        /// shooter, and because a bot simply has no crew voice to find.
        /// </summary>
        public static CrewVoiceController AttachTo(
            TeamMember player,
            WotAudioClipLibrary clipLibrary,
            CrewVoiceProfileCatalog crewCatalog = null,
            IEnumerable<TeamMember> members = null,
            IEnumerable<CaptureBase> bases = null)
        {
            if (player == null)
            {
                return null;
            }

            CrewVoiceController crew = player.GetComponent<CrewVoiceController>();
            if (crew == null)
            {
                crew = player.gameObject.AddComponent<CrewVoiceController>();
            }

            crew.Configure(player, clipLibrary, crewCatalog, members, bases);
            return crew;
        }

        /// <summary>Starts listening to one more machine, for a battle that spawns machines late.</summary>
        public void Watch(TeamMember member)
        {
            HookMember(member);
        }

        /// <summary>
        /// Wires the crew of one machine. The player's machine and the machines and bases that can produce
        /// a line are passed in, so the controller is testable without a whole battle scene.
        /// </summary>
        public void Configure(
            TeamMember player,
            WotAudioClipLibrary clipLibrary,
            CrewVoiceProfileCatalog crewCatalog = null,
            IEnumerable<TeamMember> members = null,
            IEnumerable<CaptureBase> bases = null)
        {
            Unsubscribe();
            library = clipLibrary ?? WotAudioClipLibrary.Shared;
            catalog = crewCatalog ?? CrewVoiceProfileCatalog.Load();
            voiceSource ??= CreateSource();
            playerDestroyed = false;
            markedVictim = null;
            lastPlayedAt.Clear();
            lastLineAt = float.NegativeInfinity;
            playerMember = player;

            if (playerMember == null)
            {
                // A laboratory duel has no player-controlled machine, so there is no crew to hear.
                LastRole = "нет машины игрока: голос экипажа молчит";
                return;
            }

            playerTeam = playerMember.Team;
            TankDefinition definition = playerMember.Definition;
            playerNation = definition != null ? definition.nation : TankNation.USSR;
            Language = catalog.LanguageFor(playerNation);

            if (members != null)
            {
                foreach (TeamMember member in members)
                {
                    HookMember(member);
                }
            }

            if (bases != null)
            {
                foreach (CaptureBase captureBase in bases)
                {
                    CaptureBase captured = captureBase;
                    System.Action<CaptureBase, TeamId, TeamId> handler = OnBaseCaptured;
                    captured.Captured += handler;
                    unsubscribers.Add(() =>
                    {
                        if (captured != null) captured.Captured -= handler;
                    });
                }
            }
        }

        public void PlayBattleStart()
        {
            Play(CrewVoiceRole.BattleStart);
        }

        /// <summary>
        /// Marks the machine the player's shell is about to hit. Called before the impact is resolved so
        /// the generic kill line can yield to the player's own line for the same victim.
        /// </summary>
        public void MarkIncomingHit(TankArmor victim)
        {
            if (victim == null)
            {
                return;
            }

            TeamMember member = victim.GetComponentInParent<TeamMember>();
            if (member != null && member.Team != playerTeam)
            {
                markedVictim = member;
            }
        }

        /// <summary>Reports what the player's own shell did to the machine it hit.</summary>
        public void ReportShotResult(TankArmor victim, ImpactOutcome outcome, bool destroyed)
        {
            markedVictim = null;
            if (!IsActive || playerDestroyed || victim == null)
            {
                return;
            }

            TeamMember member = victim.GetComponentInParent<TeamMember>();
            if (member == null)
            {
                return;
            }

            if (destroyed)
            {
                Play(member.Team == playerTeam ? CrewVoiceRole.AllyDestroyed : CrewVoiceRole.EnemyDestroyedByPlayer);
                return;
            }

            // A hit on a friendly machine is not a line of its own; only losing one is.
            if (member.Team == playerTeam)
            {
                return;
            }

            switch (outcome)
            {
                case ImpactOutcome.Penetrated:
                    Play(CrewVoiceRole.Penetrated);
                    break;
                case ImpactOutcome.Blocked:
                    Play(CrewVoiceRole.NotPenetrated);
                    break;
                default:
                    Play(CrewVoiceRole.Ricochet);
                    break;
            }
        }

        /// <summary>Reports a base changing hands from the player's point of view.</summary>
        public void ReportBaseCaptured(TeamId previousOwner, TeamId newOwner)
        {
            // A dead crew has nothing left to say, so losing a base after the player's machine is gone
            // stays silent; the battle itself continues without them.
            if (!IsActive || playerDestroyed)
            {
                return;
            }

            if (newOwner == playerTeam)
            {
                Play(CrewVoiceRole.BaseCaptured);
            }
            else if (previousOwner == playerTeam)
            {
                Play(CrewVoiceRole.BaseLost);
            }
        }

        /// <summary>Plays one role in the player's own language. Returns whether a line was asked for.</summary>
        public bool Play(CrewVoiceRole role)
        {
            if (!IsActive || catalog == null)
            {
                return false;
            }

            string clipId = catalog.ClipIdFor(role, playerNation);
            if (string.IsNullOrWhiteSpace(clipId))
            {
                LastRole = $"{role}: для роли нет шаблона клипа";
                return false;
            }

            // The loss of the player's own machine is the one line that overrides pacing: a crew that
            // stays quiet while its tank burns would be worse than a repeated phrase.
            bool critical = role == CrewVoiceRole.VehicleDestroyed;
            float now = Time.time;
            if (!critical && catalog.repeatCooldownSeconds > 0f &&
                lastPlayedAt.TryGetValue(role, out float playedAt) &&
                now - playedAt < catalog.repeatCooldownSeconds)
            {
                RepeatSuppressedCount++;
                LastRole = $"{role}: повтор пропущен, фраза звучала {now - playedAt:0.0} с назад";
                return false;
            }

            if (!critical && catalog.speechCooldownSeconds > 0f &&
                now - lastLineAt < catalog.speechCooldownSeconds)
            {
                SpeechSuppressedCount++;
                LastRole = $"{role}: пропущено, экипаж говорил {now - lastLineAt:0.0} с назад";
                return false;
            }

            AudioClip clip = library != null ? library.Load(clipId) : null;
            LastClipId = clipId;
            LastRole = $"{role} ({catalog.DescriptionFor(role)})";
            if (clip == null || voiceSource == null)
            {
                // A language whose clips were never extracted stays silent instead of blocking the battle.
                return false;
            }

            voiceSource.PlayOneShot(clip, Mathf.Clamp01(Volume));
            lastPlayedAt[role] = now;
            lastLineAt = now;
            PlayCount++;
            return true;
        }

        public void StopAll()
        {
            if (voiceSource != null)
            {
                voiceSource.Stop();
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            foreach (System.Action unsubscriber in unsubscribers)
            {
                unsubscriber();
            }

            unsubscribers.Clear();
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

            TankHealth health = member.GetComponent<TankHealth>();
            if (health == null)
            {
                return;
            }

            System.Action<TankHealth> destroyed = _ => OnMemberDestroyed(member);
            health.Destroyed += destroyed;
            unsubscribers.Add(() =>
            {
                if (health != null) health.Destroyed -= destroyed;
            });
        }

        private void OnMemberDestroyed(TeamMember member)
        {
            if (!IsActive || member == null)
            {
                return;
            }

            if (member == playerMember)
            {
                playerDestroyed = true;
                Play(CrewVoiceRole.VehicleDestroyed);
                return;
            }

            if (playerDestroyed || member.Team == playerTeam)
            {
                return;
            }

            if (member == markedVictim)
            {
                // The player's own shell is resolving right now; its line is played by ReportShotResult.
                SuppressedDuplicates++;
                return;
            }

            Play(CrewVoiceRole.EnemyDestroyed);
        }

        private void OnBaseCaptured(CaptureBase captureBase, TeamId previousOwner, TeamId newOwner)
        {
            ReportBaseCaptured(previousOwner, newOwner);
        }

        private AudioSource CreateSource()
        {
            Transform existing = transform.Find("CrewVoice");
            if (existing != null)
            {
                AudioSource found = existing.GetComponent<AudioSource>();
                if (found != null)
                {
                    return found;
                }
            }

            GameObject child = new GameObject("CrewVoice");
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            return source;
        }
    }
}
