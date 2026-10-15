using System;
using UnityEngine;

namespace LocalTanks
{
    public sealed class Projectile2D : MonoBehaviour
    {
        [SerializeField] private LayerMask collisionMask = ~0;

        private Vector2 direction;
        private float speed;
        private float radius;
        private float penetration;
        private float nominalPenetration;
        private int damage;
        private float ricochetAngle;
        private float ricochetSpeedMultiplier;
        private float ricochetPenetrationMultiplier;
        private int remainingRicochets;
        private Transform ownerRoot;
        private ShellAudioProfile audioProfile;
        private CrewVoiceController crewVoice;
        private Collider2D temporarilyIgnoredCollider;
        private MatchRules matchRules;
        private TeamId shooterTeam = TeamId.Neutral;
        private TeamMember shooterMember;
        private string shellId = string.Empty;
        private ShotRandomization randomization = ShotRandomization.Identity;
        private CombatRules combatRules;
        private bool initialized;
        private readonly ProjectileTravelBudget travelBudget = new ProjectileTravelBudget();

        public Vector2 Direction => direction;
        public float Speed => speed;
        public float Penetration => penetration;
        public int RemainingRicochets => remainingRicochets;
        public ShotRandomization Randomization => randomization;

        public void Initialize(
            Vector2 travelDirection,
            float newSpeed,
            float newRadius,
            float lifetime,
            float range,
            Transform newOwnerRoot)
        {
            direction = travelDirection.normalized;
            speed = Mathf.Max(0f, newSpeed);
            radius = Mathf.Max(0.001f, newRadius);
            travelBudget.Reset(lifetime, range);
            ownerRoot = newOwnerRoot;
            initialized = true;
        }

        public void Initialize(Vector2 travelDirection, ShellDefinition shell, Transform newOwnerRoot)
        {
            Initialize(travelDirection, shell, newOwnerRoot, null);
        }

        public void Initialize(
            Vector2 travelDirection,
            ShellDefinition shell,
            Transform newOwnerRoot,
            MatchRules rules)
        {
            Initialize(travelDirection, shell, newOwnerRoot, rules, ShotRandomization.Identity);
        }

        public void Initialize(
            Vector2 travelDirection,
            ShellDefinition shell,
            Transform newOwnerRoot,
            MatchRules rules,
            ShotRandomization shotRandomization)
        {
            if (shell == null)
            {
                throw new System.ArgumentNullException(nameof(shell));
            }

            Initialize(
                travelDirection,
                shell.speed,
                shell.radius,
                shell.lifetimeSeconds,
                shell.maximumRange,
                newOwnerRoot);
            randomization = shotRandomization;
            nominalPenetration = Mathf.Max(0f, shell.penetration);
            penetration = CombatRandomMath.ApplyPenetration(
                nominalPenetration,
                randomization.PenetrationMultiplier);
            damage = CombatRandomMath.ApplyDamage(shell.damage, randomization.DamageMultiplier);
            ricochetAngle = Mathf.Clamp(shell.ricochetAngle, 0f, 90f);
            ricochetSpeedMultiplier = Mathf.Clamp01(shell.ricochetSpeedMultiplier);
            ricochetPenetrationMultiplier = Mathf.Clamp01(shell.ricochetPenetrationMultiplier);
            remainingRicochets = Mathf.Max(0, shell.maximumRicochets);
            audioProfile = shell.audio;
            crewVoice = FindCrewVoice(newOwnerRoot);
            matchRules = rules;
            combatRules = rules?.combat ?? new CombatRules();
            shellId = shell.id ?? string.Empty;
            shooterMember = newOwnerRoot != null ? newOwnerRoot.GetComponentInChildren<TeamMember>() : null;
            shooterTeam = shooterMember != null ? shooterMember.Team : TeamId.Neutral;
        }

        /// <summary>
        /// True when this shell may damage the machine it hit. A wreck is always a physical obstacle — it is
        /// cover — so only a living machine of the shooter's own team is passed through when the match rules
        /// forbid friendly fire. Public because the decision, not the collision, is what the rule is about.
        /// </summary>
        public bool CanDamageMachine(TankArmor armor)
        {
            if (armor == null || matchRules == null || matchRules.friendlyFire)
            {
                return true;
            }

            TankHealth health = armor.GetComponent<TankHealth>();
            if (health != null && health.IsDestroyed)
            {
                return true;
            }

            TeamMember victim = armor.GetComponentInParent<TeamMember>();
            return victim == null || matchRules.AllowsDamageTo(shooterTeam, victim.Team);
        }

