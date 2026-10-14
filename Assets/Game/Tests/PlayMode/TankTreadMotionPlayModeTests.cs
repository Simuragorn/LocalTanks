using System.Collections;
using LocalTanks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The tread has to reach the shader from the generated prefab, and the overlay has to carry the
    /// track mask rather than the whole sprite sheet. Both mistakes are invisible to a compiler and to
    /// a still image: the overlay would draw a pixel-identical copy of the hull, so the tracks would
    /// simply never move. The look itself is judged from frames of
    /// Local Tanks > Diagnostics > Capture Track Effect Preview, which is why this test checks the
    /// wiring rather than the pixels.
    /// </summary>
    public sealed class TankTreadMotionPlayModeTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Tanks/MS1_Player.prefab";

        [UnityTest]
        public IEnumerator ThePrefabCarriesItsMaskAndItsTravelToTheOverlayMaterial()
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Prefab was not found at '{PrefabPath}'.");

            GameObject tank = Object.Instantiate(prefab);
            tank.transform.position = Vector3.zero;
            tank.transform.rotation = Quaternion.identity;

            // The prefab drives itself and its motor would cancel the movement applied here, so the
            // machine is left as a plain body that this test moves the same way TankMotor does.
            foreach (MonoBehaviour behaviour in tank.GetComponents<MonoBehaviour>())
            {
                if (behaviour is TankMotor || behaviour is PlayerTankInput)
                {
                    behaviour.enabled = false;
                }
            }

            Rigidbody2D body = tank.GetComponent<Rigidbody2D>();
            TankTrackAnimator animator = tank.GetComponent<TankTrackAnimator>();
            SpriteRenderer overlay = tank.transform.Find("HullVisual/TrackOverlay")?.GetComponent<SpriteRenderer>();
            SpriteRenderer hull = tank.transform.Find("HullVisual")?.GetComponent<SpriteRenderer>();
            Assert.That(body, Is.Not.Null);
            Assert.That(animator, Is.Not.Null, "The prefab must carry the tread animator.");
            Assert.That(overlay, Is.Not.Null, "The prefab must carry the tread overlay.");
            Assert.That(hull, Is.Not.Null, "The prefab must carry the hull renderer the overlay reads.");

            try
            {
                Assert.That(overlay.sprite, Is.Not.Null, "The overlay must have a mask sprite.");
                Assert.That(overlay.sprite.texture, Is.Not.Null);
                Assert.That(overlay.sharedMaterial, Is.Not.Null);
                Assert.That(overlay.sharedMaterial.shader.name, Is.EqualTo("LocalTanks/TankTrackOverlay"),
                    "The overlay must use the tread shader.");

                // A sprite sheet bound here instead of the mask would silently disable the effect, and
                // the pixel check of the mask itself lives in TankTrackLayoutTests, which can read the
                // generated file without a graphics device.
                Assert.That(ReferenceEquals(overlay.sprite.texture, hull.sprite.texture), Is.False,
                    "The overlay must sample the track mask, not the sprite sheet of the hull.");
                Assert.That(overlay.sprite.texture.name, Does.Contain("tracks_mask"),
                    $"The overlay samples {overlay.sprite.texture.name}, which is not a track mask.");

                Rect rect = overlay.sprite.textureRect;
                Assert.That(rect.width, Is.EqualTo(overlay.sprite.texture.width).Within(0.5f),
                    "The mask must span its whole texture, otherwise the band fades along a straight cut.");
                Assert.That(rect.height, Is.EqualTo(overlay.sprite.texture.height).Within(0.5f));

                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                overlay.GetPropertyBlock(properties);
                Vector4 travelBefore = properties.GetVector("_TrackTravel");
                Vector4 parameters = properties.GetVector("_TrackParams");
                Vector4 strength = properties.GetVector("_TrackStrength");
                Vector4 motionBefore = properties.GetVector("_TrackMotion");
                Vector4 geometry = properties.GetVector("_TrackGeometry");
                Vector4 hullRect = properties.GetVector("_HullRect");

                Assert.That(parameters.x, Is.GreaterThan(1f),
                    $"The property block must carry the link count of this machine, but it holds {parameters.x}.");
                Assert.That(parameters.y, Is.GreaterThan(0f), "The property block must carry the coarse link count.");
                Assert.That(strength.x, Is.GreaterThan(0f), "The property block must carry the tread strength.");
                Assert.That(motionBefore.x, Is.EqualTo(0f).Within(0.001f),
                    "A stationary machine must not carry a moving highlight on its left track.");
                Assert.That(motionBefore.y, Is.EqualTo(0f).Within(0.001f),
                    "A stationary machine must not carry a moving highlight on its right track.");
                Assert.That(geometry.z, Is.GreaterThan(0f), "The shader needs the measured half-width of a track band.");
                Assert.That(hullRect.z, Is.GreaterThan(0f), "The property block must carry the hull rectangle.");
                Assert.That(properties.GetTexture("_HullTex"), Is.Not.Null,
                    "The property block must carry the hull texture the shader re-shades.");

                for (int step = 0; step < 8; step++)
                {
                    body.MovePosition(body.position + Vector2.up * 0.05f);
                    yield return new WaitForFixedUpdate();
                }

                overlay.GetPropertyBlock(properties);
                Vector4 travelAfter = properties.GetVector("_TrackTravel");
                Vector4 motionAfter = properties.GetVector("_TrackMotion");
                Assert.That(travelAfter.x, Is.Not.EqualTo(travelBefore.x).Within(0.0001f),
                    "Driving must push the left track's travel into the overlay material.");
                Assert.That(travelAfter.y, Is.Not.EqualTo(travelBefore.y).Within(0.0001f),
                    "Driving must push the right track's travel into the overlay material.");
                Assert.That(travelAfter.x, Is.EqualTo(travelAfter.y).Within(0.01f),
                    "Driving straight must move both tracks by the same amount.");
                Assert.That(motionAfter.x, Is.GreaterThan(0f), "An achieved movement must reveal the left track pattern.");
                Assert.That(motionAfter.y, Is.GreaterThan(0f), "An achieved movement must reveal the right track pattern.");
            }
            finally
            {
                Object.Destroy(tank);
            }

            yield return null;
        }
    }
}
