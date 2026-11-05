#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace TopDownRPG.EditorTools
{
    /// <summary>
    /// Adds visible, non-colliding +Z-facing sunglasses to a 2-unit-high player capsule.
    /// Works for selected prefab assets or player objects selected in the Hierarchy.
    /// </summary>
    public static class CapsuleSunglassesBuilder
    {
        private const string GlassesRootName = "DebugSunglasses";
        private const string MaterialsFolder = "Assets/CapsulePrototypeMaterials";

        [MenuItem("Tools/Combat Prototype/Add Sunglasses To Selected Player")]
        private static void AddSunglasses()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Select Player", "Select the Player prefab or its root in the Hierarchy first.", "OK");
                return;
            }

            GameObject player = selected;
            string prefabPath = null;
            bool asset = PrefabUtility.IsPartOfPrefabAsset(selected);
            if (asset)
            {
                prefabPath = AssetDatabase.GetAssetPath(selected);
                if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab"))
                {
                    Debug.LogError("Select the Player prefab asset, not a child asset.");
                    return;
                }
                player = PrefabUtility.LoadPrefabContents(prefabPath);
            }

            try
            {
                if (player.transform.Find(GlassesRootName) != null)
                {
                    Debug.LogWarning("DebugSunglasses already exist on " + player.name + ". No duplicate added.");
                    return;
                }

                Material lenses = GetMaterial("DebugSunglasses_Lens", new Color(0.025f, 0.035f, 0.06f));
                Material frames = GetMaterial("DebugSunglasses_Frame", new Color(0.10f, 0.10f, 0.12f));

                GameObject root = new GameObject(GlassesRootName);
                root.transform.SetParent(player.transform, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                root.layer = LayerMask.NameToLayer("Ignore Raycast");
                if (!asset) Undo.RegisterCreatedObjectUndo(root, "Add Capsule Sunglasses");

                // Local +Z is the FRONT of the player. These values assume the
                // original Player root feet at y=0 and capsule center at y=1.
                AddCube(root.transform, "Lens_Left", new Vector3(-0.175f, 1.66f, 0.475f),
                    new Vector3(0.30f, 0.15f, 0.035f), lenses);
                AddCube(root.transform, "Lens_Right", new Vector3(0.175f, 1.66f, 0.475f),
                    new Vector3(0.30f, 0.15f, 0.035f), lenses);
                AddCube(root.transform, "Bridge", new Vector3(0f, 1.685f, 0.49f),
                    new Vector3(0.065f, 0.035f, 0.05f), frames);
                AddCube(root.transform, "Brow_Left", new Vector3(-0.175f, 1.745f, 0.48f),
                    new Vector3(0.32f, 0.025f, 0.045f), frames);
                AddCube(root.transform, "Brow_Right", new Vector3(0.175f, 1.745f, 0.48f),
                    new Vector3(0.32f, 0.025f, 0.045f), frames);
                AddCube(root.transform, "Temple_Left", new Vector3(-0.33f, 1.68f, 0.29f),
                    new Vector3(0.035f, 0.035f, 0.38f), frames);
                AddCube(root.transform, "Temple_Right", new Vector3(0.33f, 1.68f, 0.29f),
                    new Vector3(0.035f, 0.035f, 0.38f), frames);

                if (asset)
                {
                    PrefabUtility.SaveAsPrefabAsset(player, prefabPath);
                    AssetDatabase.SaveAssets();
                    Debug.Log("Added +Z-facing sunglasses and saved Player prefab: " + prefabPath);
                }
                else
                {
                    Debug.Log("Added +Z-facing sunglasses to " + player.name + ". If it is a scene prefab instance, apply the override to save the prefab.");
                }
            }
            finally
            {
                if (asset && player != null) PrefabUtility.UnloadPrefabContents(player);
            }
        }

        [MenuItem("Tools/Combat Prototype/Add Sunglasses To Selected Player", true)]
        private static bool CanAdd() => Selection.activeGameObject != null;

        private static void AddCube(Transform parent, string name, Vector3 localPosition,
            Vector3 localScale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = localScale;
            cube.layer = LayerMask.NameToLayer("Ignore Raycast");
            Collider collider = cube.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Material GetMaterial(string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
                AssetDatabase.CreateFolder("Assets", "CapsulePrototypeMaterials");

            string path = MaterialsFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogError("No supported shader found. Check your render pipeline.");
                return null;
            }
            material = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
#endif
