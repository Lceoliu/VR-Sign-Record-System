using System.Collections.Generic;
using SignVR.Recording;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.EditorTools
{
    /// <summary>
    /// Makes the compact HMD status HUD draw on top of the room.
    ///
    /// TeacherUI intentionally remains a normal World Space Canvas because an
    /// OVROverlayCanvas made the whole UI appear grey on Quest. Only its compact
    /// status, countdown, progress and warning graphics use ZTest Always. The
    /// sentence prompt lives on the desk and keeps normal depth testing.
    /// </summary>
    public static class SignVRHmdUiDepthSetup
    {
        private const string MaterialFolder = "Assets/Materials/SignVR HMD UI";
        private const int ZTestAlways = 8;

        [MenuItem("SignVR/Setup/Make HMD UI Draw On Top")]
        public static void MakeHmdUiDrawOnTop()
        {
            RecordingTeacherUI teacherUI =
                Object.FindAnyObjectByType<RecordingTeacherUI>(FindObjectsInactive.Include);
            if (teacherUI == null)
            {
                Debug.LogError("[SignVRHmdUiDepthSetup] TeacherUI not found.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/Materials", "SignVR HMD UI");
            }

            var cache = new Dictionary<Material, Material>();
            int textCount = 0;
            int graphicCount = 0;

            foreach (TMP_Text text in teacherUI.GetComponentsInChildren<TMP_Text>(true))
            {
                Material source = text.fontSharedMaterial;
                if (source == null)
                {
                    continue;
                }

                text.fontSharedMaterial = GetOrCreateAlwaysVariant(source, cache);
                EditorUtility.SetDirty(text);
                textCount++;
            }

            foreach (Graphic graphic in teacherUI.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic is TMP_Text)
                {
                    continue;
                }

                // The built-in UI shader reads its depth test from the global
                // unity_GUIZTestMode the Canvas sets, so it cannot be overridden per
                // material. These graphics get a dedicated ZTest Always shader.
                graphic.material = GetOrCreateUiAlwaysMaterial();
                EditorUtility.SetDirty(graphic);
                graphicCount++;
            }

            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log(
                $"[SignVRHmdUiDepthSetup] ZTest Always applied to {textCount} texts and {graphicCount} graphics."
            );
        }

        private static Material GetOrCreateUiAlwaysMaterial()
        {
            const string path = MaterialFolder + "/SignVR UI AlwaysOnTop.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("SignVR/UI Always On Top");
            if (shader == null)
            {
                Debug.LogError(
                    "[SignVRHmdUiDepthSetup] Shader 'SignVR/UI Always On Top' not found."
                );
                return null;
            }

            material = new Material(shader) { name = "SignVR UI AlwaysOnTop" };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Material GetOrCreateAlwaysVariant(
            Material source,
            Dictionary<Material, Material> cache)
        {
            if (cache.TryGetValue(source, out Material cached))
            {
                return cached;
            }

            // Re-running the menu must not wrap an already-converted material into
            // another variant; the first pass is already the final one.
            if (source.name.Contains("AlwaysOnTop"))
            {
                ApplyAlwaysOnTop(source);
                EditorUtility.SetDirty(source);
                cache[source] = source;
                return source;
            }

            string path = $"{MaterialFolder}/{SanitizeName(source.name)} AlwaysOnTop.mat";
            var variant = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (variant == null)
            {
                variant = new Material(source);
                AssetDatabase.CreateAsset(variant, path);
            }
            else
            {
                variant.CopyPropertiesFromMaterial(source);
            }

            ApplyAlwaysOnTop(variant);
            EditorUtility.SetDirty(variant);
            cache[source] = variant;
            return variant;
        }

        private static void ApplyAlwaysOnTop(Material material)
        {
            // TextMeshPro exposes _ZTestMode; the built-in UI shader uses
            // unity_GUIZTestMode. Set whichever the shader actually declares.
            if (material.HasProperty("_ZTestMode"))
            {
                material.SetFloat("_ZTestMode", ZTestAlways);
            }

            if (material.HasProperty("unity_GUIZTestMode"))
            {
                material.SetFloat("unity_GUIZTestMode", ZTestAlways);
            }

            if (material.HasProperty("_ZTest"))
            {
                material.SetFloat("_ZTest", ZTestAlways);
            }

            // Draw after everything opaque so the HUD is never sorted behind it.
            material.renderQueue = 4000;
        }

        private static string SanitizeName(string value)
        {
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }
            return value.Replace("(Clone)", string.Empty).Trim();
        }
    }
}
