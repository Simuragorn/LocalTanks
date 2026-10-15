using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalTanks
{
    /// <summary>
    /// One screen-space owner for transient damage numbers and spotted enemies outside the camera.
    /// It writes into the battle UIDocument, so fourteen machines do not create fourteen screen-space layers
    /// and graphical proofs can render the exact same elements without an interactive game window.
    /// </summary>
    public sealed class DamagePopupLayer : MonoBehaviour
    {
        public const int PopupCapacity = 24;
        public const float PopupLifetimeSeconds = 1.25f;
        public const float PopupGrowthFraction = 0.35f;
        public const string AllyDamageClass = "damage-popup--ally";
        public const string EnemyDamageClass = "damage-popup--enemy";

        private readonly List<DamagePopup> popups = new List<DamagePopup>();
        private readonly Stack<Label> idlePopupLabels = new Stack<Label>();
        private readonly HashSet<TankArmor> subscribedArmor = new HashSet<TankArmor>();
        private readonly Dictionary<TeamMember, HealthBarView> healthBars = new Dictionary<TeamMember, HealthBarView>();
        private readonly Dictionary<TeamMember, IndicatorView> indicators = new Dictionary<TeamMember, IndicatorView>();
        private BattleRoster roster;
        private Camera worldCamera;
        private VisualElement root;
        private VisualElement overlay;

        public int ActivePopupCount => popups.Count;

        private void OnEnable()
        {
            TeamMember.MembershipChanged += OnMembershipChanged;
            RebindArmor();
        }

        private void OnDisable()
        {
            TeamMember.MembershipChanged -= OnMembershipChanged;
            foreach (TankArmor armor in subscribedArmor.ToArray())
            {
                Unsubscribe(armor);
            }

            ClearViews();
        }

        public void Configure(BattleRoster battleRoster, Camera camera, VisualElement uiRoot)
        {
            roster = battleRoster;
            worldCamera = camera;
            if (root != uiRoot)
            {
                ClearViews();
                root = uiRoot;
            }

            EnsureOverlay();
            RebindArmor();
        }

        private void Update()
        {
            if (roster == null || root == null)
            {
                return;
            }

            EnsureOverlay();
            Camera camera = worldCamera != null ? worldCamera : Camera.main;
            if (camera == null)
            {
                return;
            }

            RefreshPopups(camera);
            RefreshTankHealth(camera);
            RefreshIndicators(camera);
        }

        private void RefreshTankHealth(Camera camera)
        {
            HashSet<TeamMember> active = new HashSet<TeamMember>();
            foreach (BattleRosterEntry entry in roster.Allies.Concat(roster.Enemies))
            {
                if (entry.Member == null || !entry.IsAlive || !entry.IsAlly && !entry.IsCurrentlyVisible)
                {
                    continue;
                }

                active.Add(entry.Member);
                TankHealthBar component = entry.Member.GetComponent<TankHealthBar>();
                component?.SetExternalPresentation(true);
                if (!healthBars.TryGetValue(entry.Member, out HealthBarView view))
                {
                    view = CreateHealthBar();
                    healthBars.Add(entry.Member, view);
                    overlay.Add(view.Root);
                }

                float worldOffset = component != null ? component.WorldOffset : 1.45f;
                float width = component != null ? component.Width : TankHealthBar.DefaultWidth;
                float height = component != null ? component.Height : TankHealthBar.DefaultHeight;
                if (!TryProject(camera, entry.Member.transform.position + Vector3.up * worldOffset, out Vector2 point))
                {
                    view.Root.style.display = DisplayStyle.None;
                    continue;
                }

                view.Root.style.display = DisplayStyle.Flex;
                view.Root.style.left = point.x - width * 0.5f;
                view.Root.style.top = point.y - height * 0.5f;
                view.Root.style.width = width;
                view.Root.style.height = height;
                view.Fill.style.width = Length.Percent(entry.HealthRatio * 100f);
                view.Fill.style.backgroundColor = entry.TeamColor;
                view.Label.text = BattleHudController.FormatHitPoints(entry.LastKnownHitPoints, entry.MaximumHitPoints);
            }

            foreach (TeamMember member in healthBars.Keys.Where(member => !active.Contains(member)).ToArray())
            {
                member?.GetComponent<TankHealthBar>()?.SetExternalPresentation(false);
                healthBars[member].Root.RemoveFromHierarchy();
                healthBars.Remove(member);
            }
        }

        private static HealthBarView CreateHealthBar()
        {
            VisualElement root = new VisualElement { name = "world-health-bar", pickingMode = PickingMode.Ignore };
            root.AddToClassList("world-health-bar");
            VisualElement fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("world-health-fill");
            Label label = new Label { name = "world-health-number", pickingMode = PickingMode.Ignore };
            label.AddToClassList("world-health-number");
            root.Add(fill);
            root.Add(label);
            return new HealthBarView(root, fill, label);
        }

        private void RefreshPopups(Camera camera)
        {
            float now = Time.unscaledTime;
            for (int index = popups.Count - 1; index >= 0; index--)
            {
                DamagePopup popup = popups[index];
                float progress = Mathf.Clamp01((now - popup.StartedAt) / PopupLifetimeSeconds);
                if (progress >= 1f)
                {
                    ReleasePopup(index);
                    continue;
                }

                if (!TryProject(camera, popup.WorldPosition, out Vector2 point))
                {
                    popup.Label.style.display = DisplayStyle.None;
                    continue;
                }

                popup.Label.style.display = DisplayStyle.Flex;
                popup.Label.style.left = point.x - 45f;
                popup.Label.style.top = point.y - 38f - progress * 28f;
                float scale = PopupScale(progress);
                popup.Label.style.scale = new Scale(new Vector3(scale, scale, 1f));
                popup.Label.style.opacity = PopupOpacity(progress);
            }
        }

        private void RefreshIndicators(Camera camera)
        {
            TeamMember player = roster.PlayerMember;
            HashSet<TeamMember> active = new HashSet<TeamMember>();
            if (player == null)
            {
                HideUnusedIndicators(active);
                return;
            }

            Vector2 panelSize = PanelSize();
            float horizontalInset = panelSize.x >= 1000f ? 292f : 46f;
            float verticalInset = panelSize.y >= 700f ? 88f : 42f;
            Rect safeArea = new Rect(
                horizontalInset,
                verticalInset,
                Mathf.Max(1f, panelSize.x - horizontalInset * 2f),
                Mathf.Max(1f, panelSize.y - verticalInset * 2f));
            List<Rect> occupied = new List<Rect>();

            foreach (BattleRosterEntry entry in roster.Enemies)
            {
                if (!entry.IsAlive || !entry.IsCurrentlyVisible || entry.Member == null)
                {
                    continue;
                }

                Vector3 projected = camera.WorldToScreenPoint(entry.Member.transform.position);
                Vector2 screen = ToPanelBottomLeft(camera, projected);
                if (projected.z < 0f)
                {
                    screen = safeArea.center - (screen - safeArea.center);
                }

                if (!HudProjectionMath.TryPlaceAtEdge(screen, safeArea, out EdgeIndicatorPlacement placement))
                {
                    continue;
                }

                active.Add(entry.Member);
                if (!indicators.TryGetValue(entry.Member, out IndicatorView view))
                {
                    view = CreateIndicator(entry);
                    indicators.Add(entry.Member, view);
                    overlay.Add(view.Root);
                }

                float distanceMetres = GameScale.UnitsToMetres(
                    Vector2.Distance(player.transform.position, entry.Member.transform.position));
                view.Arrow.text = ArrowGlyph(placement.AngleDegrees);
                view.Text.text = FormatIndicator(entry.TankName, distanceMetres);
                view.Root.style.display = DisplayStyle.Flex;
                Vector2 position = ResolveIndicatorPosition(
                    new Vector2(
                        Mathf.Clamp(placement.Position.x - 88f, 4f, panelSize.x - 180f),
                        Mathf.Clamp(panelSize.y - placement.Position.y - 24f, 4f, panelSize.y - 54f)),
                    panelSize,
                    occupied);
                view.Root.style.left = position.x;
                view.Root.style.top = position.y;
                occupied.Add(new Rect(position.x, position.y, 176f, 52f));
            }

            HideUnusedIndicators(active);
        }

        private static Vector2 ResolveIndicatorPosition(
            Vector2 desired,
            Vector2 panelSize,
            IReadOnlyList<Rect> occupied)
        {
            for (int step = 0; step <= occupied.Count; step++)
            {
                float signedStep = step == 0 ? 0f : ((step + 1) / 2) * 56f * (step % 2 == 1 ? 1f : -1f);
                Vector2 candidate = new Vector2(
                    desired.x,
                    Mathf.Clamp(desired.y + signedStep, 4f, Mathf.Max(4f, panelSize.y - 54f)));
                Rect candidateRect = new Rect(candidate.x, candidate.y, 176f, 52f);
                if (!occupied.Any(candidateRect.Overlaps))
                {
                    return candidate;
                }
            }

            return desired;
        }

        private IndicatorView CreateIndicator(BattleRosterEntry entry)
        {
            VisualElement container = new VisualElement { pickingMode = PickingMode.Ignore };
            container.AddToClassList("offscreen-indicator");
            Label arrow = new Label { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList("offscreen-arrow");
            Image icon = new Image
            {
                sprite = entry.ClassIcon,
                tintColor = entry.TeamColor,
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            icon.AddToClassList("offscreen-class-icon");
            Label text = new Label { pickingMode = PickingMode.Ignore };
            text.AddToClassList("offscreen-text");
            VisualElement details = new VisualElement { pickingMode = PickingMode.Ignore };
            details.AddToClassList("offscreen-details");
            details.AddToClassList("lt-panel");
            details.AddToClassList("lt-panel--quiet");
            details.Add(icon);
            details.Add(text);
            container.Add(arrow);
            container.Add(details);
            return new IndicatorView(container, arrow, text);
        }

        private void HideUnusedIndicators(HashSet<TeamMember> active)
        {
            foreach (KeyValuePair<TeamMember, IndicatorView> pair in indicators)
            {
                if (!active.Contains(pair.Key)) pair.Value.Root.style.display = DisplayStyle.None;
            }
        }

        private bool TryProject(Camera camera, Vector3 worldPosition, out Vector2 point)
        {
            Vector3 projected = camera.WorldToScreenPoint(worldPosition);
            if (projected.z <= 0f)
            {
                point = default;
                return false;
            }

            Vector2 bottomLeft = ToPanelBottomLeft(camera, projected);
            Vector2 size = PanelSize();
            point = new Vector2(bottomLeft.x, size.y - bottomLeft.y);
            return point.x >= 0f && point.x <= size.x && point.y >= 0f && point.y <= size.y;
        }

        private Vector2 ToPanelBottomLeft(Camera camera, Vector3 projected)
        {
            Vector2 size = PanelSize();
            return new Vector2(
                projected.x / Mathf.Max(1f, camera.pixelWidth) * size.x,
                projected.y / Mathf.Max(1f, camera.pixelHeight) * size.y);
        }

        private Vector2 PanelSize()
        {
            float width = root != null ? root.resolvedStyle.width : 0f;
            float height = root != null ? root.resolvedStyle.height : 0f;
            return new Vector2(
                width > 1f ? width : Mathf.Max(1f, Screen.width),
                height > 1f ? height : Mathf.Max(1f, Screen.height));
        }

        private void OnImpactResolved(TankArmor armor, ArmorImpactObservation observation)
        {
            if (armor == null || observation.Damage <= 0 || roster == null) return;
            TeamMember member = armor.GetComponentInParent<TeamMember>();
            BattleRosterEntry entry = roster.Find(member);
            if (entry == null || !entry.IsAlly && !entry.IsCurrentlyVisible) return;

            EnsureOverlay();
            Vector2 point = armor.LastImpactPoint;
            if (point == Vector2.zero && member != null) point = member.transform.position;
            if (idlePopupLabels.Count == 0 && popups.Count > 0)
            {
                // The pool has a hard ceiling. Under simultaneous autocannon fire the oldest number is
                // already fading, so recycle it instead of allocating another UI hierarchy.
                ReleasePopup(0);
            }

            if (idlePopupLabels.Count == 0) return;
            Label label = idlePopupLabels.Pop();
            label.text = FormatDamage(observation.Damage);
            label.style.display = DisplayStyle.Flex;
            label.AddToClassList("damage-popup");
            label.AddToClassList(DamageClass(observation.AttackerTeam, member.Team));
            popups.Add(new DamagePopup(point, Time.unscaledTime, label));
        }

        private void OnMembershipChanged(TeamMember member, bool added)
        {
            TankArmor armor = member != null ? member.GetComponent<TankArmor>() : null;
            if (armor != null)
            {
                if (added) Subscribe(armor);
                else Unsubscribe(armor);
            }

            if (!added && member != null)
            {
                member.GetComponent<TankHealthBar>()?.SetExternalPresentation(false);
                if (healthBars.TryGetValue(member, out HealthBarView health)) health.Root.RemoveFromHierarchy();
                healthBars.Remove(member);
                if (indicators.TryGetValue(member, out IndicatorView indicator)) indicator.Root.RemoveFromHierarchy();
                indicators.Remove(member);
            }
        }

        private void RebindArmor()
        {
            foreach (TankArmor armor in subscribedArmor.ToArray()) Unsubscribe(armor);
            foreach (TeamMember member in TeamMember.ActiveMembers)
                Subscribe(member != null ? member.GetComponent<TankArmor>() : null);
        }

        private void Subscribe(TankArmor armor)
        {
            if (armor == null || !subscribedArmor.Add(armor)) return;
            armor.ImpactResolved += OnImpactResolved;
        }

        private void Unsubscribe(TankArmor armor)
        {
            if (armor == null || !subscribedArmor.Remove(armor)) return;
            armor.ImpactResolved -= OnImpactResolved;
        }

        private void EnsureOverlay()
        {
            if (overlay != null || root == null) return;
            overlay = new VisualElement { name = "combat-screen-overlay", pickingMode = PickingMode.Ignore };
            overlay.AddToClassList("combat-screen-overlay");
            root.Add(overlay);
            for (int index = 0; index < PopupCapacity; index++)
            {
                Label label = new Label { pickingMode = PickingMode.Ignore };
                label.style.display = DisplayStyle.None;
                overlay.Add(label);
                idlePopupLabels.Push(label);
            }
        }

        private void ReleasePopup(int index)
        {
            DamagePopup popup = popups[index];
            popups.RemoveAt(index);
            popup.Label.RemoveFromClassList("damage-popup");
            popup.Label.RemoveFromClassList(AllyDamageClass);
            popup.Label.RemoveFromClassList(EnemyDamageClass);
            popup.Label.style.display = DisplayStyle.None;
            idlePopupLabels.Push(popup.Label);
        }

        private void ClearViews()
        {
            foreach (TeamMember member in healthBars.Keys)
            {
                member?.GetComponent<TankHealthBar>()?.SetExternalPresentation(false);
            }
            popups.Clear();
            idlePopupLabels.Clear();
            healthBars.Clear();
            indicators.Clear();
            overlay?.RemoveFromHierarchy();
            overlay = null;
        }

        public static string FormatDamage(int damage) => $"-{Mathf.Max(0, damage)}";

        public static string DamageClass(TeamId attackerTeam, TeamId targetTeam)
        {
            bool isFriendlyFire = attackerTeam != TeamId.Neutral && attackerTeam == targetTeam;
            return isFriendlyFire ? AllyDamageClass : EnemyDamageClass;
        }

        public static float PopupScale(float normalizedProgress)
        {
            float growth = Mathf.Clamp01(normalizedProgress / PopupGrowthFraction);
            return Mathf.Lerp(0.84f, 1.1f, Mathf.SmoothStep(0f, 1f, growth));
        }

        public static float PopupOpacity(float normalizedProgress)
        {
            float fade = Mathf.InverseLerp(PopupGrowthFraction, 1f, Mathf.Clamp01(normalizedProgress));
            return 1f - Mathf.SmoothStep(0f, 1f, fade);
        }

        public static string FormatIndicator(string tankName, float distanceMetres)
        {
            return $"{tankName}  {Mathf.RoundToInt(Mathf.Max(0f, distanceMetres))} м";
        }

        public static string ArrowGlyph(float angleDegrees)
        {
            int octant = Mathf.RoundToInt(Mathf.Repeat(angleDegrees, 360f) / 45f) % 8;
            return new[] { "▶", "↗", "▲", "↖", "◀", "↙", "▼", "↘" }[octant];
        }

        private sealed class IndicatorView
        {
            public IndicatorView(VisualElement root, Label arrow, Label text)
            {
                Root = root;
                Arrow = arrow;
                Text = text;
            }

            public VisualElement Root { get; }
            public Label Arrow { get; }
            public Label Text { get; }
        }

        private sealed class HealthBarView
        {
            public HealthBarView(VisualElement root, VisualElement fill, Label label)
            {
                Root = root;
                Fill = fill;
                Label = label;
            }

            public VisualElement Root { get; }
            public VisualElement Fill { get; }
            public Label Label { get; }
        }

        private readonly struct DamagePopup
        {
            public DamagePopup(Vector2 worldPosition, float startedAt, Label label)
            {
                WorldPosition = worldPosition;
                StartedAt = startedAt;
                Label = label;
            }

            public Vector2 WorldPosition { get; }
            public float StartedAt { get; }
            public Label Label { get; }
        }
    }
}
