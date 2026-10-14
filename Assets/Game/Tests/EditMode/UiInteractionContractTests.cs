using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    public sealed class UiInteractionContractTests
    {
        private static readonly string[] InteractiveImguiFiles =
        {
            "Assets/Game/Runtime/AiDuelLabController.cs",
            "Assets/Game/Runtime/CombatAiDebugOverlay.cs",
            "Assets/Game/Runtime/DeveloperTimeScaleController.cs",
            "Assets/Game/Runtime/GroundAppearanceTuner.cs",
            "Assets/Game/Runtime/NavigationDebugOverlay.cs",
            "Assets/Game/Runtime/PlayerAiTestLabController.cs",
            "Assets/Game/Runtime/PlayerTankSelector.cs"
        };

        private static readonly string[] PointerConsumers =
        {
            "Assets/Game/Runtime/PlayerTankInput.cs",
            "Assets/Game/Runtime/CameraFollow2D.cs",
            "Assets/Game/Runtime/PlayerAimReticle.cs"
        };

        [Test]
        public void PointerRegions_UseTopLeftGuiCoordinatesAndCleanUpDestroyedOwners()
        {
            GameObject owner = new GameObject("PointerRegionOwner");
            try
            {
                ModalUiState.SetPointerRegion(owner, new Rect(100f, 50f, 200f, 100f));

                Assert.That(ModalUiState.BlocksPointer(new Vector2(150f, 650f), 720f), Is.True,
                    "Input System uses bottom-left coordinates, while IMGUI regions use top-left coordinates.");
                Assert.That(ModalUiState.BlocksPointer(new Vector2(150f, 500f), 720f), Is.False);

                Object.DestroyImmediate(owner);
                Assert.That(ModalUiState.PointerRegionCount, Is.Zero,
                    "A destroyed window must not leave an invisible dead zone over the battlefield.");
            }
            finally
            {
                if (owner != null)
                {
                    ModalUiState.ClearPointerRegion(owner);
                    Object.DestroyImmediate(owner);
                }
            }
        }

        [Test]
        public void ModalOwner_BlocksTheWholePointerSurface()
        {
            GameObject owner = new GameObject("ModalOwner");
            try
            {
                ModalUiState.Set(owner, true);
                Assert.That(ModalUiState.BlocksPointer(new Vector2(-1000f, -1000f), 720f), Is.True);
                Assert.That(ModalUiState.BlocksPointer(new Vector2(5000f, 5000f), 720f), Is.True);
            }
            finally
            {
                ModalUiState.Set(owner, false);
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void InteractiveImguiWindows_RegisterAndReleasePointerOwnership()
        {
            string[] missing = InteractiveImguiFiles.Where(path =>
            {
                string source = File.ReadAllText(path);
                return !source.Contains("ModalUiState.SetPointerRegion(this") ||
                       !source.Contains("ModalUiState.ClearPointerRegion(this");
            }).ToArray();

            Assert.That(missing, Is.Empty,
                "Interactive IMGUI windows must register their bounds and release them: " +
                string.Join(", ", missing));
        }

        [Test]
        public void ExistingImguiWindows_UseTheSharedThemeControls()
        {
            string[] forbidden = { "GUI.Button(", "GUI.Box(", "GUI.HorizontalSlider(", "GUI.Toggle(" };
            string[] offenders = Directory.GetFiles("Assets/Game/Runtime", "*.cs")
                .Where(path => !path.EndsWith("LocalTanksGuiTheme.cs"))
                .Where(path => forbidden.Any(token => File.ReadAllText(path).Contains(token)))
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "Existing IMGUI windows must use LocalTanksGuiTheme instead of raw default controls: " +
                string.Join(", ", offenders));
        }

        [Test]
        public void AimFireZoomAndReticle_RespectPointerOwnership()
        {
            string[] missing = PointerConsumers
                .Where(path => !File.ReadAllText(path).Contains("ModalUiState.BlocksPointer"))
                .ToArray();

            Assert.That(missing, Is.Empty,
                "Every mouse-driven gameplay consumer must respect UI pointer ownership: " +
                string.Join(", ", missing));
        }
    }
}
