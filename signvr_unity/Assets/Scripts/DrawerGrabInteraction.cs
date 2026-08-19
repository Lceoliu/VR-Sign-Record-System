using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Constrains a Meta Interaction SDK grabbable so it behaves like a drawer.
/// The drawer stays locked on its local Y/Z axes and only slides on local X.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Grabbable))]
[RequireComponent(typeof(OneGrabTranslateTransformer))]
public sealed class DrawerGrabInteraction : MonoBehaviour
{
    [SerializeField, Min(0.01f)]
    private float travelDistance = 0.4f;

    [SerializeField]
    private bool opensTowardNegativeX = true;

    private Vector3 closedLocalPosition;
    private Rigidbody drawerRigidbody;
    private Grabbable grabbable;
    private OneGrabTranslateTransformer translateTransformer;

    /// <summary>
    /// Current opening amount where 0 is closed and 1 is fully open.
    /// </summary>
    public float NormalizedOpenAmount
    {
        get
        {
            float xOffset = transform.localPosition.x - closedLocalPosition.x;
            float openingDistance = opensTowardNegativeX ? -xOffset : xOffset;
            return Mathf.Clamp01(openingDistance / SafeTravelDistance);
        }
    }

    private float SafeTravelDistance => Mathf.Max(0.01f, travelDistance);

    private void Awake()
    {
        closedLocalPosition = transform.localPosition;
        ApplyConfiguration();
    }

    /// <summary>
    /// Applies the physics and transformer settings. This is public so the
    /// editor setup command can serialize a complete, inspectable scene.
    /// </summary>
    public void ApplyConfiguration()
    {
        CacheComponents();

        drawerRigidbody.useGravity = false;
        drawerRigidbody.isKinematic = true;
        drawerRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        drawerRigidbody.collisionDetectionMode =
            CollisionDetectionMode.ContinuousSpeculative;

        float minimumX = opensTowardNegativeX ? -SafeTravelDistance : 0f;
        float maximumX = opensTowardNegativeX ? 0f : SafeTravelDistance;

        OneGrabTranslateTransformer.OneGrabTranslateConstraints constraints =
            new OneGrabTranslateTransformer.OneGrabTranslateConstraints
            {
                ConstraintsAreRelative = true,
                MinX = Constrained(minimumX),
                MaxX = Constrained(maximumX),
                MinY = Constrained(0f),
                MaxY = Constrained(0f),
                MinZ = Constrained(0f),
                MaxZ = Constrained(0f)
            };

        translateTransformer.Constraints = constraints;

        grabbable.MaxGrabPoints = 1;
        grabbable.InjectOptionalTargetTransform(transform);
        grabbable.InjectOptionalRigidbody(drawerRigidbody);
        grabbable.InjectOptionalOneGrabTransformer(translateTransformer);
        grabbable.InjectOptionalTwoGrabTransformer(null);
        grabbable.InjectOptionalThrowWhenUnselected(false);
    }

    private void CacheComponents()
    {
        if (drawerRigidbody == null)
        {
            drawerRigidbody = GetComponent<Rigidbody>();
        }

        if (grabbable == null)
        {
            grabbable = GetComponent<Grabbable>();
        }

        if (translateTransformer == null)
        {
            translateTransformer =
                GetComponent<OneGrabTranslateTransformer>();
        }
    }

    private static FloatConstraint Constrained(float value)
    {
        return new FloatConstraint
        {
            Constrain = true,
            Value = value
        };
    }
}
