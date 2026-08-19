using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.Grab;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Reproducibly configures the hand/controller interactions for the furniture
/// added to SignTrackingRecorder.unity.
/// </summary>
internal static class ConfigureSceneInteractions
{
    private const string ScenePath =
        "Assets/Scenes/SignTrackingRecorder.unity";

    private static readonly HashSet<string> DrawerNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "c",
            "c (1)",
            "c (2)",
            "c (3)"
        };

    [MenuItem("Tools/SignVR/Configure Scene Interactions")]
    private static void ConfigureFromMenu()
    {
        ConfigureActiveScene(saveScene: true);
    }

    private static void ConfigureActiveScene(bool saveScene)
    {
        Scene scene = SceneManager.GetActiveScene();

        if (!scene.IsValid() || scene.path != ScenePath)
        {
            throw new InvalidOperationException(
                $"The active scene must be {ScenePath}."
            );
        }

        GameObject cabinet = FindCabinet(scene);

        if (cabinet == null)
        {
            throw new InvalidOperationException(
                "Could not find the cabinet with its four drawer children."
            );
        }

        Transform[] drawers = cabinet.transform
            .Cast<Transform>()
            .Where(child => DrawerNames.Contains(child.name))
            .OrderBy(child => child.name, StringComparer.Ordinal)
            .ToArray();

        if (drawers.Length != DrawerNames.Count)
        {
            throw new InvalidOperationException(
                $"Expected four drawers under {GetHierarchyPath(cabinet.transform)}, " +
                $"but found {drawers.Length}."
            );
        }

        foreach (Transform drawer in drawers)
        {
            EnsureGrabInteraction(drawer.gameObject);
            ConfigureDrawer(drawer.gameObject);
        }

        Transform cylinder = EnsureCylinderRigidbodyRoot(scene, cabinet);

        EnsureGrabInteraction(cylinder.gameObject);
        ConfigurePhysicalGrab(cylinder.gameObject);

        if (saveScene)
        {
            EditorSceneManager.MarkSceneDirty(scene);

            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new IOException($"Unity could not save {ScenePath}.");
            }
        }

        Debug.Log(
            "[SignVR] Configured four constrained drawers and one free-grab " +
            "Cylinder. Grab interactors were added to the available hand/" +
            "controller rigs."
        );
    }

    private static GameObject FindCabinet(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                int drawerCount = candidate
                    .Cast<Transform>()
                    .Count(child => DrawerNames.Contains(child.name));

                if (drawerCount == DrawerNames.Count)
                {
                    return candidate.gameObject;
                }
            }
        }

        return null;
    }

    private static Transform EnsureCylinderRigidbodyRoot(
        Scene scene,
        GameObject cabinet
    )
    {
        Transform migratedRoot = FindMigratedCylinderRoot(scene);

        if (migratedRoot != null)
        {
            return migratedRoot;
        }

        Transform visual = cabinet.transform.Find("Cylinder");

        if (visual == null)
        {
            throw new InvalidOperationException(
                $"Could not find Cylinder under {GetHierarchyPath(cabinet.transform)}."
            );
        }

        Vector3 worldPosition = visual.position;
        Quaternion worldRotation = visual.rotation;
        Vector3 worldScale = visual.lossyScale;

        GameObject[] oldInteractionObjects = visual
            .GetComponentsInChildren<HandGrabInteractable>(true)
            .Select(component => component.gameObject)
            .Distinct()
            .ToArray();

        foreach (GameObject interactionObject in oldInteractionObjects)
        {
            Undo.DestroyObjectImmediate(interactionObject);
        }

        Grabbable oldGrabbable = visual.GetComponent<Grabbable>();

        if (oldGrabbable != null)
        {
            Undo.DestroyObjectImmediate(oldGrabbable);
        }

        Rigidbody oldRigidbody = visual.GetComponent<Rigidbody>();

        if (oldRigidbody != null)
        {
            Undo.DestroyObjectImmediate(oldRigidbody);
        }

        GameObject rigidbodyRoot = new GameObject("Cylinder");
        Undo.RegisterCreatedObjectUndo(
            rigidbodyRoot,
            "Create unscaled Cylinder rigidbody root"
        );

        Transform rootTransform = rigidbodyRoot.transform;
        rootTransform.SetParent(cabinet.transform.parent, false);
        rootTransform.SetPositionAndRotation(worldPosition, worldRotation);
        rootTransform.localScale = Vector3.one;
        rootTransform.SetSiblingIndex(cabinet.transform.GetSiblingIndex() + 1);

        Undo.RecordObject(visual.gameObject, "Move Cylinder visual under rigidbody");
        visual.name = "Cylinder Visual";
        Undo.SetTransformParent(
            visual,
            rootTransform,
            "Move Cylinder visual under rigidbody"
        );
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        visual.localScale = worldScale;

        rigidbodyRoot.layer = visual.gameObject.layer;
        return rootTransform;
    }

    private static Transform FindMigratedCylinderRoot(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == "Cylinder" &&
                    candidate.Find("Cylinder Visual") != null)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void EnsureGrabInteraction(GameObject target)
    {
        bool hasHandGrab =
            target.GetComponentInChildren<HandGrabInteractable>(true) != null;

        bool hasControllerGrab =
            target.GetComponentInChildren<GrabInteractable>(true) != null;

        if (!hasHandGrab || !hasControllerGrab)
        {
            QuickActionsAPI.AddGrabInteraction(target);
        }
    }

    private static void ConfigureDrawer(GameObject drawer)
    {
        OneGrabTranslateTransformer transformer =
            GetOrAddComponent<OneGrabTranslateTransformer>(drawer);

        DrawerGrabInteraction interaction =
            GetOrAddComponent<DrawerGrabInteraction>(drawer);

        interaction.ApplyConfiguration();

        EditorUtility.SetDirty(transformer);
        EditorUtility.SetDirty(interaction);
        EditorUtility.SetDirty(drawer.GetComponent<Rigidbody>());
        EditorUtility.SetDirty(drawer.GetComponent<Grabbable>());
    }

    private static void ConfigurePhysicalGrab(GameObject target)
    {
        Rigidbody rigidbody = target.GetComponent<Rigidbody>();
        Grabbable grabbable = target.GetComponent<Grabbable>();

        rigidbody.mass = 0.25f;
        rigidbody.useGravity = true;
        rigidbody.isKinematic = false;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;

        grabbable.MaxGrabPoints = 1;
        grabbable.InjectOptionalTargetTransform(target.transform);
        grabbable.InjectOptionalRigidbody(rigidbody);
        grabbable.InjectOptionalKinematicWhileSelected(true);
        grabbable.InjectOptionalThrowWhenUnselected(true);
        grabbable.ForceKinematicDisabled = true;

        EditorUtility.SetDirty(rigidbody);
        EditorUtility.SetDirty(grabbable);
    }

    private static T GetOrAddComponent<T>(GameObject target)
        where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;

        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }

}
