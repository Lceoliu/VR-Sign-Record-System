#if UNITY_EDITOR
using System;
using System.IO;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.CaptureHost
{
    /// <summary>
    /// Runtime-side PlayMode scenario for the public sampler-to-Controller
    /// tracking seam. Kept out of player builds by UNITY_EDITOR.
    /// </summary>
    public static class InteractionTrackingContinuityPlayModeTestDriver
    {
        public static void
            TrackingLossDoesNotAbortAndRecoveryContinuesRunningRun()
        {
            string root = W6InteractionCaptureHostTestDriver
                .CreateTemporaryRoot();
            GameObject owner = null;
            InteractionRunController controller = null;
            InteractionCaptureWriter writer = null;
            try
            {
                owner = new GameObject("Tracking Continuity Controller");
                owner.SetActive(false);
                controller = owner.AddComponent<InteractionRunController>();

                InteractionRunStateMachine machine =
                    W6InteractionCaptureHostTestDriver
                        .CreateRunningStateMachine(121, "P954");
                writer = W6InteractionCaptureHostTestDriver.CreateWriter(
                    root,
                    machine.Plan
                );
                writer.BeginCapture();

                var summary = new InteractionSummaryTracker(
                    machine.Plan.RunId
                );
                summary.BeginRun(
                    0d,
                    W6InteractionCaptureHostTestDriver.FixedUtc
                );
                summary.BeginPhase(1, 0.1d);
                controller.InstallDeterministicScenarioForTests(
                    machine,
                    summary,
                    writer,
                    initialization: null,
                    persistentDataPath: root
                );

                var trackingLost = new InteractionHandSample(
                    tracked: false,
                    dataValid: false,
                    highConfidence: false,
                    poseSourceInferred: false,
                    joints: Array.Empty<InteractionJointSample>()
                );
                bool lossSampleAccepted = controller.TryCapturePose(
                    hmdValid: false,
                    hmdPosition: new InteractionVector3Sample(0d, 0d, 0d),
                    hmdRotation: new InteractionQuaternionSample(
                        0d,
                        0d,
                        0d,
                        1d
                    ),
                    leftHand: trackingLost,
                    rightHand: trackingLost
                );

                Require(
                    lossSampleAccepted &&
                    controller.State == RunState.Running &&
                    controller.IsCaptureActive &&
                    writer.NextPoseSequence == 2L &&
                    !writer.IsSealed &&
                    string.IsNullOrEmpty(controller.LastError) &&
                    controller.TerminalizationForTests == null &&
                    controller.LifecycleTerminalizationForTests == null &&
                    controller.TerminalSealStateForTests ==
                        InteractionTerminalSealArbitrationState.Open,
                    "Tracking loss aborted or terminalized the running Run."
                );

                var trackingRecovered = new InteractionHandSample(
                    tracked: true,
                    dataValid: true,
                    highConfidence: true,
                    poseSourceInferred: false,
                    joints: Array.Empty<InteractionJointSample>()
                );
                bool recoverySampleAccepted = controller.TryCapturePose(
                    hmdValid: true,
                    hmdPosition: new InteractionVector3Sample(0d, 1.6d, 0d),
                    hmdRotation: new InteractionQuaternionSample(
                        0d,
                        0d,
                        0d,
                        1d
                    ),
                    leftHand: trackingRecovered,
                    rightHand: trackingRecovered
                );
                controller.RecordInteractionAttempt(
                    correct: true,
                    actorId: "participant",
                    targetId: "tracking_recovery_probe",
                    detailPayloadJson: "{\"tracking_recovered\":true}"
                );

                InteractionBackgroundOperation<bool> checkpoint =
                    writer.BeginPhaseCheckpoint();
                Require(
                    checkpoint.Wait(TimeSpan.FromSeconds(5)) &&
                    checkpoint.Succeeded && checkpoint.GetResult(),
                    "Tracking continuity capture did not flush in time."
                );

                string poses = ReadAllTextShared(PartialPath(
                    writer.RunDirectory,
                    InteractionStoragePaths.PosesFileName
                ));
                string events = ReadAllTextShared(PartialPath(
                    writer.RunDirectory,
                    InteractionStoragePaths.EventsFileName
                ));
                Require(
                    recoverySampleAccepted &&
                    writer.NextPoseSequence == 3L &&
                    poses.IndexOf(
                        "\"pose_seq\":1",
                        StringComparison.Ordinal
                    ) >= 0 &&
                    poses.IndexOf(
                        "\"hmd\":{\"valid\":false",
                        StringComparison.Ordinal
                    ) >= 0 &&
                    poses.IndexOf(
                        "\"left_hand\":{\"tracked\":false",
                        StringComparison.Ordinal
                    ) >= 0 &&
                    poses.IndexOf(
                        "\"pose_seq\":2",
                        StringComparison.Ordinal
                    ) >= 0 &&
                    poses.IndexOf(
                        "\"hmd\":{\"valid\":true",
                        StringComparison.Ordinal
                    ) >= 0 &&
                    poses.IndexOf(
                        "\"left_hand\":{\"tracked\":true",
                        StringComparison.Ordinal
                    ) >= 0,
                    "Capture did not retain both tracking-loss and recovery samples."
                );
                Require(
                    controller.State == RunState.Running &&
                    controller.CurrentPhaseId == 1 &&
                    controller.IsCaptureActive &&
                    string.IsNullOrEmpty(controller.LastError) &&
                    controller.TerminalizationForTests == null &&
                    controller.LifecycleTerminalizationForTests == null &&
                    events.IndexOf(
                        "\"event_type\":\"" +
                            InteractionEventNames.InteractionAttempt + "\"",
                        StringComparison.Ordinal
                    ) >= 0 &&
                    events.IndexOf(
                        "\"event_type\":\"" +
                            InteractionEventNames.RunAborted + "\"",
                        StringComparison.Ordinal
                    ) < 0,
                    "The recovered Run could not continue normal interaction."
                );
            }
            finally
            {
                TerminalizeForCleanup(controller);
                writer?.Dispose();
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        private static string PartialPath(string directory, string fileName)
        {
            return Path.Combine(directory, "." + fileName + ".partial");
        }

        private static string ReadAllTextShared(string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static void TerminalizeForCleanup(
            InteractionRunController controller)
        {
            if (controller == null || controller.State != RunState.Running)
            {
                return;
            }
            if (!controller.TryAbortRun("test_cleanup", out _))
            {
                return;
            }
            InteractionBackgroundOperation<InteractionCaptureSealResult>
                terminalization = controller.TerminalizationForTests;
            if (terminalization != null &&
                terminalization.Wait(TimeSpan.FromSeconds(10)))
            {
                controller.ReconcileTerminalSealForTests();
            }
        }

        private static void Require(bool condition, string message)
        {
            W6InteractionCaptureHostTestDriver.Require(condition, message);
        }
    }
}
#endif
