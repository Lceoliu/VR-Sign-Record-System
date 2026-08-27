using UnityEngine;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Unity-serializable entry point for the test scene's control panel. The
    /// implementation lives beside its paired sequence controller while this
    /// matching filename keeps the MonoScript reference stable across reloads.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionSignSequenceTestControls :
        InteractionSignSequenceTestControlsBase
    {
    }
}
