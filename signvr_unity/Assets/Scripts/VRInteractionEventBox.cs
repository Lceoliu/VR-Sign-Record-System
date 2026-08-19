using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A reusable trigger/event box for future gameplay and recording logic.
/// Assign a layer mask or tag to keep unrelated room colliders out.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class VRInteractionEventBox : MonoBehaviour
{
    [SerializeField] private LayerMask acceptedLayers = ~0;
    [SerializeField] private string acceptedTag;
    [SerializeField] private bool oneShot;
    [SerializeField] private UnityEvent onEntered;
    [SerializeField] private UnityEvent onExited;

    public event Action<Collider> Entered;
    public event Action<Collider> Exited;

    private BoxCollider boxCollider;
    private bool fired;

    private void Awake()
    {
        boxCollider = GetComponent<BoxCollider>();
        boxCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!Accepts(other) || (oneShot && fired))
        {
            return;
        }
        fired = true;
        Entered?.Invoke(other);
        onEntered?.Invoke();
        VRInteractionEvents.Instance?.PublishTrigger(gameObject, other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!Accepts(other))
        {
            return;
        }
        Exited?.Invoke(other);
        onExited?.Invoke();
    }

    public void ResetOneShot()
    {
        fired = false;
    }

    private bool Accepts(Collider other)
    {
        if (((1 << other.gameObject.layer) & acceptedLayers.value) == 0)
        {
            return false;
        }
        return string.IsNullOrEmpty(acceptedTag) || other.CompareTag(acceptedTag);
    }
}
