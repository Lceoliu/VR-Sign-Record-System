using System;
using System.Collections.Generic;
using System.Text;
using SignVR.Interaction.CaptureHost;
using SignVR.Interaction.Core;
using SignVR.Interaction.Diagnostics;

namespace SignVR.Interaction.Orchestration
{
    /// <summary>
    /// Thin, pure-C# sequencing layer. It owns callback lifetimes and
    /// exactly-once presentation tokens, while W1, W5, W6, and W7 retain all
    /// Run, presentation, local capture, and task-rule authority respectively.
    /// </summary>
    public sealed class InteractionStudyFlow : IDisposable
    {
        private const int MaximumLifecycleAbortRetryDelayTicks = 300;

        private IInteractionStudyRunPort run;
        private IInteractionStudyPresentationPort presentation;
        private IInteractionStudyTaskPort tasks;
        private IDisposable runSubscription;
        private IDisposable presentationSubscription;
        private IDisposable taskSubscription;
        private long bindingEpoch;
        private bool suspended;
        private bool disposed;
        private bool terminalPresentationCleanupComplete;
        private bool terminalTaskCleaned;
        private bool abortInProgress;
        private bool abortTaskDisableComplete;
        private string pendingLifecycleAbortReason = string.Empty;
        private int lifecycleAbortRetryDelayTicks;
        private int lifecycleAbortNextDelayTicks = 1;
        private bool playbackCompletionHandled;
        private long lastRequestSequence;
        private string lastRequestRunId = string.Empty;
        private InteractionStudyPresentationRequest pendingPresentation;
        private InteractionStudyPresentationRequest activePlayback;
        private ValidationResult lastHandledResult;
        private int? outcomeHandledPhaseId;
        private int progress;
        private int requiredProgress;
        private string status = "PreStart";
        private readonly InteractionTerminalWarningBuffer terminalWarnings =
            new();

        public InteractionStudyFlow(
            IInteractionStudyRunPort run,
            IInteractionStudyPresentationPort presentation,
            IInteractionStudyTaskPort tasks)
        {
            AssignPorts(run, presentation, tasks);
            Attach();
        }

        public event Action StateChanged;

        public InteractionStudyFlowSnapshot Snapshot
        {
            get
            {
                RunState state = run.State;
                PhaseExecutionSnapshot phase = run.CurrentPhase;
                bool lifecycleAbortPending =
                    !string.IsNullOrEmpty(pendingLifecycleAbortReason);
                bool commandIdle = !suspended && !disposed &&
                    pendingPresentation == null && activePlayback == null &&
                    !abortInProgress && !lifecycleAbortPending;
                bool isResultVisible = IsResultTerminal(state);
                bool canAcknowledgeResult = isResultVisible &&
                    !suspended && !disposed && !abortInProgress &&
                    !lifecycleAbortPending &&
                    terminalPresentationCleanupComplete && terminalTaskCleaned;
                bool phaseCommandAvailable = !suspended && !disposed &&
                    !abortInProgress && !lifecycleAbortPending;
                bool canStart = false;
                string preStartReadiness = null;
                if (commandIdle && state == RunState.PreStart)
                {
                    try
                    {
                        canStart = run.CanStart(out string reason);
                        preStartReadiness = canStart
                            ? "Ready to Start."
                            : reason;
                    }
                    catch (Exception exception)
                    {
                        canStart = false;
                        preStartReadiness =
                            "Study readiness check failed: " +
                            exception.Message;
                    }
                }

                string resolvedStatus = ResolveStatus();
                if (state == RunState.PreStart &&
                    (string.IsNullOrWhiteSpace(resolvedStatus) ||
                     string.Equals(
                         resolvedStatus,
                         "PreStart",
                         StringComparison.Ordinal)) &&
                    !string.IsNullOrWhiteSpace(preStartReadiness))
                {
                    resolvedStatus = preStartReadiness;
                }

                return new InteractionStudyFlowSnapshot(
                    state,
                    phase?.PhaseId,
                    progress,
                    requiredProgress,
                    canStart,
                    commandIdle && state == RunState.Running &&
                        phase != null && phase.ReplayAvailable &&
                        presentation.ReplayAvailable,
                    phaseCommandAvailable && state == RunState.Running &&
                        phase != null && phase.GiveUpAvailable,
                    abortInProgress || lifecycleAbortPending ||
                        state == RunState.Aborting,
                    isResultVisible,
                    canAcknowledgeResult,
                    ResolveTerminalOutcome(state),
                    resolvedStatus
                );
            }
        }

        public InteractionStudyFlowCommandResult TryStart()
        {
            InteractionRuntimeDiagnosticTrace.Write(
                "flow_try_start_enter",
                "state=" + run.State
            );
            if (!CanAcceptCommands(out string unavailable))
            {
                return Fail(unavailable);
            }
            if (!run.CanStart(out string reason))
            {
                return Fail(reason);
            }

            ResetRunLocalState();
            if (!run.TryStart(out string error))
            {
                InteractionRuntimeDiagnosticTrace.Write(
                    "flow_try_start_run_rejected",
                    "state=" + run.State + "; error=" + error
                );
                if (IsTerminal(run.State))
                {
                    status = "Consumed Run failed; awaiting terminal cleanup.";
                }
                return Fail(error);
            }

            RunPlan plan = run.Plan;
            if (plan == null)
            {
                return AbortAfterIntegrationFailure(
                    "W6 accepted Start without exposing W1's frozen RunPlan."
                );
            }

            try
            {
                tasks.Configure(plan);
                tasks.Enable();
                InteractionRuntimeDiagnosticTrace.Write(
                    "flow_tasks_configured",
                    "run_id=" + plan.RunId + "; phases=" + plan.Phases.Count
                );
            }
            catch (Exception exception)
            {
                return AbortAfterIntegrationFailure(
                    "W7 configuration failed: " + exception.Message
                );
            }

            status = "Run consumed; waiting for local capture and presentation.";
            NotifyChanged();
            return InteractionStudyFlowCommandResult.Success();
        }

