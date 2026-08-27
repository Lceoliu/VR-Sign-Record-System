using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SignVR.Interaction.CaptureHost
{
    public enum InteractionExposureKind
    {
        Text,
        Pointing
    }

    public sealed class InteractionDataCompleteness
    {
        public InteractionDataCompleteness(
            bool manifest,
            bool events,
            bool poses,
            bool objects,
            bool summary,
            long captureGapCount)
        {
            if (captureGapCount < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(captureGapCount));
            }
            Manifest = manifest;
            Events = events;
            Poses = poses;
            Objects = objects;
            Summary = summary;
            CaptureGapCount = captureGapCount;
        }

        public bool Manifest { get; }
        public bool Events { get; }
        public bool Poses { get; }
        public bool Objects { get; }
        public bool Summary { get; }
        public long CaptureGapCount { get; }
        public bool QuestArtifactsComplete =>
            Manifest && Events && Poses && Objects && Summary;
    }

    public sealed class InteractionPhaseSummary
    {
        internal InteractionPhaseSummary(MutablePhase source)
        {
            PhaseId = source.PhaseId;
            FirstAttemptCorrect = source.FirstAttemptCorrect;
            FirstActionCorrect = source.FirstAttemptCorrect;
            FirstAttemptSuccess = source.Finished
                ? source.PhaseCompleted && source.ErrorCount == 0 &&
                    source.FirstAttemptCorrect == true
                : (bool?)null;
            PhaseCompleted = source.PhaseCompleted;
            PhaseStuck = source.PhaseStuck;
            ErrorCount = source.ErrorCount;
            ReplayUsed = source.ReplayUsed;
            TimeToFirstActionSeconds = source.TimeToFirstActionSeconds;
            TimeToCompletionSeconds = source.TimeToCompletionSeconds;
            TextExposureSeconds = source.TextExposureSeconds;
            PointingExposureSeconds = source.PointingExposureSeconds;
            TextExposed = source.TextExposed;
            PointingExposed = source.PointingExposed;
            TimeoutObserved = source.TimeoutObserved;
        }

        public int PhaseId { get; }
        /// <summary>
        /// Legacy compatibility alias whose historical meaning is whether the
        /// participant's first recorded action was correct.
        /// </summary>
        public bool? FirstAttemptCorrect { get; }
        public bool? FirstActionCorrect { get; }
        public bool? FirstAttemptSuccess { get; }
        public bool PhaseCompleted { get; }
        public bool PhaseStuck { get; }
        public int ErrorCount { get; }
        public bool ReplayUsed { get; }
        public double? TimeToFirstActionSeconds { get; }
        public double? TimeToCompletionSeconds { get; }
        public bool TextExposed { get; }
        public bool PointingExposed { get; }
        public double TextExposureSeconds { get; }
        public double PointingExposureSeconds { get; }
        public bool TimeoutObserved { get; }
    }

    public sealed class InteractionRunSummary
    {
        internal InteractionRunSummary(
            string runId,
            string status,
            DateTimeOffset? startedUtc,
            DateTimeOffset endedUtc,
            double totalDurationSeconds,
            IReadOnlyList<InteractionPhaseSummary> phases,
            string abortReason,
            InteractionDataCompleteness dataCompleteness,
            InteractionCaptureQuality captureQuality = null)
        {
            RunId = runId;
            Status = status;
            StartedUtc = startedUtc;
            EndedUtc = endedUtc;
            TotalDurationSeconds = totalDurationSeconds;
            Phases = phases;
            AbortReason = abortReason;
            DataCompleteness = dataCompleteness ??
                throw new ArgumentNullException(nameof(dataCompleteness));
            CaptureQuality = captureQuality ??
                InteractionCaptureQuality.CreateUnavailable(
                    DataCompleteness.CaptureGapCount
                );
            TotalErrorCount = phases.Sum(phase => phase.ErrorCount);
            TotalReplayCount = phases.Count(phase => phase.ReplayUsed);
            TotalStuckCount = phases.Count(phase => phase.PhaseStuck);
            CompletedPhaseCount = phases.Count(phase => phase.PhaseCompleted);
        }

        public string RunId { get; }
        public string Status { get; }
        public DateTimeOffset? StartedUtc { get; }
        public DateTimeOffset EndedUtc { get; }
        public double TotalDurationSeconds { get; }
        public IReadOnlyList<InteractionPhaseSummary> Phases { get; }
        public int TotalErrorCount { get; }
        public int TotalReplayCount { get; }
        public int TotalStuckCount { get; }
        public int CompletedPhaseCount { get; }
        public string AbortReason { get; }
        public InteractionDataCompleteness DataCompleteness { get; }
        public InteractionCaptureQuality CaptureQuality { get; }

        internal InteractionRunSummary WithCaptureQuality(
            InteractionCaptureQuality captureQuality)
        {
            return new InteractionRunSummary(
                RunId,
                Status,
                StartedUtc,
                EndedUtc,
                TotalDurationSeconds,
                Phases,
                AbortReason,
                DataCompleteness,
                captureQuality ?? throw new ArgumentNullException(
                    nameof(captureQuality)
                )
            );
        }
    }

    public sealed class InteractionSummaryTracker
    {
        private readonly string runId;
        private readonly MutablePhase[] phases;
        private bool runStarted;
        private double runStartedMonotonic;
        private DateTimeOffset? runStartedUtc;
        private bool sealedResult;

        public InteractionSummaryTracker(string runId)
        {
            this.runId = InteractionStoragePaths.ValidateSegment(
                runId,
                nameof(runId)
            );
            phases = new MutablePhase[6];
            for (int index = 0; index < phases.Length; index++)
            {
                phases[index] = new MutablePhase(index + 1);
            }
        }

        /// <summary>
        /// Creates a deep, unsealed copy for ownership transfer to background
        /// terminalization. Mutating or sealing the copy cannot publish partial
        /// state through the Controller-owned tracker.
        /// </summary>
        public InteractionSummaryTracker CreateDetachedCopy()
        {
            EnsureNotSealed();
            var copy = new InteractionSummaryTracker(runId)
            {
                runStarted = runStarted,
                runStartedMonotonic = runStartedMonotonic,
                runStartedUtc = runStartedUtc
            };
            for (int index = 0; index < phases.Length; index++)
            {
                copy.phases[index] = new MutablePhase(phases[index]);
            }
            return copy;
        }

        public void BeginRun(double monotonicTimeSeconds, DateTimeOffset utcTime)
        {
            EnsureNotSealed();
            ValidateTime(monotonicTimeSeconds);
            if (runStarted)
            {
                throw new InvalidOperationException("Run summary already started.");
            }
            runStarted = true;
            runStartedMonotonic = monotonicTimeSeconds;
            runStartedUtc = utcTime.ToUniversalTime();
        }

        public void BeginPhase(int phaseId, double monotonicTimeSeconds)
        {
            MutablePhase phase = GetPhase(phaseId);
            EnsureRunTime(monotonicTimeSeconds);
            if (phase.Started)
            {
                throw new InvalidOperationException(
                    "Phase " + phaseId + " summary already started."
                );
            }
            phase.Started = true;
            phase.StartedMonotonic = monotonicTimeSeconds;
        }

        public void RecordAttempt(
            int phaseId,
            bool correct,
            double monotonicTimeSeconds)
        {
            MutablePhase phase = RequireActivePhase(phaseId, monotonicTimeSeconds);
            if (!phase.FirstAttemptCorrect.HasValue)
            {
                phase.FirstAttemptCorrect = correct;
                phase.TimeToFirstActionSeconds =
                    monotonicTimeSeconds - phase.StartedMonotonic;
            }
        }

        public void RecordError(int phaseId, double monotonicTimeSeconds)
        {
            MutablePhase phase = RequireActivePhase(phaseId, monotonicTimeSeconds);
            phase.ErrorCount = checked(phase.ErrorCount + 1);
        }

        public void RecordReplay(int phaseId, double monotonicTimeSeconds)
        {
            MutablePhase phase = RequireActivePhase(phaseId, monotonicTimeSeconds);
            if (phase.ReplayUsed)
            {
                throw new InvalidOperationException(
                    "Replay is already recorded for phase " + phaseId + "."
                );
            }
            phase.ReplayUsed = true;
        }

        public void RecordTimeout(int phaseId, double monotonicTimeSeconds)
        {
            MutablePhase phase = RequireActivePhase(phaseId, monotonicTimeSeconds);
            phase.TimeoutObserved = true;
        }

        public void BeginExposure(
            int phaseId,
            InteractionExposureKind kind,
            double monotonicTimeSeconds)
        {
            MutablePhase phase = RequireActivePhase(phaseId, monotonicTimeSeconds);
            phase.BeginExposure(kind, monotonicTimeSeconds);
        }

        public void EndExposure(
            int phaseId,
            InteractionExposureKind kind,
            double monotonicTimeSeconds)
        {
            MutablePhase phase = RequireActivePhase(phaseId, monotonicTimeSeconds);
            phase.EndExposure(kind, monotonicTimeSeconds);
        }

        public void FinishPhase(
            int phaseId,
            bool completed,
            bool stuck,
            double monotonicTimeSeconds)
        {
            if (completed == stuck)
            {
                throw new ArgumentException(
                    "A phase result must be exactly one of completed or stuck."
                );
            }
            MutablePhase phase = RequireActivePhase(phaseId, monotonicTimeSeconds);
            phase.CloseExposures(monotonicTimeSeconds);
            phase.PhaseCompleted = completed;
            phase.PhaseStuck = stuck;
            phase.TimeToCompletionSeconds =
                monotonicTimeSeconds - phase.StartedMonotonic;
            phase.Finished = true;
        }

        public InteractionRunSummary SealCompleted(
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            InteractionDataCompleteness completeness)
        {
            for (int index = 0; index < phases.Length; index++)
            {
                if (!phases[index].Finished)
                {
                    throw new InvalidOperationException(
                        "Completed Run summary requires six phase results."
                    );
                }
            }
            return Seal(
                "completed",
                monotonicTimeSeconds,
                utcTime,
                null,
                completeness
            );
        }

        public InteractionRunSummary SealAborted(
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            string abortReason,
            InteractionDataCompleteness completeness)
        {
            if (string.IsNullOrWhiteSpace(abortReason))
            {
                throw new ArgumentException(
                    "Abort reason is required.",
                    nameof(abortReason)
                );
            }
            for (int index = 0; index < phases.Length; index++)
            {
                if (phases[index].Started && !phases[index].Finished)
                {
                    phases[index].CloseExposures(monotonicTimeSeconds);
                }
            }
            return Seal(
                "aborted",
                monotonicTimeSeconds,
                utcTime,
                abortReason.Trim(),
                completeness
            );
        }

        private InteractionRunSummary Seal(
            string status,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            string abortReason,
            InteractionDataCompleteness completeness)
        {
            EnsureNotSealed();
            ValidateTime(monotonicTimeSeconds);
            if (runStarted && monotonicTimeSeconds < runStartedMonotonic)
            {
                throw new ArgumentOutOfRangeException(nameof(monotonicTimeSeconds));
            }
            sealedResult = true;
            var snapshots = new List<InteractionPhaseSummary>(phases.Length);
            for (int index = 0; index < phases.Length; index++)
            {
                snapshots.Add(new InteractionPhaseSummary(phases[index]));
            }
            return new InteractionRunSummary(
                runId,
                status,
                runStartedUtc,
                utcTime.ToUniversalTime(),
                runStarted ? monotonicTimeSeconds - runStartedMonotonic : 0d,
                snapshots.AsReadOnly(),
                abortReason,
                completeness ?? throw new ArgumentNullException(nameof(completeness))
            );
        }

        private MutablePhase RequireActivePhase(
            int phaseId,
            double monotonicTimeSeconds)
        {
            MutablePhase phase = GetPhase(phaseId);
            EnsureRunTime(monotonicTimeSeconds);
            if (!phase.Started || phase.Finished ||
                monotonicTimeSeconds < phase.StartedMonotonic)
            {
                throw new InvalidOperationException(
                    "Phase " + phaseId + " is not active in the summary."
                );
            }
            return phase;
        }

        private MutablePhase GetPhase(int phaseId)
        {
            EnsureNotSealed();
            if (phaseId < 1 || phaseId > phases.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(phaseId));
            }
            return phases[phaseId - 1];
        }

        private void EnsureRunTime(double value)
        {
            ValidateTime(value);
            if (!runStarted || value < runStartedMonotonic)
            {
                throw new InvalidOperationException(
                    "Run summary has not started or time moved backwards."
                );
            }
        }

        private void EnsureNotSealed()
        {
            if (sealedResult)
            {
                throw new InvalidOperationException("Run summary is sealed.");
            }
        }

        private static void ValidateTime(double value)
        {
            InteractionEventSequencer.ValidateFiniteNonNegative(
                value,
                nameof(value)
            );
        }
    }

    internal sealed class MutablePhase
    {
        private double? textExposureStarted;
        private double? pointingExposureStarted;

        public MutablePhase(int phaseId)
        {
            PhaseId = phaseId;
        }

        public MutablePhase(MutablePhase source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            PhaseId = source.PhaseId;
            Started = source.Started;
            Finished = source.Finished;
            StartedMonotonic = source.StartedMonotonic;
            FirstAttemptCorrect = source.FirstAttemptCorrect;
            PhaseCompleted = source.PhaseCompleted;
            PhaseStuck = source.PhaseStuck;
            ErrorCount = source.ErrorCount;
            ReplayUsed = source.ReplayUsed;
            TimeToFirstActionSeconds = source.TimeToFirstActionSeconds;
            TimeToCompletionSeconds = source.TimeToCompletionSeconds;
            TextExposureSeconds = source.TextExposureSeconds;
            PointingExposureSeconds = source.PointingExposureSeconds;
            TextExposed = source.TextExposed;
            PointingExposed = source.PointingExposed;
            TimeoutObserved = source.TimeoutObserved;
            textExposureStarted = source.textExposureStarted;
            pointingExposureStarted = source.pointingExposureStarted;
        }

        public int PhaseId { get; }
        public bool Started { get; set; }
        public bool Finished { get; set; }
        public double StartedMonotonic { get; set; }
        public bool? FirstAttemptCorrect { get; set; }
        public bool PhaseCompleted { get; set; }
        public bool PhaseStuck { get; set; }
        public int ErrorCount { get; set; }
        public bool ReplayUsed { get; set; }
        public double? TimeToFirstActionSeconds { get; set; }
        public double? TimeToCompletionSeconds { get; set; }
        public double TextExposureSeconds { get; private set; }
        public double PointingExposureSeconds { get; private set; }
        public bool TextExposed { get; private set; }
        public bool PointingExposed { get; private set; }
        public bool TimeoutObserved { get; set; }

        public void BeginExposure(
            InteractionExposureKind kind,
            double monotonicTimeSeconds)
        {
            if (kind == InteractionExposureKind.Text)
            {
                if (textExposureStarted.HasValue)
                {
                    throw new InvalidOperationException("Text exposure already active.");
                }
                textExposureStarted = monotonicTimeSeconds;
                TextExposed = true;
            }
            else if (kind == InteractionExposureKind.Pointing)
            {
                if (pointingExposureStarted.HasValue)
                {
                    throw new InvalidOperationException("Pointing exposure already active.");
                }
                pointingExposureStarted = monotonicTimeSeconds;
                PointingExposed = true;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public void EndExposure(
            InteractionExposureKind kind,
            double monotonicTimeSeconds)
        {
            if (kind == InteractionExposureKind.Text)
            {
                TextExposureSeconds += CloseOne(
                    ref textExposureStarted,
                    monotonicTimeSeconds,
                    "Text"
                );
            }
            else if (kind == InteractionExposureKind.Pointing)
            {
                PointingExposureSeconds += CloseOne(
                    ref pointingExposureStarted,
                    monotonicTimeSeconds,
                    "Pointing"
                );
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public void CloseExposures(double monotonicTimeSeconds)
        {
            if (textExposureStarted.HasValue)
            {
                TextExposureSeconds += CloseOne(
                    ref textExposureStarted,
                    monotonicTimeSeconds,
                    "Text"
                );
            }
            if (pointingExposureStarted.HasValue)
            {
                PointingExposureSeconds += CloseOne(
                    ref pointingExposureStarted,
                    monotonicTimeSeconds,
                    "Pointing"
                );
            }
        }

        private static double CloseOne(
            ref double? started,
            double ended,
            string name)
        {
            if (!started.HasValue || ended < started.Value)
            {
                throw new InvalidOperationException(
                    name + " exposure is not active or time moved backwards."
                );
            }
            double result = ended - started.Value;
            started = null;
            return result;
        }
    }

    public static class InteractionSummaryJson
    {
        public static string Serialize(InteractionRunSummary value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            var builder = new StringBuilder(4096);
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "schema_version");
            builder.Append('1');
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "run_id");
            InteractionJson.AppendQuoted(builder, value.RunId);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "status");
            InteractionJson.AppendQuoted(builder, value.Status);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "started_utc");
            if (value.StartedUtc.HasValue)
            {
                InteractionJson.AppendQuoted(
                    builder,
                    InteractionRunManifestContractV1.FormatUtc(value.StartedUtc.Value)
                );
            }
            else
            {
                builder.Append("null");
            }
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "ended_utc");
            InteractionJson.AppendQuoted(
                builder,
                InteractionRunManifestContractV1.FormatUtc(value.EndedUtc)
            );
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "total_duration_s");
            InteractionJson.AppendFiniteDouble(builder, value.TotalDurationSeconds);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "total_error_count");
            builder.Append(value.TotalErrorCount.ToString(CultureInfo.InvariantCulture));
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "total_replay_count");
            builder.Append(value.TotalReplayCount.ToString(CultureInfo.InvariantCulture));
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "total_stuck_count");
            builder.Append(value.TotalStuckCount.ToString(CultureInfo.InvariantCulture));
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "completed_phase_count");
            builder.Append(value.CompletedPhaseCount.ToString(CultureInfo.InvariantCulture));
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "abort_reason");
            InteractionJson.AppendNullableString(builder, value.AbortReason);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "phases");
            builder.Append('[');
            for (int index = 0; index < value.Phases.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                AppendPhase(builder, value.Phases[index]);
            }
            builder.Append(']');
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "capture_quality");
            AppendCaptureQuality(builder, value.CaptureQuality);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "data_completeness");
            AppendCompleteness(builder, value.DataCompleteness);
            builder.Append('}');
            builder.Append('\n');
            return builder.ToString();
        }

        private static void AppendPhase(
            StringBuilder builder,
            InteractionPhaseSummary value)
        {
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "phase_id");
            builder.Append(value.PhaseId.ToString(CultureInfo.InvariantCulture));
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "first_attempt_correct");
            AppendNullableBoolean(builder, value.FirstAttemptCorrect);
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "first_attempt_correct_semantics"
            );
            InteractionJson.AppendQuoted(
                builder,
                "legacy_alias_of_first_action_correct"
            );
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "first_action_correct");
            AppendNullableBoolean(builder, value.FirstActionCorrect);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "first_attempt_success");
            AppendNullableBoolean(builder, value.FirstAttemptSuccess);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "phase_completed");
            InteractionCaptureJson.AppendBoolean(builder, value.PhaseCompleted);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "phase_stuck");
            InteractionCaptureJson.AppendBoolean(builder, value.PhaseStuck);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "error_count");
            builder.Append(value.ErrorCount.ToString(CultureInfo.InvariantCulture));
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "replay_used");
            InteractionCaptureJson.AppendBoolean(builder, value.ReplayUsed);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "time_to_first_action_s");
            AppendNullableDouble(builder, value.TimeToFirstActionSeconds);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "time_to_completion_s");
            AppendNullableDouble(builder, value.TimeToCompletionSeconds);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "text_exposed");
            InteractionCaptureJson.AppendBoolean(builder, value.TextExposed);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "pointing_exposed");
            InteractionCaptureJson.AppendBoolean(builder, value.PointingExposed);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "text_exposure_s");
            InteractionJson.AppendFiniteDouble(builder, value.TextExposureSeconds);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "pointing_exposure_s");
            InteractionJson.AppendFiniteDouble(builder, value.PointingExposureSeconds);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "timeout_observed");
            InteractionCaptureJson.AppendBoolean(builder, value.TimeoutObserved);
            builder.Append('}');
        }

        private static void AppendCompleteness(
            StringBuilder builder,
            InteractionDataCompleteness value)
        {
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "manifest");
            InteractionCaptureJson.AppendBoolean(builder, value.Manifest);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "events");
            InteractionCaptureJson.AppendBoolean(builder, value.Events);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "poses");
            InteractionCaptureJson.AppendBoolean(builder, value.Poses);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "objects");
            InteractionCaptureJson.AppendBoolean(builder, value.Objects);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "summary");
            InteractionCaptureJson.AppendBoolean(builder, value.Summary);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "capture_gap_count");
            builder.Append(value.CaptureGapCount.ToString(CultureInfo.InvariantCulture));
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "quest_artifacts_complete");
            InteractionCaptureJson.AppendBoolean(
                builder,
                value.QuestArtifactsComplete
            );
            builder.Append('}');
        }

        private static void AppendCaptureQuality(
            StringBuilder builder,
            InteractionCaptureQuality value)
        {
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(
                builder,
                "measurement_available"
            );
            InteractionCaptureJson.AppendBoolean(
                builder,
                value.MeasurementAvailable
            );
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "overall"
            );
            AppendQualityLevel(builder, value.Overall);

            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "sample_rate"
            );
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "status");
            AppendQualityLevel(builder, value.SampleRate);
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "target_hz"
            );
            InteractionJson.AppendFiniteDouble(builder, value.TargetSampleRateHz);
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "actual_hz"
            );
            InteractionJson.AppendFiniteDouble(builder, value.ActualSampleRateHz);
            builder.Append('}');

            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "tracking_validity"
            );
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "status");
            AppendQualityLevel(builder, value.TrackingValidity);
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "hmd_rate"
            );
            InteractionJson.AppendFiniteDouble(builder, value.HmdValidityRate);
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "left_hand_rate"
            );
            InteractionJson.AppendFiniteDouble(
                builder,
                value.LeftHandValidityRate
            );
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "right_hand_rate"
            );
            InteractionJson.AppendFiniteDouble(
                builder,
                value.RightHandValidityRate
            );
            builder.Append('}');

            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "required_probe_coverage"
            );
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "status");
            AppendQualityLevel(builder, value.RequiredProbeCoverage);
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "required_probe_count"
            );
            builder.Append(value.RequiredProbeCount.ToString(
                CultureInfo.InvariantCulture
            ));
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "rate"
            );
            InteractionJson.AppendFiniteDouble(
                builder,
                value.RequiredProbeCoverageRate
            );
            builder.Append('}');

            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "gaps"
            );
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "status");
            AppendQualityLevel(builder, value.CaptureGaps);
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "count"
            );
            builder.Append(value.CaptureGapCount.ToString(
                CultureInfo.InvariantCulture
            ));
            builder.Append('}');
            builder.Append('}');
        }

        private static void AppendQualityLevel(
            StringBuilder builder,
            InteractionCaptureQualityLevel value)
        {
            if (value < InteractionCaptureQualityLevel.Pass ||
                value > InteractionCaptureQualityLevel.Fail)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            InteractionJson.AppendQuoted(builder, value.ToString());
        }

        private static void AppendNullableBoolean(
            StringBuilder builder,
            bool? value)
        {
            if (value.HasValue)
            {
                InteractionCaptureJson.AppendBoolean(builder, value.Value);
            }
            else
            {
                builder.Append("null");
            }
        }

        private static void AppendNullableDouble(
            StringBuilder builder,
            double? value)
        {
            if (value.HasValue)
            {
                InteractionJson.AppendFiniteDouble(builder, value.Value);
            }
            else
            {
                builder.Append("null");
            }
        }
    }
}
