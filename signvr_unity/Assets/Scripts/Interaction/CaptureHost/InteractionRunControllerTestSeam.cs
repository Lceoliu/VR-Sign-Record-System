#if UNITY_EDITOR
using System;
using SignVR.Interaction.Core;

namespace SignVR.Interaction.CaptureHost
{
    public sealed partial class InteractionRunController
    {
        internal void InstallStandalonePreStartForTests(
            InteractionRunStateMachine machine,
            InstructionContentCatalog catalog,
            string persistentDataPath,
            bool debugBuild)
        {
            if (machine == null || machine.Plan != null ||
                machine.State != RunState.PreStart)
            {
                throw new ArgumentException(
                    "Standalone Controller tests require a PreStart W1 machine.",
                    nameof(machine)
                );
            }
            stateMachine = machine;
            contentCatalog = catalog ?? throw new ArgumentNullException(
                nameof(catalog)
            );
            storageRoot = System.IO.Path.GetFullPath(persistentDataPath);
            appSessionId = "app_standalone_controller_test";
            startupRecovery = null;
            debugBuildOverrideForTests = debugBuild;
            lifecycleShutdown.Reset();
            terminalSealArbiter.Reset();
            pendingRunDiscoveryFailure = string.Empty;
            lastError = string.Empty;
        }

        internal void BeginStandaloneStartupRecoveryForTests()
        {
            BeginStandaloneStartupRecovery(storageRoot);
        }

        internal void RedirectStandaloneStorageRootForTests(
            string persistentDataPath,
            bool debugBuild)
        {
            if (State != RunState.PreStart || StartupRecoveryStatus !=
                InteractionStandaloneLocalRunRecoveryStatus.Succeeded)
            {
                throw new InvalidOperationException(
                    "Storage redirection requires a recovered PreStart Controller."
                );
            }
            storageRoot = System.IO.Path.GetFullPath(persistentDataPath);
            debugBuildOverrideForTests = debugBuild;
            RefreshPendingRuns();
        }

        internal bool CompleteStandaloneStartupRecoveryForTests(
            TimeSpan timeout)
        {
            if (startupRecovery == null || !startupRecovery.Wait(timeout))
            {
                return false;
            }
            ApplyStandaloneStartupRecoveryResult();
            return true;
        }

        internal bool WaitForCaptureInitializationForTests(TimeSpan timeout)
        {
            return captureInitialization != null &&
                captureInitialization.Wait(timeout);
        }

        internal void ReconcileConsumedRunInitializationForTests()
        {
            ReconcileConsumedRunInitialization();
        }

        internal void ArmUnityLifecycleForTests()
        {
            if (UnityEngine.Application.isPlaying)
            {
                throw new InvalidOperationException(
                    "Editor lifecycle simulation must not run in Play Mode."
                );
            }
            editorLifecycleTestsArmed = true;
        }

        internal void InstallDeterministicScenarioForTests(
            InteractionRunStateMachine machine,
            InteractionSummaryTracker summary,
            InteractionCaptureWriter writer,
            InteractionBackgroundOperation<InteractionCaptureWriter>
                initialization,
            string persistentDataPath = null)
        {
            if (machine == null || machine.Plan == null)
            {
                throw new ArgumentNullException(nameof(machine));
            }
            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }
            if (writer == null && initialization == null)
            {
                throw new ArgumentException(
                    "A deterministic Controller scenario requires capture ownership."
                );
            }
            if (writer != null && !string.Equals(
                    writer.RunId,
                    machine.Plan.RunId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Controller test writer does not match the W1 Run."
                );
            }
            stateMachine = machine;
            if (!string.IsNullOrWhiteSpace(persistentDataPath))
            {
                storageRoot = System.IO.Path.GetFullPath(persistentDataPath);
            }
            summaryTracker = summary;
            captureWriter = writer;
            captureInitialization = initialization;
            captureInitializationReconciled = writer != null;
            captureTerminalization = null;
            captureTerminalKind = null;
            captureTerminalizationReconciled = false;
            lifecycleTerminalizationJob = null;
            phaseCheckpointRoutine = null;
            terminalizationRoutine = null;
            activePhaseCheckpoint = null;
            pendingRunDiscoveryFailure = string.Empty;
            lastError = string.Empty;
            lifecycleShutdown.Reset();
            terminalSealArbiter.Reset();
        }

        internal void BeginCompletedSealForTests(double monotonicTimeSeconds)
        {
            if (!TryBeginCompletedSeal(monotonicTimeSeconds))
            {
                throw new InvalidOperationException(
                    "Deterministic Controller could not queue Completed seal."
                );
            }
        }

        internal InteractionBackgroundOperation<InteractionCaptureSealResult>
            TerminalizationForTests => captureTerminalization;

        internal InteractionLifecycleTerminalizationJob
            LifecycleTerminalizationForTests => lifecycleTerminalizationJob;

        internal InteractionDetachedInitializationOwner
            DetachedInitializationOwnerForTests =>
                detachedInitializationOwnerForTests;

        internal InteractionCaptureWriter CaptureWriterForTests => captureWriter;

        internal void ReconcileTerminalSealForTests()
        {
            if (!captureTerminalKind.HasValue)
            {
                throw new InvalidOperationException(
                    "No deterministic terminal seal is owned."
                );
            }
            CompleteTerminalSeal(captureTerminalKind.Value);
        }

        internal void ReconcileLifecycleTerminalizationForTests()
        {
            ReconcileLifecycleTerminalization();
        }

        internal bool WaitForLifecycleTerminalizationForTests(TimeSpan timeout)
        {
            InteractionLifecycleTerminalizationJob job =
                lifecycleTerminalizationJob;
            if (job != null && !job.Wait(timeout))
            {
                return false;
            }
            ReconcileLifecycleTerminalization();
            return lifecycleTerminalizationJob == null &&
                State == RunState.Aborted;
        }

        internal void InstallLifecycleWorkQueueForTests(
            IInteractionBackgroundWorkQueue workQueue)
        {
            lifecycleWorkQueue = workQueue ??
                throw new ArgumentNullException(nameof(workQueue));
        }

        internal InteractionTerminalSealArbitrationState
            TerminalSealStateForTests => terminalSealArbiter.State;

        internal bool LifecycleShutdownInitiatedForTests =>
            lifecycleShutdown.IsShutdownInitiated;

        internal void ProcessApplicationPauseForTests()
        {
            ProcessLifecycleSignal(ControllerLifecycleSignal.ApplicationPaused);
        }

        internal void ProcessHeadsetUnmountForTests()
        {
            ProcessLifecycleSignal(ControllerLifecycleSignal.HeadsetUnmounted);
        }

        internal void ProcessHeadsetMountForTests()
        {
            ProcessLifecycleSignal(ControllerLifecycleSignal.HeadsetMounted);
        }
    }
}
#endif