        public InteractionStudyFlowCommandResult TryReplay()
        {
            if (!CanAcceptCommands(out string unavailable))
            {
                return Fail(unavailable);
            }
            PhaseExecutionSnapshot phase = run.CurrentPhase;
            if (run.State != RunState.Running || phase == null ||
                !phase.ReplayAvailable || !presentation.ReplayAvailable ||
                pendingPresentation != null ||
                activePlayback != null)
            {
                return Fail("Instruction playback is not ready or is running.");
            }

            if (!run.TryRequestReplay(out string error))
            {
                return Fail(error);
            }

            status = phase.FirstPlaybackCompleted
                ? "Replay requested from the beginning."
                : "First instruction playback requested by the participant.";
            NotifyChanged();
            return InteractionStudyFlowCommandResult.Success();
        }

        public InteractionStudyFlowCommandResult TryGiveUp()
        {
            if (!CanAcceptCommands(out string unavailable))
            {
                return Fail(unavailable);
            }
            PhaseExecutionSnapshot phase = run.CurrentPhase;
            if (run.State != RunState.Running || phase == null ||
                !phase.GiveUpAvailable)
            {
                return Fail("Give Up is unavailable for the current phase.");
            }

            ValidationResult result;
            try
            {
                result = tasks.GiveUp(phase);
                if (result != null &&
                    !ReferenceEquals(result, lastHandledResult))
                {
                    HandleTaskResult(bindingEpoch, result);
                }
            }
            catch (Exception exception)
            {
                return AbortAfterIntegrationFailure(
                    "W7 Give Up failed: " + exception.Message
                );
            }

            if (result == null || !result.PhaseGivenUp)
            {
                return Fail(
                    result == null
                        ? "W7 produced no Give Up result."
                        : "W7 rejected Give Up with " + result.Error + "."
                );
            }
            return InteractionStudyFlowCommandResult.Success();
        }

        public InteractionStudyFlowCommandResult TryAbort(string reason)
        {
            if (disposed)
            {
                return Fail("The Study Flow is disposed.");
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Fail("Abort reason is required.");
            }
            return BeginAbort(reason.Trim());
        }

        public InteractionStudyFlowCommandResult TryAcknowledgeResult()
        {
            if (disposed)
            {
                return Fail("The Study Flow is disposed.");
            }
            if (suspended)
            {
                return Fail("The Study Flow is suspended.");
            }

            RunState state = run.State;
            if (!IsResultTerminal(state))
            {
                return Fail(
                    "Only a completed or safely aborted Run has a result to " +
                    "acknowledge."
                );
            }
            if (abortInProgress ||
                !string.IsNullOrEmpty(pendingLifecycleAbortReason) ||
                !terminalPresentationCleanupComplete || !terminalTaskCleaned)
            {
                return Fail(
                    "The result cannot be acknowledged until terminal " +
                    "cleanup and local sealing are complete."
                );
            }

            bool resetAccepted;
            try
            {
                resetAccepted = run.TryResetToPreStart();
            }
            catch (Exception exception)
            {
                RecordTerminalCleanupWarning(
                    "W6 terminal reset failed: " + exception.Message
                );
                return Fail(
                    "The result could not return to PreStart: " +
                    exception.Message
                );
            }
            if (!resetAccepted)
            {
                return Fail(
                    "The result cannot return to PreStart until W6 local " +
                    "seal cleanup is complete."
                );
            }

            abortInProgress = false;
            ClearPendingLifecycleAbort();
            ResetTerminalCleanupAttempts();
            lastRequestSequence = 0L;
            lastRequestRunId = string.Empty;
            status = terminalWarnings.IsEmpty
                ? "PreStart"
                : "PreStart after terminal cleanup warning: " +
                    terminalWarnings.Value;
            terminalWarnings.Clear();
            NotifyChanged();
            return InteractionStudyFlowCommandResult.Success();
        }

