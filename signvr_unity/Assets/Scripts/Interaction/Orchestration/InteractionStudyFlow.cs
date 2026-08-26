using System;
using SignVR.Interaction.CaptureHost;
using SignVR.Interaction.Core;

namespace SignVR.Interaction.Orchestration
{
    /// <summary>
    /// Thin, pure-C# sequencing layer. It owns callback lifetimes and
    /// exactly-once presentation tokens, while W1, W5, W6, and W7 retain all
    /// Run, presentation, capture/Host, and task-rule authority respectively.
    /// </summary>
    public sealed class InteractionStudyFlow : IDisposable
    {
        private IInteractionStudyRunPort run;
        private IInteractionStudyPresentationPort presentation;
        private IInteractionStudyTaskPort tasks;
        private IDisposable runSubscription;
        private IDisposable presentationSubscription;
        private IDisposable taskSubscription;
        private long bindingEpoch;
        private bool suspended;
        private bool disposed;
        private bool terminalTaskCleaned;
        private bool abortInProgress;
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
        private string terminalCleanupWarning = string.Empty;

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
                bool commandIdle = !suspended && !disposed &&
                    pendingPresentation == null && activePlayback == null &&
                    !abortInProgress;
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
                        phase != null && phase.ReplayAvailable,
                    commandIdle && state == RunState.Running &&
                        phase != null && phase.GiveUpAvailable,
                    abortInProgress || state == RunState.Aborting,
                    resolvedStatus
                );
            }
        }

        public InteractionStudyFlowCommandResult TryStart()
        {
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
            }
            catch (Exception exception)
            {
                return AbortAfterIntegrationFailure(
                    "W7 configuration failed: " + exception.Message
                );
            }

            status = "Run consumed; waiting for Host and presentation.";
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
                !phase.ReplayAvailable || pendingPresentation != null ||
                activePlayback != null)
            {
                return Fail(
                    "Replay is unavailable until W1 completes the first " +
                    "playback, and it can be consumed only once."
                );
            }

            if (!run.TryRequestReplay(out string error))
            {
                return Fail(error);
            }

            status = "Replay requested through W6's authoritative token.";
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
                !phase.GiveUpAvailable || pendingPresentation != null ||
                activePlayback != null)
            {
                return Fail(
                    "Give Up requires W1's completed one-time replay gate."
                );
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

        public void Tick()
        {
            if (disposed)
            {
                return;
            }

            RunState state = run.State;
            if (state == RunState.Aborting)
            {
                abortInProgress = true;
                return;
            }
            if (!IsTerminal(state))
            {
                return;
            }

            try
            {
                if (presentation.PhaseActive)
                {
                    presentation.EndPhase();
                }
            }
            catch (Exception exception)
            {
                RecordTerminalCleanupWarning(
                    "W5 terminal EndPhase failed: " + exception.Message
                );
            }
            ClearPresentationTokens();

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
                    terminalCleanupWarning;
                NotifyChanged();
                return;
            }
            if (!resetAccepted)
            {
                status = "Terminal Run is waiting for W6 seal/upload cleanup." +
                    (string.IsNullOrWhiteSpace(terminalCleanupWarning)
                        ? string.Empty
                        : " " + terminalCleanupWarning);
                return;
            }

            abortInProgress = false;
            terminalTaskCleaned = false;
            lastRequestSequence = 0L;
            lastRequestRunId = string.Empty;
            status = string.IsNullOrWhiteSpace(terminalCleanupWarning)
                ? "PreStart"
                : "PreStart after terminal cleanup warning: " +
                    terminalCleanupWarning;
            terminalCleanupWarning = string.Empty;
            NotifyChanged();
        }

        public void Suspend(string reason)
        {
            if (disposed || suspended)
            {
                return;
            }
            Exception failure = null;
            try
            {
                if (IsRunOwned(run.State))
                {
                    BeginAbort(
                        string.IsNullOrWhiteSpace(reason)
                            ? "study_flow_suspended"
                            : reason.Trim()
                    );
                }
                else if (run.State == RunState.Aborting)
                {
                    presentation.EndPhase();
                    ClearPresentationTokens();
                    tasks.Disable();
                    abortInProgress = true;
                }
            }
            catch (Exception exception)
            {
                failure = exception;
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
            status = IsTerminal(run.State)
                ? "Terminal Run detected after resume."
                : status;
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
                if (!suspended && IsRunOwned(run.State))
                {
                    BeginAbort("study_flow_disposed");
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
            try
            {
                if (request.PlaybackKind ==
                    InteractionPresentationPlaybackKind.First)
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
            }

            if (!began)
            {
                status = "W5 presentation failed: " + error;
                BeginAbort("presentation_start_failed");
                return;
            }
            status = "Waiting for W5's actual first presented frame.";
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
                status = "W6 rejected first-frame ACK: " + error;
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
                        "W1 exposed no phase after first-frame ACK."
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
                ? "First playback completed; Replay is governed by W1."
                : "Replay completed; Give Up is governed by W1.";
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
            BeginAbort("presentation_fault");
        }

        private InteractionStudyFlowCommandResult BeginAbort(string reason)
        {
            RunState state = run.State;
            if (state == RunState.Aborting)
            {
                abortInProgress = true;
                return Fail("Run abort is already in progress.");
            }
            if (!IsRunOwned(state))
            {
                return Fail("Run cannot abort from " + state + ".");
            }

            string presentationError = null;
            try
            {
                presentation.EndPhase();
            }
            catch (Exception exception)
            {
                presentationError = exception.Message;
            }
            ClearPresentationTokens();

            bool accepted = run.TryAbort(reason, out string error);
            tasks.Disable();
            abortInProgress = accepted || run.State == RunState.Aborting;
            if (!accepted)
            {
                return Fail(
                    string.IsNullOrWhiteSpace(presentationError)
                        ? error
                        : "W5 EndPhase failed: " + presentationError +
                            "; W6 abort failed: " + error
                );
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
            reason = null;
            return true;
        }

        private void ResetRunLocalState()
        {
            ClearPresentationTokens();
            terminalTaskCleaned = false;
            abortInProgress = false;
            lastRequestSequence = 0L;
            lastRequestRunId = string.Empty;
            lastHandledResult = null;
            outcomeHandledPhaseId = null;
            progress = 0;
            requiredProgress = 0;
            terminalCleanupWarning = string.Empty;
        }

        private void RecordTerminalCleanupWarning(string warning)
        {
            if (string.IsNullOrWhiteSpace(warning))
            {
                return;
            }
            terminalCleanupWarning = string.IsNullOrWhiteSpace(
                terminalCleanupWarning)
                ? warning.Trim()
                : terminalCleanupWarning + " | " + warning.Trim();
        }

        private void ClearPresentationTokens()
        {
            pendingPresentation = null;
            activePlayback = null;
            playbackCompletionHandled = false;
        }

        private bool IsCurrent(long epoch)
        {
            return !disposed && !suspended && epoch == bindingEpoch;
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
            return state == RunState.AwaitingHost ||
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
    }
}