        /// <summary>
        /// The crew voice of the machine that fired, or null when it has none. Only the player's machine
        /// carries one, so a bot's shell resolves to nothing and its impacts stay silent.
        /// </summary>
        public static CrewVoiceController FindCrewVoice(Transform ownerRoot)
        {
            return ownerRoot != null ? ownerRoot.GetComponentInChildren<CrewVoiceController>() : null;
        }

        private void FixedUpdate()
        {
            if (!initialized)
            {
                return;
            }

            RefreshIgnoredCollider();

            float stepDistance = travelBudget.Consume(speed * Time.fixedDeltaTime, Time.fixedDeltaTime);
            RaycastHit2D[] hits = Physics2D.CircleCastAll(
                transform.position,
                radius,
                direction,
                stepDistance,
                collisionMask);
            Array.Sort(hits, PhysicsQueryOrder.CompareHits);

            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider == null ||
                    hit.collider.isTrigger ||
                    hit.collider.GetComponentInParent<FirePassThroughObstacle>() != null ||
                    hit.collider == temporarilyIgnoredCollider ||
                    IsOwnedCollider(hit.collider.transform))
                {
                    continue;
                }

                transform.position = hit.centroid;
                TankArmor armor = hit.collider.GetComponentInParent<TankArmor>();
                if (armor != null && !CanDamageMachine(armor))
                {
                    // Friendly fire is switched off, so the shell passes through a living ally and the next hit
                    // along the same cast is considered instead of this one.
                    continue;
                }

                if (armor == null)
                {
                    DestructibleObstacle obstacle = hit.collider.GetComponentInParent<DestructibleObstacle>();
                    obstacle?.ApplyDamage(damage);
                    CombatAudioEmitter.PlayImpact(audioProfile, ImpactOutcome.Blocked, hit.point);
                    // The map answers with dust, chips or a splash. The projectile does not know which
                    // surface it landed on; the effect layer resolves that from the battle's map.
                    CombatVfxEmitter.ShellImpact(hit.point, hit.normal, radius);
                    Destroy(gameObject);
                    return;
                }

                // The crew is told about the victim before the impact is resolved: damage is applied inside
                // ResolveImpact, so a machine this shell destroys reports its own destruction first and the
                // generic "enemy destroyed" line has to yield to the player's own line for the same victim.
                crewVoice?.MarkIncomingHit(armor);

                ImpactResult result = armor.ResolveImpact(
                    direction,
                    hit.point,
                    hit.normal,
                    penetration,
                    speed,
                    damage,
                    ricochetAngle,
                    ricochetSpeedMultiplier,
                    ricochetPenetrationMultiplier,
                    remainingRicochets,
                    nominalPenetration,
                    combatRules,
                    shooterTeam);

                TeamMember victim = armor.GetComponentInParent<TeamMember>();
                BattleTelemetry.Active?.RecordShot(
                    shooterMember,
                    victim,
                    result.Outcome.ToString(),
                    armor.LastAppliedDamage,
                    shellId,
                    randomization,
                    result.PenetrationChance);

                CombatAudioEmitter.PlayImpact(audioProfile, result.Outcome, hit.point);

                TankHealth victimHealth = armor.GetComponent<TankHealth>();
                crewVoice?.ReportShotResult(armor, result.Outcome,
                    victimHealth != null && victimHealth.IsDestroyed);

                if (result.Outcome != ImpactOutcome.Ricocheted)
                {
                    Destroy(gameObject);
                    return;
                }

                direction = result.OutgoingDirection;
                speed = result.OutgoingSpeed;
                penetration = result.RemainingPenetration;
                nominalPenetration *= ricochetPenetrationMultiplier;
                remainingRicochets--;
                temporarilyIgnoredCollider = hit.collider;
                transform.position = hit.centroid + hit.normal * (radius + 0.002f);
                transform.up = direction;
                return;
            }

            transform.position += (Vector3)(direction * stepDistance);
            if (travelBudget.IsExpired)
            {
                Destroy(gameObject);
            }
        }

        private void RefreshIgnoredCollider()
        {
            if (temporarilyIgnoredCollider == null)
            {
                return;
            }

            Vector2 position = transform.position;
            Vector2 closest = temporarilyIgnoredCollider.ClosestPoint(position);
            if (Vector2.Distance(position, closest) > radius + 0.002f)
            {
                temporarilyIgnoredCollider = null;
            }
        }

        private bool IsOwnedCollider(Transform candidate)
        {
            return ownerRoot != null && (candidate == ownerRoot || candidate.IsChildOf(ownerRoot));
        }
    }
}
