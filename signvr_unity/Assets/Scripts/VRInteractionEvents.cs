using System;
using UnityEngine;

/// <summary>
/// Small, recording-friendly event hub. Gameplay systems can publish and
/// subscribe without taking a dependency on the future recorder transport.
/// </summary>
[DisallowMultipleComponent]
public sealed class VRInteractionEvents : MonoBehaviour
{
    public static VRInteractionEvents Instance { get; private set; }

    public event Action<GameObject, GameObject> GrabStarted;
    public event Action<GameObject, GameObject> GrabEnded;
    public event Action<GameObject, Collider> CollisionEntered;
    public event Action<GameObject, Collider> TriggerEntered;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public void PublishGrabStarted(GameObject interactable, GameObject interactor)
        => GrabStarted?.Invoke(interactable, interactor);

    public void PublishGrabEnded(GameObject interactable, GameObject interactor)
        => GrabEnded?.Invoke(interactable, interactor);

    public void PublishCollision(GameObject source, Collider other)
        => CollisionEntered?.Invoke(source, other);

    public void PublishTrigger(GameObject source, Collider other)
        => TriggerEntered?.Invoke(source, other);

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
