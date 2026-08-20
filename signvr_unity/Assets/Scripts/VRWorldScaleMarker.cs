using UnityEngine;

/// <summary>
/// Records the authoring-unit conversion applied to a VRroom scene root.
/// </summary>
[DisallowMultipleComponent]
public sealed class VRWorldScaleMarker : MonoBehaviour
{
    [SerializeField, HideInInspector] private float appliedScale = 1f;

    public float AppliedScale => appliedScale;

    public void SetAppliedScale(float value)
    {
        appliedScale = value;
    }
}
