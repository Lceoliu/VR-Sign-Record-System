using System;
using System.Globalization;
using System.Text;
using SignVR.Interaction.CaptureHost;
using SignVR.Interaction.Core;
using SignVR.Interaction.PhaseAdapters;
using SignVR.Interaction.Presentation;

namespace SignVR.Interaction.Orchestration
{
    public interface IInteractionStudyStrictStartGate
    {
        bool CanStartStudy(out string reason);
    }

    public static class InteractionStudyRunModePolicy
    {
        public static bool RequiresStrictXrGate(InteractionRunMode mode)
        {
            if (!Enum.IsDefined(typeof(InteractionRunMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            return mode == InteractionRunMode.StandaloneStudy;
        }
    }

    public sealed class UnityInteractionStudyRunPort :
        IInteractionStudyRunPort
    {
        private readonly InteractionRunController controller;
        private readonly IInteractionStudyStrictStartGate strictStartGate;
        private string adapterError = string.Empty;

        public UnityInteractionStudyRunPort(
            InteractionRunController controller,
            IInteractionStudyStrictStartGate strictStartGate = null)
        {
            this.controller = controller ??
                throw new ArgumentNullException(nameof(controller));
            this.strictStartGate = strictStartGate;
        }

        public RunState State => controller.State;
        public RunPlan Plan => controller.Plan;
        public PhaseExecutionSnapshot CurrentPhase =>
            controller.StateMachine?.CurrentPhase;
        public string LastError => string.IsNullOrWhiteSpace(adapterError)
            ? controller.LastError
            : adapterError;

        public IDisposable Subscribe(
            Action<InteractionStudyPresentationRequest> presentationRequested)
        {
            if (presentationRequested == null)
            {
                throw new ArgumentNullException(nameof(presentationRequested));
            }
            void Handler(InteractionPresentationRequest request)
            {
                presentationRequested(Map(request));
            }
            controller.PresentationRequested += Handler;
            return new InteractionStudyCallbackDisposable(
                () => controller.PresentationRequested -= Handler
            );
        }

        public bool CanStart(out string reason)
        {
            if (!controller.CanStart(out reason))
            {
                return false;
            }
            if (InteractionStudyRunModePolicy.RequiresStrictXrGate(
                    controller.RunMode) &&
                (strictStartGate == null ||
                 !strictStartGate.CanStartStudy(out reason)))
            {
                reason = strictStartGate == null
                    ? "Study Flow strict XR capture gate is not configured."
                    : reason;
                return false;
            }
            reason = null;
            return true;
        }

        public bool TryStart(out string error)
        {
            if (!CanStart(out error))
            {
                adapterError = error;
                return false;
            }
            bool accepted = controller.TryStartRun(out error);
            adapterError = accepted ? string.Empty : error;
            return accepted;
        }

        public bool TryRequestReplay(out string error)
        {
            return Try(
                () => controller.ReplayInstruction(),
                out error
            );
        }

        public bool TryAcknowledgePresentationStarted(
            InteractionStudyPresentationRequest request,
            out string error)
        {
            if (request == null)
            {
                error = "Presentation request is required.";
                return false;
            }
            return Try(
                () => controller.NotifyInstructionPlaybackStarted(
                    request.RequestSequence,
                    request.PhaseId,
                    request.PlaybackKind
                ),
                out error
            );
        }

        public bool TryNotifyPlaybackCompleted(
            InteractionPresentationPlaybackKind playbackKind,
            out string error)
        {
            return Try(
                () =>
                {
                    if (playbackKind ==
                        InteractionPresentationPlaybackKind.First)
                    {
                        controller.NotifyFirstPlaybackCompleted();
                    }
                    else
                    {
                        controller.NotifyReplayPlaybackCompleted();
                    }
                },
                out error
            );
        }

        public bool TryRecordValidationResult(
            ValidationResult result,
            out string error)
        {
            if (result == null)
            {
                error = "Validation result is required.";
                return false;
            }
            if (!result.InputKind.HasValue)
            {
                error = null;
                return true;
            }

            string detail = BuildValidationDetail(result);
            string targetId = result.InputTargetId;
            return Try(
                () =>
                {
                    bool correct = result.Accepted &&
                        !result.InteractionError;
                    controller.RecordInteractionAttempt(
                        correct,
                        targetId: targetId,
                        detailPayloadJson: detail
                    );
                    if (result.InteractionError)
                    {
                        controller.RecordInteractionValidationError(
                            result.ProgressReset,
                            targetId: targetId,
                            payloadJson: detail
                        );
                    }
                },
                out error
            );
        }

        public bool TryRecordPresentationObservation(
            InteractionStudyPresentationObservation observation,
            out string error)
        {
            if (observation == null)
            {
                error = "Presentation observation is required.";
                return false;
            }
            return Try(
                () =>
                {
                    switch (observation.Kind)
                    {
                        case InteractionStudyPresentationObservationKind
                            .BubbleShown:
                            controller.NotifyBubbleShown();
                            break;
                        case InteractionStudyPresentationObservationKind
                            .BubbleHidden:
                            controller.NotifyBubbleHidden();
                            break;
                        case InteractionStudyPresentationObservationKind
                            .PointingHitStarted:
                            controller.NotifyPointingHitStarted(
                                observation.TargetId
                            );
                            break;
                        case InteractionStudyPresentationObservationKind
                            .PointingHitEnded:
                            controller.NotifyPointingHitEnded();
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(
                                nameof(observation)
                            );
                    }
                },
                out error
            );
        }

        public bool TryFinishPhase(bool stuck, out string error)
        {
            return Try(
                () =>
                {
                    if (stuck)
                    {
                        controller.GiveUpCurrentPhase();
                    }
                    else
                    {
                        controller.CompleteCurrentPhase();
                    }
                },
                out error
            );
        }

        public bool TryAbort(string reason, out string error)
        {
            bool accepted = controller.TryAbortRun(reason, out error);
            adapterError = accepted ? string.Empty : error;
            return accepted;
        }

        public bool TryResetToPreStart()
        {
            bool reset = controller.ResetToPreStart();
            if (reset)
            {
                adapterError = string.Empty;
            }
            return reset;
        }

        private bool Try(Action action, out string error)
        {
            try
            {
                action();
                adapterError = string.Empty;
                error = null;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                error = exception.Message;
                adapterError = error;
                return false;
            }
        }

        private static InteractionStudyPresentationRequest Map(
            InteractionPresentationRequest request)
        {
            return new InteractionStudyPresentationRequest(
                request.RequestSequence,
                request.RunId,
                request.PhaseId,
                request.PlaybackKind
            );
        }

        private static string BuildValidationDetail(ValidationResult result)
        {
            var builder = new StringBuilder(320);
            builder.Append('{');
            AppendName(builder, "phase_id");
            builder.Append(result.PhaseId.ToString(CultureInfo.InvariantCulture));
            AppendSeparatorAndName(builder, "accepted");
            builder.Append(result.Accepted ? "true" : "false");
            AppendSeparatorAndName(builder, "progress");
            builder.Append(result.Progress.ToString(CultureInfo.InvariantCulture));
            AppendSeparatorAndName(builder, "required_progress");
            builder.Append(
                result.RequiredProgress.ToString(CultureInfo.InvariantCulture)
            );
            AppendSeparatorAndName(builder, "progress_reset");
            builder.Append(result.ProgressReset ? "true" : "false");
            AppendSeparatorAndName(builder, "feedback_cue");
            InteractionJson.AppendQuoted(builder, result.FeedbackCue.ToString());
            AppendSeparatorAndName(builder, "validation_error");
            InteractionJson.AppendQuoted(builder, result.Error.ToString());
            AppendSeparatorAndName(builder, "input_kind");
            AppendNullableString(builder, result.InputKind?.ToString());
            AppendSeparatorAndName(builder, "input_target_id");
            AppendNullableString(builder, result.InputTargetId);
            AppendSeparatorAndName(builder, "input_secondary_target_id");
            AppendNullableString(builder, result.InputSecondaryTargetId);
            AppendSeparatorAndName(builder, "input_digit_value");
            if (result.InputDigitValue.HasValue)
            {
                builder.Append(
                    result.InputDigitValue.Value.ToString(
                        CultureInfo.InvariantCulture
                    )
                );
            }
            else
            {
                builder.Append("null");
            }
            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendName(StringBuilder builder, string name)
        {
            InteractionJson.AppendQuoted(builder, name);
            builder.Append(':');
        }

        private static void AppendSeparatorAndName(
            StringBuilder builder,
            string name)
        {
            builder.Append(',');
            AppendName(builder, name);
        }

        private static void AppendNullableString(
            StringBuilder builder,
            string value)
        {
            if (value == null)
            {
                builder.Append("null");
            }
            else
            {
                InteractionJson.AppendQuoted(builder, value);
            }
        }
    }

    public sealed class UnityInteractionStudyPresentationPort :
        IInteractionStudyPresentationPort
    {
        private readonly InstructionPresentationController controller;

        public UnityInteractionStudyPresentationPort(
            InstructionPresentationController controller)
        {
            this.controller = controller ??
                throw new ArgumentNullException(nameof(controller));
        }

        public bool PhaseActive => controller.PhaseActive;
        public bool ReplayAvailable => controller.ReplayIsAvailable;
        public bool GiveUpAvailable => controller.GiveUpIsAvailable;

        public IDisposable Subscribe(
            Action<InteractionPresentationPlaybackKind> firstFramePresented,
            Action<InteractionPresentationPlaybackKind> playbackCompleted,
            Action<InteractionStudyPresentationObservation>
                presentationObserved,
            Action<string> presentationFaulted)
        {
            if (firstFramePresented == null || playbackCompleted == null ||
                presentationObserved == null || presentationFaulted == null)
            {
                throw new ArgumentNullException(
                    "Presentation callbacks must all be supplied."
                );
            }

            void Started(InstructionPlaybackPass pass, double _)
            {
                if (TryMap(pass, out InteractionPresentationPlaybackKind kind))
                {
                    firstFramePresented(kind);
                }
            }
            void Completed(InstructionPlaybackPass pass, double _)
            {
                if (TryMap(pass, out InteractionPresentationPlaybackKind kind))
                {
                    playbackCompleted(kind);
                }
            }
            void BubbleShown(double _)
            {
                presentationObserved(new InteractionStudyPresentationObservation(
                    InteractionStudyPresentationObservationKind.BubbleShown
                ));
            }
            void BubbleHidden(double _)
            {
                presentationObserved(new InteractionStudyPresentationObservation(
                    InteractionStudyPresentationObservationKind.BubbleHidden
                ));
            }
            void PointingStarted(string targetId, double _)
            {
                presentationObserved(new InteractionStudyPresentationObservation(
                    InteractionStudyPresentationObservationKind
                        .PointingHitStarted,
                    targetId
                ));
            }
            void PointingEnded(string _, double __)
            {
                presentationObserved(new InteractionStudyPresentationObservation(
                    InteractionStudyPresentationObservationKind.PointingHitEnded
                ));
            }

            controller.InstructionPlaybackStarted += Started;
            controller.InstructionPlaybackCompleted += Completed;
            controller.BubbleShown += BubbleShown;
            controller.BubbleHidden += BubbleHidden;
            controller.PresentationFaulted += presentationFaulted;
            GhostPointingDetector detector = controller.PointingDetector;
            if (detector != null)
            {
                detector.HitStarted += PointingStarted;
                detector.HitEnded += PointingEnded;
            }

            return new InteractionStudyCallbackDisposable(() =>
            {
                controller.InstructionPlaybackStarted -= Started;
                controller.InstructionPlaybackCompleted -= Completed;
                controller.BubbleShown -= BubbleShown;
                controller.BubbleHidden -= BubbleHidden;
                controller.PresentationFaulted -= presentationFaulted;
                if (detector != null)
                {
                    detector.HitStarted -= PointingStarted;
                    detector.HitEnded -= PointingEnded;
                }
            });
        }

        public bool TryBeginPhase(
            RunPhasePlan phasePlan,
            AssistanceCondition condition,
            out string error)
        {
            try
            {
                controller.BeginPhase(phasePlan, condition);
                error = null;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryBeginReplay(out string error)
        {
            try
            {
                bool accepted = controller.Replay();
                error = accepted ? null : "W5 rejected Replay.";
                return accepted;
            }
            catch (InvalidOperationException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public void EndPhase()
        {
            controller.EndPhase();
        }

        private static bool TryMap(
            InstructionPlaybackPass pass,
            out InteractionPresentationPlaybackKind kind)
        {
            if (pass == InstructionPlaybackPass.First)
            {
                kind = InteractionPresentationPlaybackKind.First;
                return true;
            }
            if (pass == InstructionPlaybackPass.Replay)
            {
                kind = InteractionPresentationPlaybackKind.Replay;
                return true;
            }
            kind = default;
            return false;
        }
    }

    public sealed class UnityInteractionStudyTaskPort :
        IInteractionStudyTaskPort
    {
        private readonly InteractionPhaseCoordinator coordinator;

        public UnityInteractionStudyTaskPort(
            InteractionPhaseCoordinator coordinator)
        {
            this.coordinator = coordinator ??
                throw new ArgumentNullException(nameof(coordinator));
        }

        public RunPlan Plan => coordinator.Plan;
        public ValidationResult LastResult => coordinator.LastResult;

        public IDisposable Subscribe(Action<ValidationResult> resultProduced)
        {
            if (resultProduced == null)
            {
                throw new ArgumentNullException(nameof(resultProduced));
            }
            coordinator.ResultProduced += resultProduced;
            return new InteractionStudyCallbackDisposable(
                () => coordinator.ResultProduced -= resultProduced
            );
        }

        public void Configure(RunPlan plan)
        {
            coordinator.Configure(plan);
        }

        public void Enable()
        {
            coordinator.Enable();
        }

        public void Disable()
        {
            coordinator.Disable();
        }

        public void Synchronize(PhaseExecutionSnapshot snapshot)
        {
            coordinator.Synchronize(snapshot);
        }

        public ValidationResult GiveUp(PhaseExecutionSnapshot snapshot)
        {
            return coordinator.GiveUpCurrentPhase(snapshot);
        }

        public void Reset()
        {
            coordinator.Reset();
        }

        public void Abort()
        {
            coordinator.Abort();
        }
    }
}