        public void Tick()
        {
            if (disposed || suspended)
            {
                return;
            }

            bool lifecycleAbortWasPending =
                !string.IsNullOrEmpty(pendingLifecycleAbortReason);
            bool retryPublishedChange = false;
            if (lifecycleAbortWasPending &&
                !RetryPendingLifecycleAbort(
                    immediate: false,
                    out retryPublishedChange))
            {
                return;
            }

            RunState state = run.State;
            if (lifecycleAbortWasPending && !retryPublishedChange &&
                (state == RunState.PreStart || state == RunState.Aborting))
            {
                NotifyChanged();
            }
            if (state == RunState.Aborting)
            {
                abortInProgress = true;
                return;
            }
            if (!IsTerminal(state))
            {
                return;
            }

            if (!TryCompleteTerminalPresentationCleanup(out _))
            {
                status = "Terminal presentation cleanup will retry. " +
                    terminalWarnings.Value;
                NotifyChanged();
                return;
            }

            if (!terminalTaskCleaned)
            {
                try
                {
                    if (state == RunState.Completed)
                    {
                        tasks.Reset();
                    }
                    else
                    {
                        tasks.Abort();
                    }
                }
                catch (Exception exception)
                {
                    RecordTerminalCleanupWarning(
                        "W7 terminal cleanup failed: " + exception.Message
                    );
                }
                finally
                {
                    // A broken adapter must not strand W6 in a terminal Run or
                    // make Update throw forever. The next Run reconfigures W7
                    // from W1's fresh frozen plan before accepting input.
                    terminalTaskCleaned = true;
                    progress = 0;
                    requiredProgress = 0;
                    lastHandledResult = null;
                    outcomeHandledPhaseId = null;
                }
            }

            if (IsResultTerminal(state))
            {
                bool resultWasReady = !abortInProgress &&
                    string.IsNullOrEmpty(pendingLifecycleAbortReason) &&
                    terminalPresentationCleanupComplete && terminalTaskCleaned;
                abortInProgress = false;
                ClearPendingLifecycleAbort();
                string resultStatus = state == RunState.Completed
                    ? "Completed result is ready for acknowledgement."
                    : "Safely aborted result is ready for acknowledgement.";
                if (!terminalWarnings.IsEmpty)
                {
                    resultStatus += " Terminal cleanup warning: " +
                        terminalWarnings.Value;
                }
                bool statusChanged = !string.Equals(
                    status,
                    resultStatus,
                    StringComparison.Ordinal
                );
                status = resultStatus;
                if (!resultWasReady || statusChanged)
                {
                    NotifyChanged();
                }
                return;
            }

            bool resetAccepted;
            try
            {
                resetAccepted = run.TryResetToPreStart();
            }
            catch (Exception exception)
            {
                RecordTerminalCleanupWarning(
                    "W6 terminal reset failed: " + exception.Message
                );
                status = "Terminal Run cleanup will retry. " +
                    terminalWarnings.Value;
                NotifyChanged();
                return;
            }
            if (!resetAccepted)
            {
                status = "Terminal Run is waiting for W6 local seal cleanup." +
                    (terminalWarnings.IsEmpty
                        ? string.Empty
                        : " " + terminalWarnings.Value);
                return;
            }

            abortInProgress = false;
            ClearPendingLifecycleAbort();
            ResetTerminalCleanupAttempts();
            lastRequestSequence = 0L;
            lastRequestRunId = string.Empty;
            status = terminalWarnings.IsEmpty
                ? "PreStart"
                : "PreStart after terminal cleanup warning: " +
                    terminalWarnings.Value;
            terminalWarnings.Clear();
            NotifyChanged();
        }

