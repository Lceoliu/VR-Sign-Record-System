#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.CaptureHost
{
    /// <summary>
    /// Editor-only behavioral driver for the standalone run Controller.
    /// It exercises real local capture files and lifecycle ownership without
    /// adding a Study Player surface or using private-field reflection.
    /// </summary>
    public static class W6InteractionRunControllerTestDriver
    {
        public static void CompletedSealRejectsAbortThroughController()
        {
            string root = W6InteractionCaptureHostTestDriver
                .CreateTemporaryRoot();
            GameObject owner = null;
            InteractionCaptureWriter writer = null;
            InteractionBackgroundOperation<InteractionCaptureSealResult>
                terminalization = null;
            Exception primaryFailure = null;
            try
            {
                owner = new GameObject("W6 Controller Completed Test");
                owner.SetActive(false);
                InteractionRunController controller =
                    owner.AddComponent<InteractionRunController>();
                InteractionRunStateMachine machine =
                    W6InteractionCaptureHostTestDriver
                        .CreateCompletingStateMachine(111, "P944");
                writer = W6InteractionCaptureHostTestDriver.CreateWriter(
                    root,
                    machine.Plan
                );
                writer.BeginCapture();
                W6InteractionCaptureHostTestDriver.WriteOnePoseAndObject(
                    writer
                );
                InteractionSummaryTracker summary =
                    W6InteractionCaptureHostTestDriver.CreateCompletedSummary(
                        machine.Plan.RunId
                    );
                controller.InstallDeterministicScenarioForTests(
                    machine,
                    summary,
                    writer,
                    initialization: null
                );

                controller.BeginCompletedSealForTests(10d);
                terminalization = controller.TerminalizationForTests;
                bool accepted = controller.TryAbortRun(
                    "late_operator_abort",
                    out string rejection
                );
                W6InteractionCaptureHostTestDriver.Require(
                    !accepted && !string.IsNullOrWhiteSpace(rejection) &&
                    controller.State == RunState.Completing,
                    "Real Controller changed W1 before rejecting late Abort."
                );

                W6InteractionCaptureHostTestDriver.Await(
                    terminalization,
                    timeoutSeconds: 10
                );
                controller.ReconcileTerminalSealForTests();
                W6InteractionCaptureHostTestDriver.Require(
                    controller.State == RunState.Completed && writer.IsSealed,
                    "Original Completed seal did not retain Controller ownership."
                );
                AssertTerminalArtifacts(
                    writer.RunDirectory,
                    expectedStatus: "completed",
                    expectedEvent: InteractionEventNames.RunCompleted,
                    forbiddenEvent: InteractionEventNames.RunAborted
                );
            }
            catch (Exception exception)
            {
                primaryFailure = exception;
                throw;
            }
            finally
            {
                var failures = new List<Exception>();
                bool ownerFinished = terminalization == null ||
                    TryWaitForCleanup(
                        failures,
                        "Completed terminal seal",
                        () => terminalization.Wait(TimeSpan.FromSeconds(10)),
                        root
                    );
                TryDriverCleanup(failures, "destroy completed fixture", () =>
                {
                    if (owner != null)
                    {
                        UnityEngine.Object.DestroyImmediate(owner);
                    }
                });
                bool writerClosed = ownerFinished && TryDriverCleanup(
                    failures,
                    "dispose completed writer",
                    () => writer?.Dispose()
                );
                if (ownerFinished && writerClosed)
                {
                    TryDriverCleanup(failures, "delete completed fixture", () =>
                        W6InteractionCaptureHostTestDriver
                            .DeleteTemporaryRoot(root));
                }
                FinishDriverCleanup(primaryFailure, failures, root);
            }
        }

        public static void DisableOwnsLateInitializationThroughController()
        {
            string root = W6InteractionCaptureHostTestDriver
                .CreateTemporaryRoot();
            GameObject owner = null;
            InteractionCaptureWriter lateWriter = null;
            InteractionBackgroundOperation<InteractionCaptureWriter>
                initialization = null;
            InteractionLifecycleTerminalizationJob job = null;
            Exception primaryFailure = null;
            using (var writerCreated = new ManualResetEventSlim(false))
            using (var releaseInitialization = new ManualResetEventSlim(false))
            {
                try
                {
                    owner = new GameObject("W6 Controller Lifecycle Test");
                    owner.SetActive(false);
                    InteractionRunController controller =
                        owner.AddComponent<InteractionRunController>();
                    owner.SetActive(true);
                    owner.SetActive(false);
                    W6InteractionCaptureHostTestDriver.Require(
                        controller.State == RunState.PreStart,
                        "Warm-up deactivation did not finish Awake."
                    );
                    InteractionRunStateMachine machine =
                        W6InteractionCaptureHostTestDriver
                            .CreateRunningStateMachine(112, "P945");
                    var summary = new InteractionSummaryTracker(
                        machine.Plan.RunId
                    );
                    summary.BeginRun(
                        0d,
                        W6InteractionCaptureHostTestDriver.FixedUtc
                    );
                    summary.BeginPhase(1, 0.1d);
                    initialization = InteractionBackgroundOperation<
                        InteractionCaptureWriter>.Start(() =>
                        {
                            lateWriter = W6InteractionCaptureHostTestDriver
                                .CreateWriter(root, machine.Plan);
                            writerCreated.Set();
                            releaseInitialization.Wait();
                            return lateWriter;
                        });
                    W6InteractionCaptureHostTestDriver.Require(
                        writerCreated.Wait(TimeSpan.FromSeconds(5)),
                        "Controller late writer was not created."
                    );
                    controller.InstallDeterministicScenarioForTests(
                        machine,
                        summary,
                        writer: null,
                        initialization
                    );
                    ArmUnityLifecycleOnlyOutsidePlayMode(controller);
                    owner.SetActive(true);

                    controller.enabled = false;
                    job = controller.LifecycleTerminalizationForTests;
                    W6InteractionCaptureHostTestDriver.Require(
                        controller.State == RunState.Aborting && job != null,
                        "Controller disable did not begin local Abort ownership."
                    );
                    W6InteractionCaptureHostTestDriver.Require(
                        !job.Wait(TimeSpan.FromMilliseconds(5200)) &&
                        job.InitializationDelayObserved,
                        "Controller detached owner abandoned late initialization."
                    );
                    W6InteractionCaptureHostTestDriver.Require(
                        !controller.ResetToPreStart() &&
                        ReferenceEquals(
                            job,
                            controller.LifecycleTerminalizationForTests
                        ),
                        "Controller reset discarded delayed initialization ownership."
                    );
                    releaseInitialization.Set();
                    W6InteractionCaptureHostTestDriver.Require(
                        job.Wait(TimeSpan.FromSeconds(10)),
                        "Controller detached terminalization did not finish."
                    );
                    controller.enabled = true;
                    W6InteractionCaptureHostTestDriver.Require(
                        controller.State == RunState.Aborted &&
                        controller.LifecycleTerminalizationForTests == null &&
                        controller.CaptureWriterForTests == lateWriter &&
                        lateWriter.IsSealed,
                        "Main-thread reconciliation did not publish one late Abort."
                    );
                    AssertTerminalArtifacts(
                        lateWriter.RunDirectory,
                        expectedStatus: "aborted",
                        expectedEvent: InteractionEventNames.RunAborted,
                        forbiddenEvent: InteractionEventNames.RunCompleted
                    );
                    W6InteractionCaptureHostTestDriver.Require(
                        controller.ResetToPreStart(),
                        "Controller could not safely reset after late Abort seal."
                    );
                }
                catch (Exception exception)
                {
                    primaryFailure = exception;
                    throw;
                }
                finally
                {
                    var failures = new List<Exception>();
                    releaseInitialization.Set();
                    bool initializationFinished = initialization == null ||
                        TryWaitForCleanup(
                            failures,
                            "capture initialization",
                            () => initialization.Wait(TimeSpan.FromSeconds(10)),
                            root
                        );
                    bool jobFinished = job == null || TryWaitForCleanup(
                        failures,
                        "lifecycle terminalization",
                        () => job.Wait(TimeSpan.FromSeconds(10)),
                        root
                    );
                    TryDriverCleanup(failures, "destroy lifecycle fixture", () =>
                    {
                        if (owner != null)
                        {
                            UnityEngine.Object.DestroyImmediate(owner);
                        }
                    });
                    bool writerClosed = initializationFinished && jobFinished &&
                        TryDriverCleanup(
                            failures,
                            "dispose late writer",
                            () => lateWriter?.Dispose()
                        );
                    if (initializationFinished && jobFinished && writerClosed)
                    {
                        TryDriverCleanup(
                            failures,
                            "delete lifecycle fixture",
                            () => W6InteractionCaptureHostTestDriver
                                .DeleteTemporaryRoot(root)
                        );
                    }
                    FinishDriverCleanup(primaryFailure, failures, root);
                }
            }
        }

        public static void DestroyDoesNotDuplicateDetachedTerminalization()
        {
            string root = W6InteractionCaptureHostTestDriver
                .CreateTemporaryRoot();
            GameObject owner = null;
            InteractionCaptureWriter lateWriter = null;
            InteractionBackgroundOperation<InteractionCaptureWriter>
                initialization = null;
            InteractionLifecycleTerminalizationJob job = null;
            Exception primaryFailure = null;
            using (var writerCreated = new ManualResetEventSlim(false))
            using (var releaseInitialization = new ManualResetEventSlim(false))
            {
                try
                {
                    owner = new GameObject("W6 Controller Destroy Test");
                    owner.SetActive(false);
                    InteractionRunController controller =
                        owner.AddComponent<InteractionRunController>();
                    owner.SetActive(true);
                    owner.SetActive(false);
                    W6InteractionCaptureHostTestDriver.Require(
                        controller.State == RunState.PreStart,
                        "Warm-up deactivation did not finish Awake."
                    );
                    InteractionRunStateMachine machine =
                        W6InteractionCaptureHostTestDriver
                            .CreateRunningStateMachine(114, "P947");
                    var summary = new InteractionSummaryTracker(
                        machine.Plan.RunId
                    );
                    summary.BeginRun(
                        0d,
                        W6InteractionCaptureHostTestDriver.FixedUtc
                    );
                    summary.BeginPhase(1, 0.1d);
                    initialization = InteractionBackgroundOperation<
                        InteractionCaptureWriter>.Start(() =>
                        {
                            lateWriter = W6InteractionCaptureHostTestDriver
                                .CreateWriter(root, machine.Plan);
                            writerCreated.Set();
                            releaseInitialization.Wait();
                            return lateWriter;
                        });
                    W6InteractionCaptureHostTestDriver.Require(
                        writerCreated.Wait(TimeSpan.FromSeconds(5)),
                        "Destroy scenario writer was not created."
                    );
                    controller.InstallDeterministicScenarioForTests(
                        machine,
                        summary,
                        writer: null,
                        initialization
                    );
                    ArmUnityLifecycleOnlyOutsidePlayMode(controller);
                    owner.SetActive(true);
                    controller.enabled = false;
                    job = controller.LifecycleTerminalizationForTests;
                    W6InteractionCaptureHostTestDriver.Require(
                        job != null,
                        "Real OnDisable did not create detached ownership."
                    );

                    UnityEngine.Object.DestroyImmediate(controller);
                    releaseInitialization.Set();
                    W6InteractionCaptureHostTestDriver.Require(
                        job.Wait(TimeSpan.FromSeconds(10)) &&
                        job.TryConsume(
                            out InteractionLifecycleTerminalizationResult result) &&
                        result.Succeeded && result.Writer == lateWriter &&
                        lateWriter.IsSealed,
                        "Destroyed Controller did not leave one completing detached owner."
                    );
                    AssertTerminalArtifacts(
                        lateWriter.RunDirectory,
                        expectedStatus: "aborted",
                        expectedEvent: InteractionEventNames.RunAborted,
                        forbiddenEvent: InteractionEventNames.RunCompleted
                    );
                }
                catch (Exception exception)
                {
                    primaryFailure = exception;
                    throw;
                }
                finally
                {
                    var failures = new List<Exception>();
                    releaseInitialization.Set();
                    bool initializationFinished = initialization == null ||
                        TryWaitForCleanup(
                            failures,
                            "destroy capture initialization",
                            () => initialization.Wait(TimeSpan.FromSeconds(10)),
                            root
                        );
                    bool jobFinished = job == null || TryWaitForCleanup(
                        failures,
                        "destroy lifecycle terminalization",
                        () => job.Wait(TimeSpan.FromSeconds(10)),
                        root
                    );
                    TryDriverCleanup(failures, "destroy lifecycle owner", () =>
                    {
                        if (owner != null)
                        {
                            UnityEngine.Object.DestroyImmediate(owner);
                        }
                    });
                    bool writerClosed = initializationFinished && jobFinished &&
                        TryDriverCleanup(
                            failures,
                            "dispose destroyed-controller writer",
                            () => lateWriter?.Dispose()
                        );
                    if (initializationFinished && jobFinished && writerClosed)
                    {
                        TryDriverCleanup(
                            failures,
                            "delete destroyed-controller fixture",
                            () => W6InteractionCaptureHostTestDriver
                                .DeleteTemporaryRoot(root)
                        );
                    }
                    FinishDriverCleanup(primaryFailure, failures, root);
                }
            }
        }

        public static void LifecycleQueueFailureConvergesThroughController()
        {
            string root = W6InteractionCaptureHostTestDriver
                .CreateTemporaryRoot();
            Exception primaryFailure = null;
            var writers = new List<InteractionCaptureWriter>();
            try
            {
                RunLifecycleQueueFailureCase(
                    root,
                    117,
                    "P950",
                    new RejectingLifecycleWorkQueue(),
                    destroyController: false,
                    writers
                );
                RunLifecycleQueueFailureCase(
                    root,
                    118,
                    "P951",
                    new ThrowingLifecycleWorkQueue(),
                    destroyController: true,
                    writers
                );
            }
            catch (Exception exception)
            {
                primaryFailure = exception;
                throw;
            }
            finally
            {
                var failures = new List<Exception>();
                bool allWritersClosed = true;
                foreach (InteractionCaptureWriter writer in writers)
                {
                    TryDriverCleanup(
                        failures,
                        "dispose lifecycle queue-failure writer",
                        writer.Dispose
                    );
                    bool closed = TryWaitForCleanup(
                        failures,
                        "lifecycle queue-failure capture handles",
                        () => SpinWait.SpinUntil(
                            () => CanOpenCapturePartialsExclusively(
                                writer.RunDirectory
                            ),
                            TimeSpan.FromSeconds(10)
                        ),
                        root
                    );
                    allWritersClosed &= closed;
                }
                if (allWritersClosed)
                {
                    TryDriverCleanup(
                        failures,
                        "delete lifecycle queue-failure fixture",
                        () => W6InteractionCaptureHostTestDriver
                            .DeleteTemporaryRoot(root)
                    );
                }
                FinishDriverCleanup(primaryFailure, failures, root);
            }
        }

        public static void PendingInitializationQueueFailureConvergesThroughController()
        {
            string root = W6InteractionCaptureHostTestDriver
                .CreateTemporaryRoot();
            Exception primaryFailure = null;
            try
            {
                RunPendingInitializationQueueFailureCase(
                    root,
                    123,
                    "P956",
                    new RejectingLifecycleWorkQueue(),
                    destroyController: false
                );
                RunPendingInitializationQueueFailureCase(
                    root,
                    124,
                    "P957",
                    new ThrowingLifecycleWorkQueue(),
                    destroyController: false
                );
                RunPendingInitializationQueueFailureCase(
                    root,
                    125,
                    "P958",
                    new RejectingLifecycleWorkQueue(),
                    destroyController: true
                );
                RunPendingInitializationQueueFailureCase(
                    root,
                    126,
                    "P959",
                    new ThrowingLifecycleWorkQueue(),
                    destroyController: true
                );
            }
            catch (Exception exception)
            {
                primaryFailure = exception;
                throw;
            }
            finally
            {
                var failures = new List<Exception>();
                TryDriverCleanup(
                    failures,
                    "delete pending lifecycle queue-failure fixture",
                    () => W6InteractionCaptureHostTestDriver
                        .DeleteTemporaryRoot(root)
                );
                FinishDriverCleanup(primaryFailure, failures, root);
            }
        }

        public static void CaptureSamplerEnforcesTwentyHertzCadence()
        {
            GameObject owner = null;
            try
            {
                owner = new GameObject("W6 Capture Sampler Cadence Test");
                InteractionCaptureSampler sampler =
                    owner.AddComponent<InteractionCaptureSampler>();
                int samplesAt72Hz = 0;
                for (int frame = 0; frame < 72; frame++)
                {
                    if (sampler.EvaluateCaptureCadenceForTests(
                            frame / 72d,
                            true))
                    {
                        samplesAt72Hz++;
                    }
                }
                W6InteractionCaptureHostTestDriver.Require(
                    samplesAt72Hz == 20,
                    "Unity sampler emitted more than 20 groups at 72 Hz."
                );
                sampler.ResetCadence();
                int samplesAt90Hz = 0;
                for (int frame = 0; frame < 90; frame++)
                {
                    if (sampler.EvaluateCaptureCadenceForTests(
                            frame / 90d,
                            true))
                    {
                        samplesAt90Hz++;
                    }
                }
                W6InteractionCaptureHostTestDriver.Require(
                    samplesAt90Hz == 20 &&
                    sampler.EvaluateCaptureCadenceForTests(2d, true) &&
                    !sampler.EvaluateCaptureCadenceForTests(2d, true),
                    "Unity sampler drifted or burst after a long frame."
                );
                sampler.enabled = false;
                sampler.enabled = true;
                W6InteractionCaptureHostTestDriver.Require(
                    sampler.EvaluateCaptureCadenceForTests(3d, true),
                    "Unity sampler did not immediately sample after lifecycle reset."
                );
                sampler.ResetCadence();
                W6InteractionCaptureHostTestDriver.Require(
                    sampler.EvaluateCaptureCadenceForTests(3d, true),
                    "Unity sampler did not immediately sample after Run reset."
                );
            }
            finally
            {
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
            }
        }

private static void RunLifecycleQueueFailureCase(
            string root,
            int seed,
            string participantId,
            IInteractionBackgroundWorkQueue workQueue,
            bool destroyController,
            ICollection<InteractionCaptureWriter> writers)
        {
            GameObject owner = new GameObject(
                "W6 Lifecycle Queue Failure " + participantId
            );
            owner.SetActive(false);
            try
            {
                InteractionRunController controller =
                    owner.AddComponent<InteractionRunController>();
                owner.SetActive(true);
                InteractionRunStateMachine machine =
                    W6InteractionCaptureHostTestDriver
                        .CreateRunningStateMachine(seed, participantId);
                var summary = new InteractionSummaryTracker(machine.Plan.RunId);
                summary.BeginRun(0d, W6InteractionCaptureHostTestDriver.FixedUtc);
                summary.BeginPhase(1, 0.1d);
                InteractionCaptureWriter writer =
                    W6InteractionCaptureHostTestDriver.CreateWriter(
                        root,
                        machine.Plan
                    );
                writers.Add(writer);
                writer.BeginCapture();
                W6InteractionCaptureHostTestDriver.WriteOnePoseAndObject(writer);
                controller.InstallDeterministicScenarioForTests(
                    machine,
                    summary,
                    writer,
                    initialization: null
                );
                controller.InstallLifecycleWorkQueueForTests(workQueue);
                ArmUnityLifecycleOnlyOutsidePlayMode(controller);

                if (destroyController)
                {
                    UnityEngine.Object.DestroyImmediate(controller);
                    W6InteractionCaptureHostTestDriver.Require(
                        machine.State == RunState.Faulted &&
                        controller.LifecycleTerminalizationForTests == null,
                        "Destroy did not consume a throwing queue failure."
                    );
                }
                else
                {
                    controller.enabled = false;
                    W6InteractionCaptureHostTestDriver.Require(
                        controller.State == RunState.Faulted &&
                        controller.LifecycleTerminalizationForTests == null &&
                        controller.TerminalSealStateForTests ==
                            InteractionTerminalSealArbitrationState.Terminal &&
                        controller.ResetToPreStart(),
                        "Disable left rejected lifecycle work permanently Aborting."
                    );
                }
                W6InteractionCaptureHostTestDriver.Require(
                    !writer.IsSealed && SpinWait.SpinUntil(
                        () => CanOpenCapturePartialsExclusively(
                            writer.RunDirectory
                        ),
                        TimeSpan.FromSeconds(5)
                    ),
                    "Queue failure did not close and retain recoverable partial data."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static void RunPendingInitializationQueueFailureCase(
            string root,
            int seed,
            string participantId,
            IInteractionBackgroundWorkQueue workQueue,
            bool destroyController)
        {
            GameObject owner = new GameObject(
                "W6 Pending Lifecycle Queue Failure " + participantId
            );
            owner.SetActive(false);
            InteractionCaptureWriter lateWriter = null;
            InteractionBackgroundOperation<InteractionCaptureWriter>
                initialization = null;
            InteractionDetachedInitializationOwner detachedOwner = null;
            InteractionLifecycleTerminalizationJob pendingJob = null;
            using (var writerCreated = new ManualResetEventSlim(false))
            using (var releaseInitialization = new ManualResetEventSlim(false))
            {
                try
                {
                    InteractionRunController controller =
                        owner.AddComponent<InteractionRunController>();
                    owner.SetActive(true);
                    InteractionRunStateMachine machine =
                        W6InteractionCaptureHostTestDriver
                            .CreateRunningStateMachine(seed, participantId);
                    var summary = new InteractionSummaryTracker(
                        machine.Plan.RunId
                    );
                    summary.BeginRun(
                        0d,
                        W6InteractionCaptureHostTestDriver.FixedUtc
                    );
                    summary.BeginPhase(1, 0.1d);
                    initialization = InteractionBackgroundOperation<
                        InteractionCaptureWriter>.Start(() =>
                        {
                            lateWriter = W6InteractionCaptureHostTestDriver
                                .CreateWriter(root, machine.Plan);
                            lateWriter.BeginCapture();
                            writerCreated.Set();
                            releaseInitialization.Wait();
                            return lateWriter;
                        });
                    W6InteractionCaptureHostTestDriver.Require(
                        writerCreated.Wait(TimeSpan.FromSeconds(5)),
                        "Controller pending initialization did not create its writer."
                    );
                    controller.InstallDeterministicScenarioForTests(
                        machine,
                        summary,
                        writer: null,
                        initialization
                    );
                    controller.InstallLifecycleWorkQueueForTests(workQueue);
                    ArmUnityLifecycleOnlyOutsidePlayMode(controller);

                    if (destroyController)
                    {
                        UnityEngine.Object.DestroyImmediate(controller);
                    }
                    else
                    {
                        controller.enabled = false;
                    }
                    pendingJob =
                        controller.LifecycleTerminalizationForTests;
                    W6InteractionCaptureHostTestDriver.Require(
                        machine.State == RunState.Faulted &&
                        pendingJob == null &&
                        controller.TerminalSealStateForTests ==
                            InteractionTerminalSealArbitrationState.Terminal,
                        "Lifecycle callback returned before pending queue " +
                        "failure reached Faulted/Terminal."
                    );
                    detachedOwner =
                        controller.DetachedInitializationOwnerForTests;
                    W6InteractionCaptureHostTestDriver.Require(
                        detachedOwner != null && !detachedOwner.IsCompleted,
                        "Controller did not publish detached late-writer ownership."
                    );
                    if (!destroyController)
                    {
                        W6InteractionCaptureHostTestDriver.Require(
                            controller.ResetToPreStart(),
                            "Pending queue failure left Reset permanently blocked."
                        );
                    }

                    releaseInitialization.Set();
                    W6InteractionCaptureHostTestDriver.Require(
                        initialization.Wait(TimeSpan.FromSeconds(5)) &&
                        initialization.Succeeded,
                        "Controller late initialization did not finish."
                    );
                    W6InteractionCaptureHostTestDriver.Require(
                        SpinWait.SpinUntil(
                            () => detachedOwner.IsCompleted,
                            TimeSpan.FromSeconds(5)
                        ) && detachedOwner.WriterOwned &&
                        detachedOwner.Error == null &&
                        SpinWait.SpinUntil(
                            () => CanOpenCapturePartialsExclusively(
                                lateWriter.RunDirectory
                            ),
                            TimeSpan.FromSeconds(5)
                        ),
                        "Detached owner did not close Controller late writer."
                    );
                    W6InteractionCaptureHostTestDriver.Require(
                        !File.Exists(Path.Combine(
                            lateWriter.RunDirectory,
                            InteractionStoragePaths.SummaryFileName
                        )) &&
                        CountTerminalEvents(lateWriter.RunDirectory) == 0,
                        "Pending queue failure wrote duplicate terminal artifacts."
                    );
                }
                finally
                {
                    releaseInitialization.Set();
                    bool initializationCompleted = initialization == null ||
                        initialization.Wait(TimeSpan.FromSeconds(5));
                    bool detachedCompleted = detachedOwner == null ||
                        SpinWait.SpinUntil(
                            () => detachedOwner.IsCompleted,
                            TimeSpan.FromSeconds(5)
                        );
                    bool jobCompleted = pendingJob == null ||
                        pendingJob.Wait(TimeSpan.FromSeconds(5));
                    if (pendingJob != null && pendingJob.IsCompleted)
                    {
                        pendingJob.TryConsume(out _);
                    }
                    if (initializationCompleted && detachedCompleted &&
                        jobCompleted)
                    {
                        lateWriter?.Dispose();
                    }
                    UnityEngine.Object.DestroyImmediate(owner);
                }
            }
        }

        private static int CountTerminalEvents(string runDirectory)
        {
            string partial = Path.Combine(
                runDirectory,
                "." + InteractionStoragePaths.EventsFileName + ".partial"
            );
            if (!File.Exists(partial))
            {
                return 0;
            }
            string text = File.ReadAllText(partial);
            return CountOccurrences(text, "\"event_type\":\"run_completed\"") +
                CountOccurrences(text, "\"event_type\":\"run_aborted\"");
        }

        private static bool CanOpenCapturePartialsExclusively(
            string runDirectory)
        {
            string[] names =
            {
                InteractionStoragePaths.EventsFileName,
                InteractionStoragePaths.PosesFileName,
                InteractionStoragePaths.ObjectsFileName
            };
            try
            {
                foreach (string name in names)
                {
                    string path = Path.Combine(runDirectory, "." + name + ".partial");
                    if (!File.Exists(path))
                    {
                        continue;
                    }
                    using (new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.None))
                    {
                    }
                }
                return true;
            }
            catch (IOException)
            {
                Thread.Sleep(5);
                return false;
            }
        }

        private static bool TryWaitForCleanup(
            ICollection<Exception> failures,
            string ownerName,
            Func<bool> wait,
            string retainedPath)
        {
            try
            {
                if (wait())
                {
                    return true;
                }
                failures.Add(new TimeoutException(
                    ownerName + " did not finish within the cleanup bound; " +
                    "active data was retained at " + retainedPath + "."
                ));
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    ownerName + " cleanup wait failed; active data was retained " +
                    "at " + retainedPath + ".",
                    exception
                ));
            }
            return false;
        }

        private static bool TryDriverCleanup(
            ICollection<Exception> failures,
            string actionName,
            Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    actionName + " failed.",
                    exception
                ));
                return false;
            }
        }

        private static void FinishDriverCleanup(
            Exception primaryFailure,
            ICollection<Exception> cleanupFailures,
            string retainedPath)
        {
            if (cleanupFailures.Count == 0)
            {
                return;
            }
            string details = string.Join(
                Environment.NewLine,
                System.Linq.Enumerable.Select(
                    cleanupFailures,
                    failure => failure.ToString()
                )
            );
            if (primaryFailure != null)
            {
                primaryFailure.Data["W6CleanupFailures"] = details;
                primaryFailure.Data["W6RetainedPath"] = retainedPath;
                return;
            }
            throw new AggregateException(
                "W6 test cleanup failed; inspect retained path " +
                retainedPath + ".",
                cleanupFailures
            );
        }

        private static void ArmUnityLifecycleOnlyOutsidePlayMode(
            InteractionRunController controller)
        {
            if (!Application.isPlaying)
            {
                controller.ArmUnityLifecycleForTests();
            }
        }

        private static void AssertTerminalArtifacts(
            string runDirectory,
            string expectedStatus,
            string expectedEvent,
            string forbiddenEvent)
        {
            string events = File.ReadAllText(Path.Combine(
                runDirectory,
                InteractionStoragePaths.EventsFileName
            ));
            string summary = File.ReadAllText(Path.Combine(
                runDirectory,
                InteractionStoragePaths.SummaryFileName
            ));
            W6InteractionCaptureHostTestDriver.Require(
                CountOccurrences(
                    events,
                    "\"event_type\":\"" + expectedEvent + "\""
                ) == 1 &&
                CountOccurrences(
                    events,
                    "\"event_type\":\"" + forbiddenEvent + "\""
                ) == 0 &&
                summary.IndexOf(
                    "\"status\":\"" + expectedStatus + "\"",
                    StringComparison.Ordinal
                ) >= 0 &&
                Directory.GetFiles(
                    runDirectory,
                    InteractionStoragePaths.SummaryFileName
                ).Length == 1,
                "Controller terminal artifacts contain duplicate/conflicting output."
            );
        }

        private static int CountOccurrences(string value, string needle)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(
                       needle,
                       offset,
                       StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += needle.Length;
            }
            return count;
        }

        private sealed class RejectingLifecycleWorkQueue :
            IInteractionBackgroundWorkQueue
        {
            public bool TryQueue(Action work)
            {
                return false;
            }
        }

        private sealed class ThrowingLifecycleWorkQueue :
            IInteractionBackgroundWorkQueue
        {
            public bool TryQueue(Action work)
            {
                throw new InvalidOperationException("Injected queue failure.");
            }
        }
    }
}
#endif
