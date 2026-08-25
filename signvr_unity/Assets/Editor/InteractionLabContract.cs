using System;
using System.Collections.Generic;

namespace SignVR.Editor.Interaction
{
    /// <summary>
    /// Name- and path-based seam shared by the scene generator, validator, and
    /// build entry. It deliberately has no dependency on the parallel
    /// Interaction Core implementation.
    /// </summary>
    public static class InteractionLabContract
    {
        public const string RecorderScenePath = "Assets/Scenes/VRroom.unity";
        public const string ScenePath = "Assets/Scenes/InteractionLab.unity";
        public const string SceneRootName = "InteractionSceneRoot";
        public const string RuntimeSystemsAnchorName =
            "RuntimeSystemsAnchor";
        public const string AnchorsRootName = "Anchors";
        public const int LegacyStreamerPort = 5005;

        public static readonly IReadOnlyList<string> RequiredAnchorPaths =
            Array.AsReadOnly(
                new[]
                {
                    SceneRootName + "/" + RuntimeSystemsAnchorName,
                    SceneRootName + "/" + AnchorsRootName,
                    SceneRootName + "/" + AnchorsRootName +
                        "/ParticipantSpawnAnchor",
                    SceneRootName + "/" + AnchorsRootName +
                        "/InstructionSignerAnchor",
                    SceneRootName + "/" + AnchorsRootName +
                        "/InstructionBubbleAnchor",
                    SceneRootName + "/" + AnchorsRootName +
                        "/PhaseContentAnchor",
                    SceneRootName + "/" + AnchorsRootName +
                        "/InteractionUiAnchor",
                    SceneRootName + "/" + AnchorsRootName +
                        "/ExperimentCaptureAnchor"
                }
            );

        internal static readonly IReadOnlyCollection<string>
            RecordingOwnedObjectNames = new HashSet<string>(
                new[]
                {
                    "_Recording",
                    "RecordingViewpoints",
                    "RecordingSource",
                    "MotionRecorderCanvas",
                    "RecordingPromptBubbleCanvas",
                    "RecordingTakeUI",
                    "RecordingToolPanel",
                    "RecordingTouchscreen",
                    "RecordingTutorial",
                    "RecordingBoundaryWarning",
                    "SignVR Left IndexTip Ray Origin",
                    "SignVR Right IndexTip Ray Origin",
                    "SignVR Preview Camera"
                },
                StringComparer.Ordinal
            );

        internal static readonly IReadOnlyCollection<string>
            ForbiddenComponentTypeNames = new HashSet<string>(
                new[]
                {
                    "SignVR.Recording.RecordingRuntimeBootstrap",
                    "SignVR.Recording.RecordingCoordinator",
                    "SignVR.Recording.RecordingTargetPoseLock",
                    "SignVR.Recording.RecordingSentenceSequence",
                    "SignVR.Recording.RecordingCoordinatorUIBridge",
                    "SignVR.Recording.RecordingTeacherUI",
                    "SignVR.Recording.RecordingPromptBoard",
                    "SignVR.Recording.RecordingPromptBoardPokeDrag",
                    "SignVR.Recording.RecordingPromptBubble",
                    "SignVR.Recording.QuestPreviewStreamer",
                    "SignVR.Recording.QuestTakeUploader",
                    "SignVR.Recording.QuestDeviceGateway",
                    "MetaBodyMotionRecorder",
                    "MetaBodyMotionStreamer",
                    "MetaMotionRecorderUI"
                },
                StringComparer.Ordinal
            );
    }
}
