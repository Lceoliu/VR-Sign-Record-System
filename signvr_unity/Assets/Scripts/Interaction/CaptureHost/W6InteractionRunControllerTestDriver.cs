#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SignVR.Interaction.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace SignVR.Interaction.CaptureHost
{
    /// <summary>
    /// Editor-only behavioral driver. It crosses the real Controller and Host
    /// interfaces while using actual capture files; no Study Player surface is
    /// added and no private-field reflection is used.
    /// </summary>
    public static class W6InteractionRunControllerTestDriver
    {
        public static void ReadinessReplacementCancelsOnlyPreviousReadiness()
        {
            GameObject owner = null;
            InteractionHostClient host = null;
            LocalReadinessRequestFactory requests = null;
            IEnumerator firstOuter = null;
            IEnumerator firstSend = null;
            IEnumerator secondOuter = null;
            IEnumerator secondSend = null;
            Exception primaryFailure = null;
            try
            {
                owner = new GameObject("W6 Readiness Replacement Test");
                owner.SetActive(false);
                host = owner.AddComponent<InteractionHostClient>();
                requests = new LocalReadinessRequestFactory(
                    CreateLocalReadinessRequestUrl()
                );
                host.InstallRequestFactoryForTests(requests);

                var heartbeat = new ObservableCancelableRequest();
                var registration = new ObservableCancelableRequest();
                var upload = new ObservableCancelableRequest();
                host.RegisterActiveRequestForTests(heartbeat);
                host.RegisterActiveRequestForTests(registration);
                host.RegisterActiveRequestForTests(upload);

                int firstCallbacks = 0;
                firstOuter = host.GetReadiness(_ => firstCallbacks++);
                firstSend = BeginInFlightReadiness(
                    firstOuter,
                    "first readiness",
                    out UnityWebRequestAsyncOperation firstOperation
                );
                InteractionUnityWebRequestCancellation firstCancellation =
                    host.ActiveReadinessRequestForTests;
                W6InteractionCaptureHostTestDriver.Require(
                    firstOperation != null && firstCancellation != null &&
                    ReferenceEquals(
                        firstOperation.webRequest,
                        requests.LastGetRequest
                    ) && firstCancellation.AbortCountForTests == 0 &&
                    firstCancellation.DisposeCountForTests == 0 &&
                    host.ActiveRequestCount == 4,
                    "First readiness did not enter a real owned web request."
                );

                int secondCallbacks = 0;
                secondOuter = host.GetReadiness(_ => secondCallbacks++);
                secondSend = BeginInFlightReadiness(
                    secondOuter,
                    "replacement readiness",
                    out UnityWebRequestAsyncOperation secondOperation
                );
                InteractionUnityWebRequestCancellation secondCancellation =
                    host.ActiveReadinessRequestForTests;
                W6InteractionCaptureHostTestDriver.Require(
                    secondOperation != null && secondCancellation != null &&
                    !ReferenceEquals(firstCancellation, secondCancellation) &&
                    firstCancellation.AbortCountForTests == 1 &&
                    firstCancellation.DisposeCountForTests == 1 &&
                    ReferenceEquals(
                        secondOperation.webRequest,
                        requests.LastGetRequest
                    ) && requests.CreateGetCount == 2 &&
                    host.ActiveRequestCount == 4 &&
                    heartbeat.AbortCount == 0 &&
                    registration.AbortCount == 0 &&
                    upload.AbortCount == 0,
                    "Replacing readiness cancelled unrelated Host operations."
                );

                W6InteractionCaptureHostTestDriver.Require(
                    !firstSend.MoveNext() && firstCallbacks == 0 &&
                    firstCancellation.AbortCountForTests == 1 &&
                    firstCancellation.DisposeCountForTests == 1,
                    "A replaced in-flight readiness published a stale result."
                );
                W6InteractionCaptureHostTestDriver.Require(
                    ReferenceEquals(
                        host.ActiveReadinessRequestForTests,
                        secondCancellation
                    ) && secondCancellation.AbortCountForTests == 0 &&
                    secondCancellation.DisposeCountForTests == 0 &&
                    host.ActiveRequestCount == 4,
                    "The replacement readiness was not retained for completion."
                );
                W6InteractionCaptureHostTestDriver.Require(
                    host.CancelReadinessRequest() &&
                    host.ActiveRequestCount == 3 &&
                    secondCancellation.AbortCountForTests == 1 &&
                    secondCancellation.DisposeCountForTests == 1 &&
                    heartbeat.AbortCount == 0 &&
                    registration.AbortCount == 0 &&
                    upload.AbortCount == 0,
                    "The replacement readiness did not retain exclusive slot ownership."
                );
                W6InteractionCaptureHostTestDriver.Require(
                    !secondSend.MoveNext() && secondCallbacks == 0 &&
                    secondCancellation.AbortCountForTests == 1 &&
                    secondCancellation.DisposeCountForTests == 1,
                    "Explicit readiness cancellation published a stale completion."
                );
                W6InteractionCaptureHostTestDriver.Require(
                    host.CancelActiveRequests() == 3 &&
                    heartbeat.AbortCount == 1 &&
                    registration.AbortCount == 1 &&
                    upload.AbortCount == 1,
                    "Unrelated Host operations did not retain lifecycle ownership."
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
                    "cancel replacement Host requests",
                    () => host?.CancelActiveRequests()
                );
                TryDriverCleanup(
                    failures,
                    "dispose first readiness Send",
                    () => (firstSend as IDisposable)?.Dispose()
                );
                TryDriverCleanup(
                    failures,
                    "dispose second readiness Send",
                    () => (secondSend as IDisposable)?.Dispose()
                );
                TryDriverCleanup(
                    failures,
                    "dispose first readiness envelope",
                    () => (firstOuter as IDisposable)?.Dispose()
                );
                TryDriverCleanup(
                    failures,
                    "dispose second readiness envelope",
                    () => (secondOuter as IDisposable)?.Dispose()
                );
                TryDriverCleanup(failures, "destroy readiness fixture", () =>
                {
                    if (owner != null)
                    {
                        UnityEngine.Object.DestroyImmediate(owner);
                    }
                });
                FinishDriverCleanup(
                    primaryFailure,
                    failures,
                    "in-memory readiness replacement fixture"
                );
            }
        }

        public static void ReadinessInvalidationPreservesOtherOperations()
        {
            GameObject owner = null;
            InteractionHostClient host = null;
            LocalReadinessRequestFactory requests = null;
            IEnumerator readinessOuter = null;
            IEnumerator readinessSend = null;
            Exception primaryFailure = null;
            try
            {
                owner = new GameObject("W6 Readiness Invalidation Test");
                owner.SetActive(false);
                host = owner.AddComponent<InteractionHostClient>();
                InteractionRunController controller =
                    owner.AddComponent<InteractionRunController>();
                requests = new LocalReadinessRequestFactory(
                    CreateLocalReadinessRequestUrl()
                );
                host.InstallRequestFactoryForTests(requests);
                controller.ConfigureHostClient(host);

                var heartbeat = new ObservableCancelableRequest();
                var registration = new ObservableCancelableRequest();
                var upload = new ObservableCancelableRequest();
                host.RegisterActiveRequestForTests(heartbeat);
                host.RegisterActiveRequestForTests(registration);
                host.RegisterActiveRequestForTests(upload);

                int callbacks = 0;
                readinessOuter = host.GetReadiness(_ => callbacks++);
                readinessSend = BeginInFlightReadiness(
                    readinessOuter,
                    "invalidated readiness",
                    out UnityWebRequestAsyncOperation readinessOperation
                );
                InteractionUnityWebRequestCancellation readinessCancellation =
                    host.ActiveReadinessRequestForTests;
                W6InteractionCaptureHostTestDriver.Require(
                    readinessOperation != null &&
                    readinessCancellation != null &&
                    ReferenceEquals(
                        readinessOperation.webRequest,
                        requests.LastGetRequest
                    ) && requests.CreateGetCount == 1 &&
                    readinessCancellation.AbortCountForTests == 0 &&
                    readinessCancellation.DisposeCountForTests == 0 &&
                    host.ActiveRequestCount == 4,
                    "Invalidation fixture did not enter an owned web request."
                );

                controller.InvalidateHostReadiness();
                W6InteractionCaptureHostTestDriver.Require(
                    host.ActiveRequestCount == 3 &&
                    readinessCancellation.AbortCountForTests == 1 &&
                    readinessCancellation.DisposeCountForTests == 1 &&
                    heartbeat.AbortCount == 0 &&
                    registration.AbortCount == 0 &&
                    upload.AbortCount == 0 &&
                    !host.CancelReadinessRequest(),
                    "Readiness invalidation cancelled or retained unrelated Host work."
                );
                W6InteractionCaptureHostTestDriver.Require(
                    !readinessSend.MoveNext() && callbacks == 0 &&
                    readinessCancellation.AbortCountForTests == 1 &&
                    readinessCancellation.DisposeCountForTests == 1 &&
                    host.ActiveRequestCount == 3,
                    "An invalidated readiness published a stale result."
                );
                W6InteractionCaptureHostTestDriver.Require(
                    host.CancelActiveRequests() == 3 &&
                    heartbeat.AbortCount == 1 &&
                    registration.AbortCount == 1 &&
                    upload.AbortCount == 1,
                    "Invalidation removed other requests from lifecycle ownership."
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
                    "cancel invalidation Host requests",
                    () => host?.CancelActiveRequests()
                );
                TryDriverCleanup(
                    failures,
                    "dispose invalidated readiness Send",
                    () => (readinessSend as IDisposable)?.Dispose()
                );
                TryDriverCleanup(
                    failures,
                    "dispose invalidated readiness envelope",
                    () => (readinessOuter as IDisposable)?.Dispose()
                );
                TryDriverCleanup(failures, "destroy invalidation fixture", () =>
                {
                    if (owner != null)
                    {
                        UnityEngine.Object.DestroyImmediate(owner);
                    }
                });
                FinishDriverCleanup(
                    primaryFailure,
                    failures,
                    "in-memory readiness invalidation fixture"
                );
            }
        }

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
                InteractionHostClient host =
                    owner.AddComponent<InteractionHostClient>();
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
                    initialization: null,
                    host
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
                    InteractionHostClient host =
                        owner.AddComponent<InteractionHostClient>();
                    InteractionRunController controller =
                        owner.AddComponent<InteractionRunController>();
                    owner.SetActive(true);
                    owner.SetActive(false);
                    W6InteractionCaptureHostTestDriver.Require(
                        controller.State == RunState.PreStart &&
                        !host.HeartbeatLoopActiveForTests &&
                        host.ActiveRequestCount == 0,
                        "Warm-up deactivation did not finish Awake and clear " +
                        "ordinary Host ownership."
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
                    var request = new ObservableCancelableRequest();
                    host.PrepareLifecycleOwnershipForTests(request, 81L);
                    controller.InstallDeterministicScenarioForTests(
                        machine,
                        summary,
                        writer: null,
                        initialization,
                        host
                    );
                    ArmUnityLifecycleOnlyOutsidePlayMode(controller);
                    owner.SetActive(true);

                    controller.enabled = false;
                    job = controller.LifecycleTerminalizationForTests;
                    W6InteractionCaptureHostTestDriver.Require(
                        controller.State == RunState.Aborting && job != null &&
                        !host.HeartbeatLoopActiveForTests &&
                        host.ActiveRequestCount == 0 && request.WasAborted,
                        "Controller disable did not stop Host ownership and begin Abort."
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
                    InteractionHostClient host =
                        owner.AddComponent<InteractionHostClient>();
                    InteractionRunController controller =
                        owner.AddComponent<InteractionRunController>();
                    owner.SetActive(true);
                    owner.SetActive(false);
                    W6InteractionCaptureHostTestDriver.Require(
                        controller.State == RunState.PreStart &&
                        !host.HeartbeatLoopActiveForTests &&
                        host.ActiveRequestCount == 0,
                        "Warm-up deactivation did not finish Awake and clear " +
                        "ordinary Host ownership."
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
                    var disableRequest = new ObservableCancelableRequest();
                    host.PrepareLifecycleOwnershipForTests(disableRequest, 82L);
                    controller.InstallDeterministicScenarioForTests(
                        machine,
                        summary,
                        writer: null,
                        initialization,
                        host
                    );
                    ArmUnityLifecycleOnlyOutsidePlayMode(controller);
                    owner.SetActive(true);
                    controller.enabled = false;
                    job = controller.LifecycleTerminalizationForTests;
                    W6InteractionCaptureHostTestDriver.Require(
                        job != null && disableRequest.WasAborted,
                        "Real OnDisable did not create detached ownership."
                    );

                    var destroyRequest = new ObservableCancelableRequest();
                    host.RegisterActiveRequestForTests(destroyRequest);
                    UnityEngine.Object.DestroyImmediate(controller);
                    W6InteractionCaptureHostTestDriver.Require(
                        destroyRequest.WasAborted && host.ActiveRequestCount == 0,
                        "Real OnDestroy did not traverse Controller to Host cancellation."
                    );
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

        public static void ControllerDisableCancelsOwnedArtifactFreeze()
        {
            string root = W6InteractionCaptureHostTestDriver
                .CreateTemporaryRoot();
            GameObject owner = null;
            InteractionCaptureWriter writer = null;
            InteractionArtifactOperation<InteractionFrozenArtifactSet>
                operation = null;
            InteractionArtifactOperationRegistry operationOwner = null;
            var observer = new W6InteractionCaptureHostTestDriver
                .ControlledArtifactReadObserver();
            var replacementObserver = new W6InteractionCaptureHostTestDriver
                .CountingArtifactReadObserver();
            var workQueue = new W6InteractionCaptureHostTestDriver
                .ControlledBackgroundWorkQueue();
            Exception primaryFailure = null;
            try
            {
                CreateCompletedControllerFixture(
                    root,
                    115,
                    "P948",
                    out owner,
                    out InteractionRunController controller,
                    out InteractionRunStateMachine machine,
                    out writer
                );
                ArmUnityLifecycleOnlyOutsidePlayMode(controller);
                operationOwner = controller.ArtifactOperationRegistryForTests;
                controller.InstallArtifactWorkQueueForTests(workQueue);
                W6InteractionCaptureHostTestDriver.Require(
                    controller.BeginArtifactFreezeForTests(
                        writer.RunDirectory,
                        observer,
                        out operation
                    ) && workQueue.WorkerWaitingForRelease.Wait(
                        TimeSpan.FromSeconds(5)
                    ),
                    "Real Controller freeze was not owned before worker release."
                );
                controller.InstallArtifactReadObserverForTests(
                    replacementObserver
                );
                workQueue.ReleaseWork.Set();
                bool startupObserverEntered = observer.ChunkEntered.Wait(
                    TimeSpan.FromSeconds(5)
                );

                controller.enabled = false;
                W6InteractionCaptureHostTestDriver.Require(
                    startupObserverEntered &&
                    observer.CancellationReached.Wait(TimeSpan.FromSeconds(5)) &&
                    replacementObserver.ObservedChunkCount == 0 &&
                    workQueue.QueuedCount == 1 &&
                    controller.ActiveArtifactOperationCountForTests == 1 &&
                    machine.State == RunState.Completed,
                    "Controller did not use its startup observer snapshot or cancel cleanly."
                );
                W6InteractionCaptureHostTestDriver.Require(
                    !controller.BeginArtifactFreezeForTests(
                        writer.RunDirectory,
                        InteractionArtifactReadObserver.None,
                        out InteractionArtifactOperation<
                            InteractionFrozenArtifactSet> _
                    ) && observer.MaximumConcurrentChunks == 1,
                    "Controller started an overlapping freeze before reap."
                );
                observer.ReleaseAfterCancellation.Set();
                W6InteractionCaptureHostTestDriver.Require(
                    operation.Wait(TimeSpan.FromSeconds(5)) &&
                    operation.CancellationObserved &&
                    SpinWait.SpinUntil(
                        () => controller.ActiveArtifactOperationCountForTests == 0 &&
                            operation.IsReaped,
                        TimeSpan.FromSeconds(5)
                    ) && machine.State == RunState.Completed,
                    "Controller did not reap its cancelled freeze deterministically."
                );
                W6InteractionCaptureHostTestDriver.AssertCanOpenExclusively(
                    Path.Combine(
                        writer.RunDirectory,
                        InteractionStoragePaths.EventsFileName
                    )
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
                workQueue.ReleaseWork.Set();
                observer.ReleaseAfterCancellation.Set();
                TryDriverCleanup(failures, "destroy freeze-disable fixture", () =>
                {
                    if (owner != null)
                    {
                        UnityEngine.Object.DestroyImmediate(owner);
                    }
                });
                bool operationFinished = operation == null ||
                    TryWaitForCleanup(
                        failures,
                        "Controller disable artifact freeze",
                        () => operation.Wait(TimeSpan.FromSeconds(10)),
                        root
                    );
                bool ownershipReleased = VerifyArtifactOwnershipReleased(
                    failures,
                    "Controller disable artifact freeze",
                    root,
                    operationFinished,
                    operationOwner,
                    operation
                );
                if (ownershipReleased)
                {
                    TryDriverCleanup(
                        failures,
                        "dispose freeze-disable observer",
                        observer.Dispose
                    );
                    TryDriverCleanup(
                        failures,
                        "dispose freeze-disable work queue",
                        workQueue.Dispose
                    );
                }
                bool writerClosed = ownershipReleased && TryDriverCleanup(
                    failures,
                    "dispose freeze-disable writer",
                    () => writer?.Dispose()
                );
                if (ownershipReleased && writerClosed)
                {
                    TryDriverCleanup(failures, "delete freeze-disable fixture", () =>
                        W6InteractionCaptureHostTestDriver
                            .DeleteTemporaryRoot(root));
                }
                FinishDriverCleanup(primaryFailure, failures, root);
            }
        }

        public static void ControllerDestroyCancelsOwnedArtifactFreeze()
        {
            string root = W6InteractionCaptureHostTestDriver
                .CreateTemporaryRoot();
            GameObject owner = null;
            InteractionCaptureWriter writer = null;
            InteractionArtifactOperation<InteractionFrozenArtifactSet>
                operation = null;
            InteractionArtifactOperationRegistry operationOwner = null;
            var observer = new W6InteractionCaptureHostTestDriver
                .ControlledArtifactReadObserver();
            var replacementObserver = new W6InteractionCaptureHostTestDriver
                .CountingArtifactReadObserver();
            var workQueue = new W6InteractionCaptureHostTestDriver
                .ControlledBackgroundWorkQueue();
            Exception primaryFailure = null;
            try
            {
                CreateCompletedControllerFixture(
                    root,
                    116,
                    "P949",
                    out owner,
                    out InteractionRunController controller,
                    out InteractionRunStateMachine machine,
                    out writer
                );
                ArmUnityLifecycleOnlyOutsidePlayMode(controller);
                operationOwner = controller.ArtifactOperationRegistryForTests;
                controller.InstallArtifactWorkQueueForTests(workQueue);
                W6InteractionCaptureHostTestDriver.Require(
                    controller.BeginArtifactFreezeForTests(
                        writer.RunDirectory,
                        observer,
                        out operation
                    ) && workQueue.WorkerWaitingForRelease.Wait(
                        TimeSpan.FromSeconds(5)
                    ),
                    "Destroy freeze was not owned before worker release."
                );
                controller.InstallArtifactReadObserverForTests(
                    replacementObserver
                );
                workQueue.ReleaseWork.Set();
                bool startupObserverEntered = observer.ChunkEntered.Wait(
                    TimeSpan.FromSeconds(5)
                );
                UnityEngine.Object.DestroyImmediate(controller);
                W6InteractionCaptureHostTestDriver.Require(
                    startupObserverEntered &&
                    observer.CancellationReached.Wait(TimeSpan.FromSeconds(5)) &&
                    replacementObserver.ObservedChunkCount == 0 &&
                    workQueue.QueuedCount == 1 &&
                    machine.State == RunState.Completed,
                    "Destroyed Controller did not preserve its worker snapshot."
                );
                observer.ReleaseAfterCancellation.Set();
                W6InteractionCaptureHostTestDriver.Require(
                    operation.Wait(TimeSpan.FromSeconds(5)) &&
                    operation.CancellationObserved &&
                    SpinWait.SpinUntil(
                        () => operation.IsReaped,
                        TimeSpan.FromSeconds(5)
                    ) && machine.State == RunState.Completed,
                    "Destroyed Controller left its freeze operation orphaned."
                );
                W6InteractionCaptureHostTestDriver.AssertCanOpenExclusively(
                    Path.Combine(
                        writer.RunDirectory,
                        InteractionStoragePaths.EventsFileName
                    )
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
                workQueue.ReleaseWork.Set();
                observer.ReleaseAfterCancellation.Set();
                TryDriverCleanup(failures, "destroy freeze-destroy fixture", () =>
                {
                    if (owner != null)
                    {
                        UnityEngine.Object.DestroyImmediate(owner);
                    }
                });
                bool operationFinished = operation == null ||
                    TryWaitForCleanup(
                        failures,
                        "Controller destroy artifact freeze",
                        () => operation.Wait(TimeSpan.FromSeconds(10)),
                        root
                    );
                bool ownershipReleased = VerifyArtifactOwnershipReleased(
                    failures,
                    "Controller destroy artifact freeze",
                    root,
                    operationFinished,
                    operationOwner,
                    operation
                );
                if (ownershipReleased)
                {
                    TryDriverCleanup(
                        failures,
                        "dispose freeze-destroy observer",
                        observer.Dispose
                    );
                    TryDriverCleanup(
                        failures,
                        "dispose freeze-destroy work queue",
                        workQueue.Dispose
                    );
                }
                bool writerClosed = ownershipReleased && TryDriverCleanup(
                    failures,
                    "dispose freeze-destroy writer",
                    () => writer?.Dispose()
                );
                if (ownershipReleased && writerClosed)
                {
                    TryDriverCleanup(failures, "delete freeze-destroy fixture", () =>
                        W6InteractionCaptureHostTestDriver
                            .DeleteTemporaryRoot(root));
                }
                FinishDriverCleanup(primaryFailure, failures, root);
            }
        }

        public static void HostDisableCancelsOwnedArtifactVerify()
        {
            string root = W6InteractionCaptureHostTestDriver
                .CreateTemporaryRoot();
            GameObject owner = null;
            InteractionArtifactOperation<bool> operation = null;
            InteractionArtifactOperationRegistry operationOwner = null;
            var observer = new W6InteractionCaptureHostTestDriver
                .ControlledArtifactReadObserver();
            var replacementObserver = new W6InteractionCaptureHostTestDriver
                .CountingArtifactReadObserver();
            var workQueue = new W6InteractionCaptureHostTestDriver
                .ControlledBackgroundWorkQueue();
            Exception primaryFailure = null;
            try
            {
                string runDirectory = W6InteractionCaptureHostTestDriver
                    .BuildStrictHostFixture(root);
                InteractionFrozenArtifact artifact =
                    W6InteractionCaptureHostTestDriver.Await(
                        InteractionFrozenArtifactSet.BeginReadOnce(runDirectory)
                    ).For(InteractionArtifactTypes.Events);
                owner = new GameObject("W6 Host Verify Lifecycle Test");
                owner.SetActive(false);
                InteractionHostClient host =
                    owner.AddComponent<InteractionHostClient>();
                owner.SetActive(true);
                operationOwner = host.ArtifactOperationRegistryForTests;
                ArmUnityLifecycleOnlyOutsidePlayMode(host);
                host.InstallArtifactWorkQueueForTests(workQueue);
                host.InstallArtifactReadObserverForTests(observer);
                InteractionHostResult<bool> firstResult = null;
                IEnumerator first = host.PutArtifact(
                    "run_host_verify_lifecycle",
                    artifact,
                    value => firstResult = value
                );
                W6InteractionCaptureHostTestDriver.Require(
                    first.MoveNext() &&
                    workQueue.WorkerWaitingForRelease.Wait(
                        TimeSpan.FromSeconds(5)
                    ) &&
                    host.TryGetArtifactVerificationForTests(
                        artifact,
                        out operation
                    ),
                    "Public Host PutArtifact did not own its verification."
                );
                host.InstallArtifactReadObserverForTests(replacementObserver);
                workQueue.ReleaseWork.Set();
                bool startupObserverEntered = observer.ChunkEntered.Wait(
                    TimeSpan.FromSeconds(5)
                );

                host.enabled = false;
                W6InteractionCaptureHostTestDriver.Require(
                    startupObserverEntered &&
                    observer.CancellationReached.Wait(TimeSpan.FromSeconds(5)) &&
                    replacementObserver.ObservedChunkCount == 0 &&
                    workQueue.QueuedCount == 1 &&
                    host.ActiveArtifactOperationCountForTests == 1 &&
                    firstResult == null,
                    "Host did not preserve its startup observer snapshot."
                );
                InteractionHostResult<bool> duplicateResult = null;
                IEnumerator duplicate = host.PutArtifact(
                    "run_host_verify_lifecycle",
                    artifact,
                    value => duplicateResult = value
                );
                W6InteractionCaptureHostTestDriver.Require(
                    !duplicate.MoveNext() && duplicateResult != null &&
                    !duplicateResult.Success &&
                    observer.MaximumConcurrentChunks == 1,
                    "Host retry overlapped the interrupted verification."
                );
                observer.ReleaseAfterCancellation.Set();
                W6InteractionCaptureHostTestDriver.Require(
                    operation.Wait(TimeSpan.FromSeconds(5)) &&
                    operation.CancellationObserved &&
                    SpinWait.SpinUntil(
                        () => host.ActiveArtifactOperationCountForTests == 0 &&
                            operation.IsReaped,
                        TimeSpan.FromSeconds(5)
                    ),
                    "Host did not reap verification after its outer coroutine stopped."
                );
                W6InteractionCaptureHostTestDriver.AssertCanOpenExclusively(
                    artifact.Path
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
                workQueue.ReleaseWork.Set();
                observer.ReleaseAfterCancellation.Set();
                TryDriverCleanup(failures, "destroy Host verify fixture", () =>
                {
                    if (owner != null)
                    {
                        UnityEngine.Object.DestroyImmediate(owner);
                    }
                });
                bool operationFinished = operation == null ||
                    TryWaitForCleanup(
                        failures,
                        "Host artifact verification",
                        () => operation.Wait(TimeSpan.FromSeconds(10)),
                        root
                    );
                bool ownershipReleased = VerifyArtifactOwnershipReleased(
                    failures,
                    "Host artifact verification",
                    root,
                    operationFinished,
                    operationOwner,
                    operation
                );
                if (ownershipReleased)
                {
                    TryDriverCleanup(
                        failures,
                        "dispose Host verify observer",
                        observer.Dispose
                    );
                    TryDriverCleanup(
                        failures,
                        "dispose Host verify work queue",
                        workQueue.Dispose
                    );
                    TryDriverCleanup(failures, "delete Host verify fixture", () =>
                        W6InteractionCaptureHostTestDriver
                            .DeleteTemporaryRoot(root));
                }
                FinishDriverCleanup(primaryFailure, failures, root);
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

        public static void HostLifecycleEpochRejectsStaleArtifactIterator()
        {
            string root = W6InteractionCaptureHostTestDriver
                .CreateTemporaryRoot();
            GameObject owner = null;
            InteractionArtifactOperation<bool> operation = null;
            InteractionArtifactOperationRegistry operationOwner = null;
            var workQueue = new W6InteractionCaptureHostTestDriver
                .ControlledBackgroundWorkQueue();
            var requests = new CountingHostRequestFactory();
            Exception primaryFailure = null;
            try
            {
                string runDirectory = W6InteractionCaptureHostTestDriver
                    .BuildStrictHostFixture(root);
                InteractionFrozenArtifact artifact =
                    W6InteractionCaptureHostTestDriver.Await(
                        InteractionFrozenArtifactSet.BeginReadOnce(runDirectory)
                    ).For(InteractionArtifactTypes.Events);
                owner = new GameObject("W6 Host Request Epoch Test");
                owner.SetActive(false);
                InteractionHostClient host =
                    owner.AddComponent<InteractionHostClient>();
                owner.SetActive(true);
                operationOwner = host.ArtifactOperationRegistryForTests;
                ArmUnityLifecycleOnlyOutsidePlayMode(host);
                host.InstallArtifactWorkQueueForTests(workQueue);
                host.InstallRequestFactoryForTests(requests);

                int staleCallbacks = 0;
                InteractionHostResult<bool> staleResult = null;
                IEnumerator stale = host.PutArtifact(
                    "run_host_epoch",
                    artifact,
                    value =>
                    {
                        staleCallbacks++;
                        staleResult = value;
                    }
                );
                W6InteractionCaptureHostTestDriver.Require(
                    stale.MoveNext() &&
                    host.TryGetArtifactVerificationForTests(
                        artifact,
                        out operation
                    ) &&
                    workQueue.WorkerWaitingForRelease.Wait(
                        TimeSpan.FromSeconds(5)
                    ),
                    "Host stale-request fixture did not begin verification."
                );
                workQueue.ReleaseWork.Set();
                W6InteractionCaptureHostTestDriver.Require(
                    operation.Wait(TimeSpan.FromSeconds(5)) &&
                    SpinWait.SpinUntil(
                        () => operation.IsReaped &&
                            host.ActiveArtifactOperationCountForTests == 0,
                        TimeSpan.FromSeconds(5)
                    ),
                    "Host verification did not finish before epoch cancellation."
                );

                host.enabled = false;
                W6InteractionCaptureHostTestDriver.Require(
                    !stale.MoveNext() && staleCallbacks == 1 &&
                    staleResult != null && !staleResult.Success &&
                    requests.FileRequestCount == 0 &&
                    host.ActiveRequestCount == 0,
                    "A stale PutArtifact crossed disable and created a request."
                );

                host.enabled = true;
                workQueue.WorkerWaitingForRelease.Reset();
                workQueue.ReleaseWork.Reset();
                int freshCallbacks = 0;
                InteractionArtifactOperation<bool> freshVerification = null;
                IEnumerator fresh = host.PutArtifact(
                    "run_host_epoch",
                    artifact,
                    value => freshCallbacks++
                );
                W6InteractionCaptureHostTestDriver.Require(
                    fresh.MoveNext() &&
                    workQueue.WorkerWaitingForRelease.Wait(
                        TimeSpan.FromSeconds(5)
                    ) &&
                    host.TryGetArtifactVerificationForTests(
                        artifact,
                        out freshVerification
                    ),
                    "Fresh epoch did not own its verification."
                );
                workQueue.ReleaseWork.Set();
                W6InteractionCaptureHostTestDriver.Require(
                    freshVerification.Wait(TimeSpan.FromSeconds(5)) &&
                    SpinWait.SpinUntil(
                        () => freshVerification.IsReaped &&
                            host.ActiveArtifactOperationCountForTests == 0,
                        TimeSpan.FromSeconds(5)
                    ) && fresh.MoveNext() &&
                    requests.FileRequestCount == 1 && freshCallbacks == 0,
                    "A fresh epoch could not create the next request exactly once."
                );
                (fresh as IDisposable)?.Dispose();
                W6InteractionCaptureHostTestDriver.Require(
                    host.CancelActiveRequests() >= 1 &&
                    host.ActiveRequestCount == 0,
                    "Fresh request envelope was not lifecycle-owned."
                );
                host.EnableRequestLifecycleForTests();

                workQueue.WorkerWaitingForRelease.Reset();
                workQueue.ReleaseWork.Reset();
                int destroyCallbacks = 0;
                InteractionHostResult<bool> destroyResult = null;
                IEnumerator destroyStale = host.PutArtifact(
                    "run_host_epoch",
                    artifact,
                    value =>
                    {
                        destroyCallbacks++;
                        destroyResult = value;
                    }
                );
                W6InteractionCaptureHostTestDriver.Require(
                    destroyStale.MoveNext() &&
                    workQueue.WorkerWaitingForRelease.Wait(
                        TimeSpan.FromSeconds(5)
                    ) &&
                    host.TryGetArtifactVerificationForTests(
                        artifact,
                        out operation
                    ),
                    "Destroy epoch did not own its verification."
                );
                workQueue.ReleaseWork.Set();
                W6InteractionCaptureHostTestDriver.Require(
                    operation.Wait(TimeSpan.FromSeconds(5)) &&
                    SpinWait.SpinUntil(
                        () => operation.IsReaped &&
                            host.ActiveArtifactOperationCountForTests == 0,
                        TimeSpan.FromSeconds(5)
                    ),
                    "Destroy epoch fixture did not finish verification."
                );
                UnityEngine.Object.DestroyImmediate(host);
                W6InteractionCaptureHostTestDriver.Require(
                    !destroyStale.MoveNext() && destroyCallbacks == 1 &&
                    destroyResult != null && !destroyResult.Success &&
                    requests.FileRequestCount == 1,
                    "A stale PutArtifact crossed destroy or invoked callback twice."
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
                workQueue.ReleaseWork.Set();
                TryDriverCleanup(failures, "destroy Host epoch fixture", () =>
                {
                    if (owner != null)
                    {
                        UnityEngine.Object.DestroyImmediate(owner);
                    }
                });
                bool operationFinished = operation == null || TryWaitForCleanup(
                    failures,
                    "Host epoch artifact verification",
                    () => operation.Wait(TimeSpan.FromSeconds(10)),
                    root
                );
                bool ownershipReleased = VerifyArtifactOwnershipReleased(
                    failures,
                    "Host epoch artifact verification",
                    root,
                    operationFinished,
                    operationOwner,
                    operation
                );
                if (ownershipReleased)
                {
                    TryDriverCleanup(
                        failures,
                        "dispose Host epoch request factory",
                        requests.Dispose
                    );
                    TryDriverCleanup(
                        failures,
                        "dispose Host epoch work queue",
                        workQueue.Dispose
                    );
                    TryDriverCleanup(failures, "delete Host epoch fixture", () =>
                        W6InteractionCaptureHostTestDriver
                            .DeleteTemporaryRoot(root));
                }
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

        private static void CreateCompletedControllerFixture(
            string root,
            int seed,
            string participantId,
            out GameObject owner,
            out InteractionRunController controller,
            out InteractionRunStateMachine machine,
            out InteractionCaptureWriter writer)
        {
            owner = new GameObject("W6 Controller Artifact Lifecycle Test");
            owner.SetActive(false);
            InteractionHostClient host =
                owner.AddComponent<InteractionHostClient>();
            controller = owner.AddComponent<InteractionRunController>();
            owner.SetActive(true);
            machine = W6InteractionCaptureHostTestDriver
                .CreateCompletingStateMachine(seed, participantId);
            writer = W6InteractionCaptureHostTestDriver.CreateWriter(
                root,
                machine.Plan
            );
            writer.RecordEvent(
                InteractionEventNames.RunCreated,
                null,
                0d,
                W6InteractionCaptureHostTestDriver.FixedUtc,
                0
            );
            writer.BeginCapture();
            W6InteractionCaptureHostTestDriver.WriteOnePoseAndObject(writer);
            InteractionSummaryTracker summary =
                W6InteractionCaptureHostTestDriver.CreateCompletedSummary(
                    machine.Plan.RunId
                );
            W6InteractionCaptureHostTestDriver.Await(
                writer.BeginSeal(
                    InteractionCaptureTerminalKind.Completed,
                    completeness => summary.SealCompleted(
                        10d,
                        W6InteractionCaptureHostTestDriver.FixedUtc
                            .AddSeconds(10d),
                        completeness
                    )
                )
            );
            machine.MarkRunCompleted();
            controller.InstallDeterministicScenarioForTests(
                machine,
                summary,
                writer,
                initialization: null,
                host
            );
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
                InteractionHostClient host =
                    owner.AddComponent<InteractionHostClient>();
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
                    initialization: null,
                    host
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
                    InteractionHostClient host =
                        owner.AddComponent<InteractionHostClient>();
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
                        initialization,
                        host
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

        private static bool VerifyArtifactOwnershipReleased<T>(
            ICollection<Exception> failures,
            string ownerName,
            string retainedPath,
            bool operationCompleted,
            InteractionArtifactOperationRegistry registry,
            InteractionArtifactOperation<T> operation)
        {
            if (!operationCompleted)
            {
                return false;
            }
            bool released = SpinWait.SpinUntil(
                () => (registry == null || registry.ActiveCount == 0) &&
                    (operation == null || operation.IsReaped),
                TimeSpan.FromSeconds(10)
            );
            if (released)
            {
                return true;
            }
            failures.Add(new InvalidOperationException(
                ownerName + " completed without deterministic registry reap; " +
                "active data was retained at " + retainedPath + "."
            ));
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

        private static void ArmUnityLifecycleOnlyOutsidePlayMode(
            InteractionHostClient host)
        {
            if (!Application.isPlaying)
            {
                host.ArmUnityLifecycleForTests();
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

        private static IEnumerator BeginInFlightReadiness(
            IEnumerator outer,
            string description,
            out UnityWebRequestAsyncOperation operation)
        {
            bool began = outer != null && outer.MoveNext();
            IEnumerator send = began ? outer.Current as IEnumerator : null;
            W6InteractionCaptureHostTestDriver.Require(
                began && send != null,
                "Host did not create the " + description + " request."
            );
            bool sent = send.MoveNext();
            operation = sent
                ? send.Current as UnityWebRequestAsyncOperation
                : null;
            W6InteractionCaptureHostTestDriver.Require(
                sent && operation != null,
                "Host did not send the " + description + " request."
            );
            return send;
        }

        private static string CreateLocalReadinessRequestUrl()
        {
            string sourcePath = Path.Combine(
                Application.dataPath,
                "Scripts",
                "Interaction",
                "CaptureHost",
                "InteractionHostClient.cs"
            );
            W6InteractionCaptureHostTestDriver.Require(
                File.Exists(sourcePath),
                "Local readiness request fixture is unavailable."
            );
            return new Uri(sourcePath).AbsoluteUri;
        }

        private sealed class ObservableCancelableRequest :
            IInteractionCancelableRequest
        {
            public int AbortCount { get; private set; }
            public bool WasAborted => AbortCount > 0;

            public void Abort()
            {
                AbortCount++;
            }
        }

        private sealed class LocalReadinessRequestFactory :
            IInteractionHostRequestFactory
        {
            private readonly string localUrl;

            public LocalReadinessRequestFactory(string localUrl)
            {
                this.localUrl = localUrl ??
                    throw new ArgumentNullException(nameof(localUrl));
            }

            public int CreateGetCount { get; private set; }
            public UnityWebRequest LastGetRequest { get; private set; }

            public UnityWebRequest CreateGet(string url)
            {
                CreateGetCount++;
                LastGetRequest = UnityWebRequest.Get(localUrl);
                return LastGetRequest;
            }

            public UnityWebRequest CreateBody(
                string url,
                string method,
                byte[] bytes,
                string contentType)
            {
                throw new InvalidOperationException(
                    "Readiness fixture must not create a body request."
                );
            }

            public UnityWebRequest CreateFileBody(
                string url,
                string method,
                string path,
                string contentType)
            {
                throw new InvalidOperationException(
                    "Readiness fixture must not create a file request."
                );
            }
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

        private sealed class CountingHostRequestFactory :
            IInteractionHostRequestFactory,
            IDisposable
        {
            private readonly List<UnityWebRequest> requests =
                new List<UnityWebRequest>();

            public int FileRequestCount { get; private set; }

            public UnityWebRequest CreateGet(string url)
            {
                return Own(UnityWebRequest.Get(url));
            }

            public UnityWebRequest CreateBody(
                string url,
                string method,
                byte[] bytes,
                string contentType)
            {
                return Own(new UnityWebRequest(url, method)
                {
                    uploadHandler = new UploadHandlerRaw(bytes),
                    downloadHandler = new DownloadHandlerBuffer(),
                    disposeUploadHandlerOnDispose = true,
                    disposeDownloadHandlerOnDispose = true
                }.WithContentType(contentType));
            }

            public UnityWebRequest CreateFileBody(
                string url,
                string method,
                string path,
                string contentType)
            {
                FileRequestCount++;
                return Own(new UnityWebRequest(url, method)
                {
                    uploadHandler = new UploadHandlerFile(path),
                    downloadHandler = new DownloadHandlerBuffer(),
                    disposeUploadHandlerOnDispose = true,
                    disposeDownloadHandlerOnDispose = true
                }.WithContentType(contentType));
            }

            public void Dispose()
            {
                foreach (UnityWebRequest request in requests)
                {
                    request.Dispose();
                }
                requests.Clear();
            }

            private UnityWebRequest Own(UnityWebRequest request)
            {
                requests.Add(request);
                return request;
            }
        }
    }
}
#endif
