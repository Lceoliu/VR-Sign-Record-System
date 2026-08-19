using System;
using UnityEngine;

/// <summary>
/// Common physics contract for scene props. It keeps collision/trigger hooks
/// uniform and exposes a stable identity for a future recorder or network
/// layer without forcing every prop to know about either system.
/// </summary>
[DisallowMultipleComponent]
public sealed class VRPhysicalObject : MonoBehaviour
{
    [SerializeField] private string objectId;
    [SerializeField] private bool publishEvents = true;

    public string ObjectId => string.IsNullOrWhiteSpace(objectId) ? name : objectId;
    public Rigidbody Body { get; private set; }
    public Collider[] Colliders { get; private set; }
    public event Action<Collision> CollisionEntered;
    public event Action<Collider> TriggerEntered;

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Colliders = GetComponentsInChildren<Collider>(true);
        if (string.IsNullOrWhiteSpace(objectId))
        {
            objectId = name;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        CollisionEntered?.Invoke(collision);
        if (publishEvents)
        {
            VRInteractionEvents.Instance?.PublishCollision(gameObject, collision.collider);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TriggerEntered?.Invoke(other);
        if (publishEvents)
        {
            VRInteractionEvents.Instance?.PublishTrigger(gameObject, other);
        }
    }

    public void SetObjectId(string value)
    {
        objectId = value;
    }
}
