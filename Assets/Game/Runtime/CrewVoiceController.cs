using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Plays the crew voice of the player's own vehicle. It is deliberately attached to that vehicle:
    /// the lines describe what the player's crew personally did, plus the destruction of their machine.
    /// </summary>
    /// <remarks>
    /// Only the player's crew speaks. Bots are thirteen other virtual players, and giving every one of
    /// them a radio would turn a battle into noise while telling the player nothing new; the same reason
    /// keeps bot quick commands in text form.
    ///
    /// Enemy and allied bot events are intentionally not subscribed. A shell reports a result only when
    /// it found this component on its shooter, which makes ownership structural rather than a team check
    /// that can accidentally let another machine speak through the player's crew.
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
        private float lastLineAt = float.NegativeInfinity;
        private bool playerDestroyed;

        public bool IsActive => playerMember != null;
        public string Language { get; private set; } = string.Empty;

        /// <summary>The voice the nation's crew speaks with: `male` or `female`.</summary>
        public string Voice { get; private set; } = string.Empty;

        public string LastClipId { get; private set; } = string.Empty;
        public string LastRole { get; private set; } = "реплик ещё не было";
        public int PlayCount { get; private set; }

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
            Configure(battleDirector != null ? battleDirector.PlayerMember : null, clipLibrary, crewCatalog);
        }

        /// <summary>
        /// Puts the crew on the machine that hears it. The component belongs to the player's tank rather
        /// than to the battle, because the projectile of that machine finds its own crew by walking the
        /// shooter, and because a bot simply has no crew voice to find.
        /// </summary>
        public static CrewVoiceController AttachTo(
            TeamMember player,
            WotAudioClipLibrary clipLibrary,
            CrewVoiceProfileCatalog crewCatalog = null)
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

            crew.Configure(player, clipLibrary, crewCatalog);
            return crew;
        }

        /// <summary>
        /// Wires the crew to the player's machine only, so the controller is testable without a whole battle.
        /// </summary>
        public void Configure(
            TeamMember player,
            WotAudioClipLibrary clipLibrary,
            CrewVoiceProfileCatalog crewCatalog = null)
        {
            Unsubscribe();
            library = clipLibrary ?? WotAudioClipLibrary.Shared;
            catalog = crewCatalog ?? CrewVoiceProfileCatalog.Load();
            voiceSource ??= CreateSource();
            playerDestroyed = false;
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
            RefreshVoiceSelection();

            // Only the player's health is observed. Bot deaths and base events describe actions performed
            // elsewhere in the battle and therefore must never trigger this crew.
            HookMember(playerMember);
        }

        public void PlayBattleStart()
        {
            Play(CrewVoiceRole.BattleStart);
        }

        /// <summary>Reports what the player's own shell did to the machine it hit.</summary>
        public void ReportShotResult(TankArmor victim, ImpactOutcome outcome, bool destroyed)
        {
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

        /// <summary>Plays one role in the player's own language. Returns whether a line was asked for.</summary>
        public bool Play(CrewVoiceRole role)
        {
            if (!IsActive || catalog == null)
            {
                return false;
            }

            RefreshVoiceSelection();
            string clipId = catalog.ClipIdForLanguage(role, Language, Voice);
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

        private void RefreshVoiceSelection()
        {
            if (catalog == null)
            {
                Language = string.Empty;
                Voice = string.Empty;
                return;
            }

            Language = PlayerSettingsStore.CrewLanguage == CrewLanguageMode.Russian
                ? "ru"
                : catalog.LanguageFor(playerNation);
            Voice = PlayerSettingsStore.CrewGender == CrewVoiceGender.Female ? "female" : "male";
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
            if (!IsActive || member == null || member != playerMember)
            {
                return;
            }

            playerDestroyed = true;
            Play(CrewVoiceRole.VehicleDestroyed);
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