        public void Suspend(string reason)
        {
            if (disposed || suspended)
            {
                return;
            }
            Exception failure = null;
            bool disableTasks = false;
            string lifecycleReason = NormalizeLifecycleAbortReason(reason);
            try
            {
                RunState state = run.State;
                if (IsRunOwned(state))
                {
                    disableTasks = true;
                    InteractionStudyFlowCommandResult abort = BeginAbort(
                        lifecycleReason,
                        disableTasks: false
                    );
                    if (!abort.Succeeded && IsRunOwned(run.State))
                    {
                        ArmPendingLifecycleAbort(lifecycleReason);
                    }
                }
                else if (state == RunState.Aborting)
                {
                    disableTasks = true;
                    ClearPendingLifecycleAbort();
                    TryCompleteTerminalPresentationCleanup(out _);
                    abortInProgress = true;
                }
                else
                {
                    ClearPendingLifecycleAbort();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (disableTasks && !abortTaskDisableComplete)
                {
                    try
                    {
                        tasks.Disable();
                        abortTaskDisableComplete = true;
                    }
                    catch (Exception exception)
                    {
                        if (IsRunOwned(run.State) ||
                            run.State == RunState.Aborting)
                        {
                            ArmPendingLifecycleAbort(lifecycleReason);
                        }
                        failure ??= exception;
                    }
                }
            }
            suspended = true;
            try
            {
                Detach();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            NotifyChanged();
            if (failure != null)
            {
                throw failure;
            }
        }

        public void Resume()
        {
            ThrowIfDisposed();
            if (!suspended)
            {
                return;
            }
            suspended = false;
            Attach();
            if (!string.IsNullOrEmpty(pendingLifecycleAbortReason))
            {
                RetryPendingLifecycleAbort(
                    immediate: true,
                    out bool retryPublishedChange
                );
                if (!retryPublishedChange)
                {
                    NotifyChanged();
                }
                return;
            }
            else if (IsTerminal(run.State))
            {
                status = "Terminal Run detected after resume.";
            }
            NotifyChanged();
        }

        public void Reconfigure(
            IInteractionStudyRunPort newRun,
            IInteractionStudyPresentationPort newPresentation,
            IInteractionStudyTaskPort newTasks)
        {
            ThrowIfDisposed();
            if (run.State != RunState.PreStart)
            {
                throw new InvalidOperationException(
                    "Publisher replacement is allowed only in PreStart; " +
                    "an active or terminal Run must retain its W6 owner."
                );
            }
            if (newRun == null || newPresentation == null || newTasks == null)
            {
                throw new ArgumentNullException(
                    newRun == null
                        ? nameof(newRun)
                        : newPresentation == null
                            ? nameof(newPresentation)
                            : nameof(newTasks)
                );
            }
            if (newRun.State != RunState.PreStart)
            {
                throw new InvalidOperationException(
                    "A replacement W6 publisher must itself be in PreStart."
                );
            }

            bool wasSuspended = suspended;
            IInteractionStudyRunPort oldRun = run;
            IInteractionStudyPresentationPort oldPresentation = presentation;
            IInteractionStudyTaskPort oldTasks = tasks;
            Detach();
            try
            {
                AssignPorts(newRun, newPresentation, newTasks);
                if (!wasSuspended)
                {
                    Attach();
                }
            }
            catch (Exception replacementFailure)
            {
                Exception rollbackFailure = null;
                try
                {
                    Detach();
                    AssignPorts(oldRun, oldPresentation, oldTasks);
                    if (!wasSuspended)
                    {
                        Attach();
                    }
                }
                catch (Exception exception)
                {
                    rollbackFailure = exception;
                }
                if (rollbackFailure != null)
                {
                    throw new AggregateException(
                        "Study Flow publisher replacement and rollback both " +
                        "failed.",
                        replacementFailure,
                        rollbackFailure
                    );
                }
                throw;
            }
            ResetRunLocalState();
            status = "PreStart";
            NotifyChanged();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            Exception failure = null;
            try
            {
                if (IsRunOwned(run.State))
                {
                    InteractionStudyFlowCommandResult abort = BeginAbort(
                        "study_flow_disposed"
                    );
                    if (!abort.Succeeded && IsRunOwned(run.State))
                    {
                        failure = new InvalidOperationException(
                            "Study Flow disposal could not transfer its owned " +
                            "Run to W6 abort: " + abort.Error
                        );
                    }
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            try
            {
                Detach();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            finally
            {
                disposed = true;
            }
            if (failure != null)
            {
                throw failure;
            }
        }

        private void AssignPorts(
            IInteractionStudyRunPort newRun,
            IInteractionStudyPresentationPort newPresentation,
            IInteractionStudyTaskPort newTasks)
        {
            run = newRun ?? throw new ArgumentNullException(nameof(newRun));
            presentation = newPresentation ??
                throw new ArgumentNullException(nameof(newPresentation));
            tasks = newTasks ??
                throw new ArgumentNullException(nameof(newTasks));
        }

        private void Attach()
        {
            bindingEpoch = checked(bindingEpoch + 1L);
            long epoch = bindingEpoch;
            IDisposable newRunSubscription = null;
            IDisposable newPresentationSubscription = null;
            IDisposable newTaskSubscription = null;
            try
            {
                newRunSubscription = run.Subscribe(
                    request => HandlePresentationRequest(epoch, request)
                );
                newPresentationSubscription = presentation.Subscribe(
                    kind => HandleFirstFrame(epoch, kind),
                    kind => HandlePlaybackCompleted(epoch, kind),
                    value => HandlePresentationObservation(epoch, value),
                    error => HandlePresentationFault(epoch, error)
                );
                newTaskSubscription = tasks.Subscribe(
                    result => HandleTaskResult(epoch, result)
                );
                runSubscription = newRunSubscription;
                presentationSubscription = newPresentationSubscription;
                taskSubscription = newTaskSubscription;
            }
            catch
            {
                // A partially attached replacement must never leak callbacks.
                newTaskSubscription?.Dispose();
                newPresentationSubscription?.Dispose();
                newRunSubscription?.Dispose();
                bindingEpoch = checked(bindingEpoch + 1L);
                throw;
            }
        }

        private void Detach()
        {
            bindingEpoch = checked(bindingEpoch + 1L);
            runSubscription?.Dispose();
            presentationSubscription?.Dispose();
            taskSubscription?.Dispose();
            runSubscription = null;
            presentationSubscription = null;
            taskSubscription = null;
        }

        private void HandlePresentationRequest(
            long epoch,
            InteractionStudyPresentationRequest request)
        {
            InteractionRuntimeDiagnosticTrace.Write(
                "flow_presentation_request_enter",
                "epoch=" + epoch + "; phase=" + request?.PhaseId +
                "; kind=" + request?.PlaybackKind + "; state=" + run.State
            );
            if (!IsCurrent(epoch) || request == null)
            {
                return;
            }
            RunPlan plan = run.Plan;
            if (plan == null || !string.Equals(
                    plan.RunId,
                    request.RunId,
                    StringComparison.Ordinal))
            {
                // A late callback from a consumed/reset Run is stale evidence,
                // never a reason to start presentation under another plan.
                return;
            }
            if ((pendingPresentation != null &&
                    pendingPresentation.Matches(request)) ||
                (activePlayback != null && activePlayback.Matches(request)) ||
                (string.Equals(
                        lastRequestRunId,
                        request.RunId,
                        StringComparison.Ordinal) &&
                    request.RequestSequence <= lastRequestSequence))
            {
                return;
            }
            if (pendingPresentation != null || activePlayback != null)
            {
                BeginAbort("overlapping_presentation_request");
                return;
            }
            if (request.PhaseId < 1 || request.PhaseId > plan.Phases.Count)
            {
                BeginAbort("presentation_phase_out_of_plan");
                return;
            }

            pendingPresentation = request;
            playbackCompletionHandled = false;
            lastRequestRunId = request.RunId;
            lastRequestSequence = request.RequestSequence;
            bool began;
            string error;
            bool preparingPhase = request.PlaybackKind ==
                    InteractionPresentationPlaybackKind.First &&
                (!presentation.PhaseActive || run.CurrentPhase == null ||
                 run.CurrentPhase.PhaseId != request.PhaseId);
            try
            {
                if (preparingPhase)
                {
                    RunPhasePlan phasePlan = plan.Phases[request.PhaseId - 1];
                    began = presentation.TryBeginPhase(
                        phasePlan,
                        plan.AssistanceCondition,
                        out error
                    );
                }
                else
                {
                    began = presentation.TryBeginReplay(out error);
                }
            }
            catch (Exception exception)
            {
                began = false;
                error = exception.Message;
                InteractionRuntimeDiagnosticTrace.Write(
                    "flow_presentation_begin_exception",
                    "phase=" + request.PhaseId + "; error=" + error,
                    exception
                );
            }

            if (!began)
            {
                InteractionRuntimeDiagnosticTrace.Write(
                    "flow_presentation_begin_failed",
                    "phase=" + request.PhaseId + "; error=" + error
                );
                status = "W5 presentation failed: " + error;
                BeginAbort("presentation_start_failed");
                return;
            }
            if (preparingPhase)
            {
                InteractionRuntimeDiagnosticTrace.Write(
                    "flow_phase_prepared",
                    "phase=" + request.PhaseId
                );
                AcknowledgePreparedPhase(request);
                return;
            }
            status = "Waiting for W5's actual first presented frame.";
            NotifyChanged();
        }

        private void AcknowledgePreparedPhase(
            InteractionStudyPresentationRequest request)
        {
            if (!run.TryAcknowledgePresentationStarted(
                    request,
                    out string error))
            {
                InteractionRuntimeDiagnosticTrace.Write(
                    "flow_phase_ack_failed",
                    "phase=" + request.PhaseId + "; error=" + error
                );
                status = "W6 rejected prepared phase: " + error;
                BeginAbort("presentation_ack_failed");
                return;
            }

            pendingPresentation = null;
            try
            {
                PhaseExecutionSnapshot snapshot = run.CurrentPhase ??
                    throw new InvalidOperationException(
                        "W1 exposed no phase after phase preparation."
                    );
                tasks.Synchronize(snapshot);
                tasks.Enable();
                InteractionRuntimeDiagnosticTrace.Write(
                    "flow_phase_tasks_synchronized",
                    "phase=" + snapshot.PhaseId + "; state=" + run.State
                );
                progress = 0;
                requiredProgress = 0;
                lastHandledResult = null;
                outcomeHandledPhaseId = null;
            }
            catch (Exception exception)
            {
                InteractionRuntimeDiagnosticTrace.Write(
                    "flow_phase_tasks_sync_failed",
                    "phase=" + request.PhaseId + "; error=" + exception.Message,
                    exception
                );
                status = "W7 synchronization failed: " + exception.Message;
                BeginAbort("task_synchronization_failed");
                return;
            }

            status = "Phase ready; playback waits for the participant.";
            NotifyChanged();
        }

        private void HandleFirstFrame(
            long epoch,
            InteractionPresentationPlaybackKind kind)
        {
            if (!IsCurrent(epoch) || pendingPresentation == null ||
                pendingPresentation.PlaybackKind != kind)
            {
                return;
            }

            InteractionStudyPresentationRequest request = pendingPresentation;
            if (!run.TryAcknowledgePresentationStarted(request, out string error))
            {
                status = "W6 rejected first-frame confirmation: " + error;
                BeginAbort("presentation_ack_failed");
                return;
            }

            pendingPresentation = null;
            activePlayback = request;
            playbackCompletionHandled = false;
            try
            {
                PhaseExecutionSnapshot snapshot = run.CurrentPhase ??
                    throw new InvalidOperationException(
                        "W1 exposed no phase after first-frame confirmation."
                    );
                tasks.Synchronize(snapshot);
                tasks.Enable();
                if (kind == InteractionPresentationPlaybackKind.First)
                {
                    progress = 0;
                    requiredProgress = 0;
                    lastHandledResult = null;
                    outcomeHandledPhaseId = null;
                }
            }
            catch (Exception exception)
            {
                status = "W7 synchronization failed: " + exception.Message;
                BeginAbort("task_synchronization_failed");
                return;
            }

            status = kind == InteractionPresentationPlaybackKind.First
                ? "First playback presented; W7 interactions are synchronized."
                : "Replay presented from W6's one-time token.";
            NotifyChanged();
        }

        private void HandlePlaybackCompleted(
            long epoch,
            InteractionPresentationPlaybackKind kind)
        {
            if (!IsCurrent(epoch) || activePlayback == null ||
                activePlayback.PlaybackKind != kind ||
                playbackCompletionHandled)
            {
                return;
            }
            playbackCompletionHandled = true;
            if (!run.TryNotifyPlaybackCompleted(kind, out string error))
            {
                status = "W6 rejected playback completion: " + error;
                BeginAbort("presentation_completion_failed");
                return;
            }

            activePlayback = null;
            try
            {
                if (run.CurrentPhase != null)
                {
                    tasks.Synchronize(run.CurrentPhase);
                    tasks.Enable();
                }
            }
            catch (Exception exception)
            {
                status = "W7 completion synchronization failed: " +
                    exception.Message;
                BeginAbort("task_synchronization_failed");
                return;
            }

            status = kind == InteractionPresentationPlaybackKind.First
                ? "First playback completed; replay remains available."
                : "Replay completed; it can be started again.";
            NotifyChanged();
        }

        private void HandleTaskResult(long epoch, ValidationResult result)
        {
            if (!IsCurrent(epoch) || result == null ||
                ReferenceEquals(lastHandledResult, result) ||
                outcomeHandledPhaseId == result.PhaseId)
            {
                return;
            }
            PhaseExecutionSnapshot current = run.CurrentPhase;
            if (run.State != RunState.Running || current == null ||
                current.PhaseId != result.PhaseId)
            {
                return;
            }

            lastHandledResult = result;
            progress = result.Progress;
            requiredProgress = result.RequiredProgress;
            if (!run.TryRecordValidationResult(result, out string error))
            {
                status = "W6 validation capture failed: " + error;
                BeginAbort("validation_capture_failed");
                return;
            }

            try
            {
                // Recording a validation error mutates W1's lifecycle
                // snapshot. Refresh W7's view of that authority, while the UI
                // continues to show the W7 ValidationResult copied above.
                PhaseExecutionSnapshot refreshed = run.CurrentPhase ??
                    throw new InvalidOperationException(
                        "W1 exposed no phase after validation capture."
                    );
                if (refreshed.PhaseId != result.PhaseId)
                {
                    throw new InvalidOperationException(
                        "W1 phase changed while recording a W7 result."
                    );
                }
                tasks.Synchronize(refreshed);
            }
            catch (Exception exception)
            {
                status = "W7 validation resynchronization failed: " +
                    exception.Message;
                BeginAbort("task_synchronization_failed");
                return;
            }

            if (!result.PhaseCompleted && !result.PhaseGivenUp)
            {
                status = result.InteractionError
                    ? "W7 rejected input; progress remains W7-authoritative."
                    : "W7 accepted input.";
                NotifyChanged();
                return;
            }

            outcomeHandledPhaseId = result.PhaseId;
            try
            {
                // This ordering is intentional: stop assistance exposure and
                // playback before W6 checkpoints/seals the authoritative phase.
                presentation.EndPhase();
            }
            catch (Exception exception)
            {
                status = "W5 EndPhase failed: " + exception.Message;
                BeginAbort("presentation_end_failed");
                return;
            }
            ClearPresentationTokens();
            if (!run.TryFinishPhase(result.PhaseGivenUp, out error))
            {
                status = "W6 phase finish failed: " + error;
                BeginAbort("phase_finish_failed");
                return;
            }
            tasks.Disable();
            status = result.PhaseGivenUp
                ? "Phase marked Stuck; waiting for checkpoint and next request."
                : "Phase completed; waiting for checkpoint and next request.";
            NotifyChanged();
        }

        private void HandlePresentationObservation(
            long epoch,
            InteractionStudyPresentationObservation observation)
        {
            if (!IsCurrent(epoch) || observation == null ||
                run.State != RunState.Running || run.CurrentPhase == null)
            {
                return;
            }
            if (!run.TryRecordPresentationObservation(
                    observation,
                    out string error))
            {
                status = "W6 presentation capture failed: " + error;
                BeginAbort("presentation_capture_failed");
            }
        }

        private void HandlePresentationFault(long epoch, string error)
        {
            if (!IsCurrent(epoch))
            {
                return;
            }
            status = "W5 presentation fault: " +
                (string.IsNullOrWhiteSpace(error) ? "unknown" : error.Trim());
            InteractionRuntimeDiagnosticTrace.Write(
                "flow_presentation_fault",
                "epoch=" + epoch + "; state=" + run.State +
                "; error=" + error
            );
            BeginAbort("presentation_fault");
        }

        private InteractionStudyFlowCommandResult BeginAbort(
            string reason,
            bool disableTasks = true)
        {
            InteractionRuntimeDiagnosticTrace.Write(
                "flow_abort_begin",
                "reason=" + reason + "; state=" + run.State +
                "; status=" + status
            );
            RunState state = run.State;
            if (state == RunState.Aborting)
            {
                abortInProgress = true;
                ClearPendingLifecycleAbort();
                if (disableTasks && !TryDisableTasksForAbort(
                        out string existingAbortDisableError))
                {
                    ArmPendingLifecycleAbort(reason);
                    return Fail(
                        "W7 Disable failed while Run abort is in progress: " +
                        existingAbortDisableError
                    );
                }
                return Fail("Run abort is already in progress.");
            }
            if (!IsRunOwned(state))
            {
                return Fail("Run cannot abort from " + state + ".");
            }

            TryCompleteTerminalPresentationCleanup(
                out string presentationError
            );

            bool accepted = false;
            string abortError = null;
            string taskDisableError = null;
            try
            {
                accepted = run.TryAbort(reason, out abortError);
            }
            catch (Exception exception)
            {
                abortError = exception.Message;
            }
            finally
            {
                if (disableTasks && !TryDisableTasksForAbort(
                        out taskDisableError))
                {
                    RecordTerminalCleanupWarning(
                        "W7 abort Disable failed: " + taskDisableError
                    );
                }
            }

            abortInProgress = accepted || run.State == RunState.Aborting;
            if (abortInProgress)
            {
                if (abortTaskDisableComplete)
                {
                    ClearPendingLifecycleAbort();
                }
                else
                {
                    ArmPendingLifecycleAbort(reason);
                }
            }
            if (!accepted || !string.IsNullOrWhiteSpace(taskDisableError))
            {
                var failures = new List<string>();
                if (!string.IsNullOrWhiteSpace(presentationError))
                {
                    failures.Add("W5 EndPhase failed: " + presentationError);
                }
                if (!accepted)
                {
                    failures.Add(
                        "W6 abort failed: " +
                        (string.IsNullOrWhiteSpace(abortError)
                            ? "unknown failure"
                            : abortError)
                    );
                }
                if (!string.IsNullOrWhiteSpace(taskDisableError))
                {
                    failures.Add(
                        "W7 Disable failed: " + taskDisableError
                    );
                }
                return Fail(string.Join("; ", failures));
            }

            status = string.IsNullOrWhiteSpace(presentationError)
                ? "Abort accepted; waiting for W6's true terminal state."
                : "Abort accepted after W5 EndPhase error: " +
                    presentationError;
            NotifyChanged();
            return InteractionStudyFlowCommandResult.Success();
        }

        private InteractionStudyFlowCommandResult AbortAfterIntegrationFailure(
            string error)
        {
            status = error;
            InteractionStudyFlowCommandResult abort = BeginAbort(
                "study_flow_integration_failure"
            );
            return InteractionStudyFlowCommandResult.Failure(
                abort.Succeeded
                    ? error
                    : error + " Abort also failed: " + abort.Error
            );
        }

        private bool CanAcceptCommands(out string reason)
        {
            if (disposed)
            {
                reason = "The Study Flow is disposed.";
                return false;
            }
            if (suspended)
            {
                reason = "The Study Flow is suspended.";
                return false;
            }
            if (abortInProgress || run.State == RunState.Aborting)
            {
                reason = "Run abort is in progress.";
                return false;
            }
            if (!string.IsNullOrEmpty(pendingLifecycleAbortReason))
            {
                reason = "Run lifecycle abort retry is pending.";
                return false;
            }
            reason = null;
            return true;
        }

        private void ResetRunLocalState()
        {
            ClearPresentationTokens();
            ResetTerminalCleanupAttempts();
            abortInProgress = false;
            abortTaskDisableComplete = false;
            ClearPendingLifecycleAbort();
            lastRequestSequence = 0L;
            lastRequestRunId = string.Empty;
            lastHandledResult = null;
            outcomeHandledPhaseId = null;
            progress = 0;
            requiredProgress = 0;
            terminalWarnings.Clear();
        }

        private bool TryCompleteTerminalPresentationCleanup(out string error)
        {
            if (terminalPresentationCleanupComplete)
            {
                error = null;
                return true;
            }

            try
            {
                // PhaseActive can already be false after a partially completed
                // EndPhase. Only a complete, non-throwing call proves that all
                // W5 output owners have converged.
                presentation.EndPhase();
                terminalPresentationCleanupComplete = true;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                RecordTerminalCleanupWarning(
                    "W5 terminal EndPhase failed: " + exception.Message
                );
                return false;
            }
            finally
            {
                ClearPresentationTokens();
            }
        }

        private void RecordTerminalCleanupWarning(string warning)
        {
            terminalWarnings.Add(warning);
        }

        private void ResetTerminalCleanupAttempts()
        {
            terminalPresentationCleanupComplete = false;
            terminalTaskCleaned = false;
        }

        private void ClearPresentationTokens()
        {
            pendingPresentation = null;
            activePlayback = null;
            playbackCompletionHandled = false;
        }

        private bool IsCurrent(long epoch)
        {
            return !disposed && !suspended &&
                string.IsNullOrEmpty(pendingLifecycleAbortReason) &&
                epoch == bindingEpoch;
        }

        private bool RetryPendingLifecycleAbort(
            bool immediate,
            out bool notificationPublished)
        {
            notificationPublished = false;
            if (string.IsNullOrEmpty(pendingLifecycleAbortReason))
            {
                return true;
            }

            if (!immediate && lifecycleAbortRetryDelayTicks > 0)
            {
                lifecycleAbortRetryDelayTicks--;
                return false;
            }

            RunState state = run.State;
            if (state == RunState.Aborting)
            {
                abortInProgress = true;
                if (!TryDisableTasksForAbort(out string disableError))
                {
                    status = "W7 lifecycle Disable will retry: " +
                        disableError;
                    ScheduleLifecycleAbortRetry();
                    NotifyChanged();
                    notificationPublished = true;
                    return false;
                }
                ClearPendingLifecycleAbort();
                return true;
            }
            if (IsTerminal(state))
            {
                // W7's terminal Abort/Reset stage now owns convergence. A
                // permanently broken Disable must not prevent W6 reset.
                abortInProgress = false;
                ClearPendingLifecycleAbort();
                return true;
            }
            if (state == RunState.PreStart)
            {
                abortInProgress = false;
                ClearPendingLifecycleAbort();
                return true;
            }
            if (!IsRunOwned(state))
            {
                status = "Lifecycle abort retry is pending while W6 is " +
                    state + ".";
                NotifyChanged();
                notificationPublished = true;
                return false;
            }

            string reason = pendingLifecycleAbortReason;
            InteractionStudyFlowCommandResult retry = BeginAbort(reason);
            notificationPublished = true;
            state = run.State;
            if ((state == RunState.Aborting || IsTerminal(state)) &&
                !abortTaskDisableComplete)
            {
                ScheduleLifecycleAbortRetry();
                return false;
            }
            if (retry.Succeeded || state == RunState.Aborting ||
                state == RunState.PreStart || IsTerminal(state))
            {
                ClearPendingLifecycleAbort();
                return true;
            }
            ScheduleLifecycleAbortRetry();
            return false;
        }

        private bool TryDisableTasksForAbort(out string error)
        {
            if (abortTaskDisableComplete)
            {
                error = null;
                return true;
            }

            try
            {
                tasks.Disable();
                abortTaskDisableComplete = true;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private void ArmPendingLifecycleAbort(string reason)
        {
            pendingLifecycleAbortReason = reason;
            lifecycleAbortRetryDelayTicks = 0;
            lifecycleAbortNextDelayTicks = 1;
        }

        private void ScheduleLifecycleAbortRetry()
        {
            lifecycleAbortRetryDelayTicks =
                lifecycleAbortNextDelayTicks;
            lifecycleAbortNextDelayTicks = Math.Min(
                MaximumLifecycleAbortRetryDelayTicks,
                checked(lifecycleAbortNextDelayTicks * 2)
            );
        }

        private void ClearPendingLifecycleAbort()
        {
            pendingLifecycleAbortReason = string.Empty;
            lifecycleAbortRetryDelayTicks = 0;
            lifecycleAbortNextDelayTicks = 1;
        }

        private static string NormalizeLifecycleAbortReason(string reason)
        {
            return string.IsNullOrWhiteSpace(reason)
                ? "study_flow_suspended"
                : reason.Trim();
        }

        private string ResolveStatus()
        {
            if (!string.IsNullOrWhiteSpace(run.LastError) &&
                (string.IsNullOrWhiteSpace(status) ||
                 run.State == RunState.Faulted))
            {
                return run.LastError;
            }
            return status;
        }

        private InteractionStudyFlowCommandResult Fail(string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                status = error.Trim();
            }
            NotifyChanged();
            return InteractionStudyFlowCommandResult.Failure(error);
        }

        private void NotifyChanged()
        {
            StateChanged?.Invoke();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(InteractionStudyFlow));
            }
        }

        private static bool IsRunOwned(RunState state)
        {
            return state == RunState.Preparing ||
                state == RunState.Scheduled ||
                state == RunState.Running ||
                state == RunState.Completing;
        }

        private static bool IsTerminal(RunState state)
        {
            return state == RunState.Completed ||
                state == RunState.Aborted ||
                state == RunState.Faulted;
        }

        private static bool IsResultTerminal(RunState state)
        {
            return state == RunState.Completed || state == RunState.Aborted;
        }

        private static RunResult? ResolveTerminalOutcome(RunState state)
        {
            switch (state)
            {
                case RunState.Completed:
                    return RunResult.Completed;
                case RunState.Aborted:
                    return RunResult.Aborted;
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Bounded terminal diagnostics. Exact retained items are de-duplicated,
    /// while the newest failure is never lost to an older full buffer.
    /// </summary>
    internal sealed class InteractionTerminalWarningBuffer
    {
        internal const int MaximumLength = 1024;
        internal const int MaximumItemLength = 256;
        internal const string TruncationMarker =
            "[older warnings truncated]";
        private const string Separator = " | ";

        private readonly List<Entry> entries = new();
        private readonly HashSet<string> canonicalItems = new(
            StringComparer.Ordinal
        );
        private bool truncated;

        public bool IsEmpty => entries.Count == 0;

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal int RetainedCanonicalCount => entries.Count;

        internal int RetainedCanonicalCharacterCount
        {
            get
            {
                int total = 0;
                for (int index = 0; index < entries.Count; index++)
                {
                    total = checked(total + entries[index].Canonical.Length);
                }
                return total;
            }
        }
#endif

        public string Value
        {
            get
            {
                var values = new List<string>(entries.Count + 1);
                if (truncated)
                {
                    values.Add(TruncationMarker);
                }
                for (int index = 0; index < entries.Count; index++)
                {
                    values.Add(entries[index].Canonical);
                }
                return string.Join(Separator, values);
            }
        }

        public bool Add(string warning)
        {
            string canonical = CreateBoundedCanonical(warning);
            if (canonical == null)
            {
                return false;
            }

            if (!canonicalItems.Add(canonical))
            {
                return false;
            }
            entries.Add(new Entry(canonical));

            while (Value.Length > MaximumLength && entries.Count > 1)
            {
                Entry removed = entries[0];
                entries.RemoveAt(0);
                canonicalItems.Remove(removed.Canonical);
                truncated = true;
            }
            return true;
        }

        public void Clear()
        {
            entries.Clear();
            canonicalItems.Clear();
            truncated = false;
        }

        private static string CreateBoundedCanonical(string warning)
        {
            if (warning == null)
            {
                return null;
            }

            int start = 0;
            int end = warning.Length;
            while (start < end && char.IsWhiteSpace(warning[start]))
            {
                start++;
            }
            while (end > start && char.IsWhiteSpace(warning[end - 1]))
            {
                end--;
            }
            if (start == end)
            {
                return null;
            }

            int trimmedLength = end - start;
            if (trimmedLength <= MaximumItemLength)
            {
                if (start == 0 && trimmedLength == warning.Length)
                {
                    // Reusing an already bounded input avoids even a bounded
                    // duplicate allocation.
                    return warning;
                }
                return CopyRange(warning, start, trimmedLength, false);
            }

            int copyLength = MaximumItemLength - 1;
            int cut = start + copyLength;
            if (char.IsHighSurrogate(warning[cut - 1]) &&
                cut < end && char.IsLowSurrogate(warning[cut]))
            {
                copyLength--;
            }
            return CopyRange(warning, start, copyLength, true);
        }

        private static string CopyRange(
            string source,
            int start,
            int length,
            bool appendEllipsis)
        {
            var builder = new StringBuilder(
                length + (appendEllipsis ? 1 : 0)
            );
            builder.Append(source, start, length);
            if (appendEllipsis)
            {
                builder.Append('…');
            }
            return builder.ToString();
        }

        private sealed class Entry
        {
            public Entry(string canonical)
            {
                Canonical = canonical;
            }

            public string Canonical { get; }
        }
    }
}
