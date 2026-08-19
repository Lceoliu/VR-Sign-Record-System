using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Bridges Meta Interaction SDK selection events into the project-wide event
/// hub. The future recorder can observe grabs without depending on a specific
/// hand or controller interactor implementation.
/// </summary>
[DisallowMultipleComponent]
public sealed class VRGrabEventForwarder : MonoBehaviour
{
    [SerializeField] private VRPhysicalObject target;

    private readonly List<IInteractableView> interactables =
        new List<IInteractableView>();
    private readonly HashSet<IInteractorView> selectingInteractors =
        new HashSet<IInteractorView>();

    private void Awake()
    {
        if (target == null)
        {
            target = GetComponent<VRPhysicalObject>() ??
                     GetComponentInParent<VRPhysicalObject>();
        }
    }

    private void OnEnable()
    {
        RefreshInteractables();
    }

    private void OnDisable()
    {
        Unsubscribe();
        selectingInteractors.Clear();
    }

    public void Configure(VRPhysicalObject physicalObject)
    {
        target = physicalObject;
    }

    public void RefreshInteractables()
    {
        Unsubscribe();
        interactables.Clear();

        foreach (MonoBehaviour component in
                 GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component is IInteractableView interactable &&
                !interactables.Contains(interactable))
            {
                interactables.Add(interactable);
            }
        }

        foreach (IInteractableView interactable in interactables)
        {
            interactable.WhenSelectingInteractorViewAdded += HandleGrabStarted;
            interactable.WhenSelectingInteractorViewRemoved += HandleGrabEnded;
        }
    }

    private void Unsubscribe()
    {
        foreach (IInteractableView interactable in interactables)
        {
            if (interactable == null)
            {
                continue;
            }
            interactable.WhenSelectingInteractorViewAdded -= HandleGrabStarted;
            interactable.WhenSelectingInteractorViewRemoved -= HandleGrabEnded;
        }
    }

    private void HandleGrabStarted(IInteractorView interactor)
    {
        if (!selectingInteractors.Add(interactor) ||
            selectingInteractors.Count != 1)
        {
            return;
        }

        VRInteractionEvents.Instance?.PublishGrabStarted(
            target != null ? target.gameObject : gameObject,
            GetInteractorObject(interactor)
        );
    }

    private void HandleGrabEnded(IInteractorView interactor)
    {
        if (!selectingInteractors.Remove(interactor) ||
            selectingInteractors.Count != 0)
        {
            return;
        }

        VRInteractionEvents.Instance?.PublishGrabEnded(
            target != null ? target.gameObject : gameObject,
            GetInteractorObject(interactor)
        );
    }

    private static GameObject GetInteractorObject(IInteractorView interactor)
    {
        return (interactor as Component)?.gameObject;
    }
}
