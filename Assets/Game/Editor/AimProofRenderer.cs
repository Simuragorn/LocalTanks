using System.IO;
using LocalTanks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Renders the two independent aiming signals from their production components: hull relation
    /// outlines and penetration-chance colours. The deliberately crossed examples (ally with a poor
    /// chance, enemy with a good chance) make it visible that team and armor are not conflated.
    /// </summary>
    public static class AimProofRenderer
    {
        private const string OutputPath = "Docs/Media/AimProof.png";
        private const int Width = 1400;
        private const int Height = 720;

        [MenuItem("Local Tanks/Diagnostics/Capture Aim Proof")]
        public static void Render()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera camera = new GameObject("AimProofCamera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 3.25f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.145f, 0.12f, 1f);
            camera.transform.position = new Vector3(0f, 0f, -10f);

            Sprite dash = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            try
            {
                GameObject ally = InstantiateTank(
                    scene,
                    "Assets/Game/Prefabs/Tanks/MS1_Player.prefab",
                    new Vector2(-1.8f, -0.35f));
                GameObject enemy = InstantiateTank(
                    scene,
                    "Assets/Game/Prefabs/Tanks/Leichttraktor_Player.prefab",
                    new Vector2(1.8f, -0.35f));

                ally.transform.localScale = Vector3.one * 1.45f;
                enemy.transform.localScale = Vector3.one * 1.45f;

                AddOutline(ally, TeamPalette.Ally);
                AddOutline(enemy, TeamPalette.Enemy);
                AddReticle(ally, dash, PlayerAimReticle.PenetrationChanceColor(0.15f));
                AddReticle(enemy, dash, PlayerAimReticle.PenetrationChanceColor(0.9f));

                RenderTexture target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                Texture2D image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    camera.targetTexture = target;
                    RenderTexture.active = target;
                    camera.Render();
                    image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                    image.Apply();
                    string absolute = Path.GetFullPath(OutputPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(absolute));
                    File.WriteAllBytes(absolute, image.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = previous;
                    Object.DestroyImmediate(target);
                    Object.DestroyImmediate(image);
                }
            }
            finally
            {
                Object.DestroyImmediate(dash);
                EditorSceneManager.CloseScene(scene, true);
            }

            Debug.Log($"Aim proof written to {Path.GetFullPath(OutputPath)}");
        }

        private static GameObject InstantiateTank(Scene scene, string prefabPath, Vector2 position)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new FileNotFoundException("Aim proof tank prefab is missing.", prefabPath);
            GameObject tank = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            tank.transform.SetPositionAndRotation(position, Quaternion.identity);
            return tank;
        }

        private static void AddOutline(GameObject tank, Color color)
        {
            TankDebugSelectionOutline outline = tank.GetComponent<TankDebugSelectionOutline>();
            if (outline == null) outline = tank.AddComponent<TankDebugSelectionOutline>();
            outline.SetHoverVisible(true, color);
        }

        private static void AddReticle(GameObject tank, Sprite dash, Color color)
        {
            PolygonCollider2D hull = tank.GetComponent<PolygonCollider2D>();
            float radius = hull != null ? Mathf.Max(hull.bounds.extents.x, hull.bounds.extents.y) + 0.38f : 1.5f;
            const int segments = 40;
            for (int index = 0; index < segments; index++)
            {
                float angle = index * 360f / segments;
                float radians = angle * Mathf.Deg2Rad;
                GameObject marker = new GameObject($"ReticleDash{index:00}");
                marker.transform.SetParent(tank.transform, false);
                marker.transform.localPosition = new Vector3(
                    Mathf.Cos(radians) * radius,
                    Mathf.Sin(radians) * radius,
                    -0.1f);
                marker.transform.localRotation = Quaternion.Euler(0f, 0f, angle + 90f);
                marker.transform.localScale = new Vector3(0.18f, 0.035f, 1f);
                SpriteRenderer renderer = marker.AddComponent<SpriteRenderer>();
                renderer.sprite = dash;
                renderer.color = color;
                renderer.sortingOrder = 220;
            }
        }

    }
}
