using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests.Presentation
{
    public sealed class W5InstructionPresentationStateTests
    {
        private const string AssemblyName = "Assembly-CSharp";
        private const string CoreAssemblyName = "SignVR.Interaction.Core";
        private const string PresentationNamespace =
            "SignVR.Interaction.Presentation.";

        [TestCase("TextAndPointing")]
        [TestCase("TextOnly")]
        public void TextConditionsRevealBubbleWhenSignerPlaybackStarts(
            string conditionName)
        {
            object state = CreatePresentationState();
            Invoke(state, "BeginPhase", Assistance(conditionName));
            Assert.That(Get<bool>(state, "BubbleVisible"), Is.False);

            Invoke(state, "InstructionPlaybackStarted", 4d);
            Assert.That(Get<bool>(state, "BubbleVisible"), Is.True);
            Assert.That(Get<bool>(state, "ReplayAvailable"), Is.False);

            Invoke(state, "FirstPlaybackCompleted", 5d);
            Assert.That(Get<bool>(state, "ReplayAvailable"), Is.True);

            Assert.That((bool)Invoke(state, "TryConsumeReplay"), Is.True);
            Assert.That(Get<bool>(state, "BubbleVisible"), Is.True,
                "Replay must not hide an already revealed transcript.");
        }

        [Test]
        public void PhaseExitHidesBubbleAndPreventsLateReveal()
        {
            object state = CreatePresentationState();
            Invoke(state, "BeginPhase", Assistance("TextOnly"));
            Invoke(state, "InstructionPlaybackStarted", 9d);
            Assert.That(Get<bool>(state, "BubbleVisible"), Is.True);
            Invoke(state, "FirstPlaybackCompleted", 10d);
            Invoke(state, "EndPhase");
            Invoke(state, "Tick", 20d);

            Assert.That(Get<bool>(state, "PhaseActive"), Is.False);
            Assert.That(Get<bool>(state, "BubbleVisible"), Is.False);
            Assert.That(Get<bool>(state, "ReplayAvailable"), Is.False);
        }

        [Test]
        public void SignOnlyNeverRevealsBubble()
        {
            object state = CreatePresentationState();
            Invoke(state, "BeginPhase", Assistance("SignOnly"));
            Invoke(state, "InstructionPlaybackStarted", 1d);
            Invoke(state, "FirstPlaybackCompleted", 2d);
            Invoke(state, "Tick", 200d);

            Assert.That(Get<bool>(state, "TextAllowed"), Is.False);
            Assert.That(Get<bool>(state, "BubbleVisible"), Is.False);
            Assert.That(Get<bool>(state, "ReplayAvailable"), Is.True);
        }

        [Test]
        public void ReplayDoesNotToggleOrDuplicateVisibleBubble()
        {
            object state = CreatePresentationState();
            int shown = 0;
            int hidden = 0;
            AddEventHandler(state, "BubbleShown", (Action)(() => shown++));
            AddEventHandler(state, "BubbleHidden", (Action)(() => hidden++));

            Invoke(state, "BeginPhase", Assistance("TextOnly"));
            Invoke(state, "InstructionPlaybackStarted", 1d);
            Invoke(state, "FirstPlaybackCompleted", 2d);
            Assert.That((bool)Invoke(state, "TryConsumeReplay"), Is.True);
            Invoke(state, "InstructionPlaybackStarted", 3d);
            Invoke(state, "ReplayCompleted");

            Assert.That(Get<bool>(state, "BubbleVisible"), Is.True);
            Assert.That(shown, Is.EqualTo(1));
            Assert.That(hidden, Is.Zero);

            Invoke(state, "EndPhase");
            Assert.That(shown, Is.EqualTo(1));
            Assert.That(hidden, Is.EqualTo(1));
        }

        [Test]
        public void AssistanceMatrixHasOnlyTheThreeFrozenCombinations()
        {
            Type assistanceType = RequireType(
                "SignVR.Interaction.Core.AssistanceCondition",
                CoreAssemblyName
            );
            string[] names = Enum.GetNames(assistanceType);
            Assert.That(names, Is.EqualTo(new[]
            {
                "TextAndPointing",
                "TextOnly",
                "SignOnly"
            }));
            Assert.That(names, Does.Not.Contain("PointingOnly"));

            var expected = new Dictionary<string, (bool Text, bool Pointing)>
            {
                ["TextAndPointing"] = (true, true),
                ["TextOnly"] = (true, false),
                ["SignOnly"] = (false, false)
            };
            foreach (string name in names)
            {
                object state = CreatePresentationState();
                Invoke(state, "BeginPhase", Assistance(name));
                Assert.That(
                    (Get<bool>(state, "TextAllowed"),
                        Get<bool>(state, "PointingAllowed")),
                    Is.EqualTo(expected[name]),
                    name
                );
            }
        }

        [Test]
        public void ReplayIsConsumedOnceAndGiveUpWaitsForReplayCompletion()
        {
            object state = CreatePresentationState();
            Invoke(state, "BeginPhase", Assistance("TextAndPointing"));

            Assert.That((bool)Invoke(state, "TryConsumeReplay"), Is.False);
            Assert.That(Get<bool>(state, "GiveUpAvailable"), Is.False);

            Invoke(state, "FirstPlaybackCompleted", 1d);
            Assert.That(Get<bool>(state, "ReplayAvailable"), Is.True);
            Assert.That((bool)Invoke(state, "TryConsumeReplay"), Is.True);
            Assert.That(Get<bool>(state, "ReplayConsumed"), Is.True);
            Assert.That(Get<bool>(state, "ReplayAvailable"), Is.False);
            Assert.That(Get<bool>(state, "GiveUpAvailable"), Is.False);
            Assert.That((bool)Invoke(state, "TryConsumeReplay"), Is.False);

            Invoke(state, "ReplayCompleted");
            Assert.That(Get<bool>(state, "GiveUpAvailable"), Is.True);
        }

        [Test]
        public void PresentationNeverBlocksCurrentPhaseInteractions()
        {
            object state = CreatePresentationState();
            Invoke(state, "BeginPhase", Assistance("TextAndPointing"));
            Assert.That(Get<bool>(state, "InteractionsEnabled"), Is.True);

            Invoke(state, "FirstPlaybackCompleted", 1d);
            Assert.That(Get<bool>(state, "InteractionsEnabled"), Is.True);
            Invoke(state, "TryConsumeReplay");
            Assert.That(Get<bool>(state, "InteractionsEnabled"), Is.True);

            Invoke(state, "EndPhase");
            Assert.That(Get<bool>(state, "InteractionsEnabled"), Is.False);
        }

        [Test]
        public void AllowedPointingHitShowsAtEntryWithoutDwell()
        {
            object state = CreatePointingState("target_a");
            int starts = 0;
            string startedTarget = null;
            double startedAt = -1d;
            AddEventHandler(
                state,
                "HitStarted",
                new Action<string, double>((targetId, time) =>
                {
                    starts++;
                    startedTarget = targetId;
                    startedAt = time;
                })
            );

            Invoke(state, "BeginPlayback", 3d);
            Assert.That(
                (bool)Invoke(state, "ObserveHit", "target_a", 3d),
                Is.True
            );
            Assert.That(Get<bool>(state, "VisualVisible"), Is.True);
            Assert.That(Get<string>(state, "ActiveTargetId"), Is.EqualTo("target_a"));
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(startedTarget, Is.EqualTo("target_a"));
            Assert.That(startedAt, Is.EqualTo(3d));
        }

        [Test]
        public void LostPointingHitEndsAtOneHundredFiftyMilliseconds()
        {
            object state = CreatePointingState("target_a");
            int ends = 0;
            double endedAt = -1d;
            AddEventHandler(
                state,
                "HitEnded",
                new Action<string, double>((_, time) =>
                {
                    ends++;
                    endedAt = time;
                })
            );

            Invoke(state, "BeginPlayback", 0d);
            Invoke(state, "ObserveHit", "target_a", 0d);
            Invoke(state, "ObserveNoHit", 0d);
            Invoke(state, "Tick", 0.149d);
            Assert.That(Get<bool>(state, "VisualVisible"), Is.True);
            Assert.That(ends, Is.Zero);

            Invoke(state, "Tick", 0.15d);
            Assert.That(Get<bool>(state, "VisualVisible"), Is.False);
            Assert.That(ends, Is.EqualTo(1));
            Assert.That(endedAt, Is.EqualTo(0.15d).Within(1e-9));
            Assert.That(
                Get<double>(state, "ExposureSeconds"),
                Is.EqualTo(0.15d).Within(1e-9)
            );
        }

        [Test]
        public void DisallowedTargetNeverStartsPointingPresentation()
        {
            object state = CreatePointingState("target_a");
            int starts = 0;
            AddEventHandler(
                state,
                "HitStarted",
                new Action<string, double>((_, __) => starts++)
            );

            Invoke(state, "BeginPlayback", 0d);
            Assert.That(
                (bool)Invoke(state, "ObserveHit", "target_b", 0d),
                Is.False
            );
            Assert.That(Get<bool>(state, "VisualVisible"), Is.False);
            Assert.That(starts, Is.Zero);
        }

        [Test]
        public void PlaybackStopImmediatelyClearsPointingPresentation()
        {
            object state = CreatePointingState("target_a");
            int ends = 0;
            AddEventHandler(
                state,
                "HitEnded",
                new Action<string, double>((_, __) => ends++)
            );

            Invoke(state, "BeginPlayback", 1d);
            Invoke(state, "ObserveHit", "target_a", 1d);
            Invoke(state, "StopPlayback", 1.05d);

            Assert.That(Get<bool>(state, "PlaybackActive"), Is.False);
            Assert.That(Get<bool>(state, "VisualVisible"), Is.False);
            Assert.That(Get<string>(state, "ActiveTargetId"), Is.EqualTo(string.Empty));
            Assert.That(ends, Is.EqualTo(1));
            Assert.That(
                Get<double>(state, "ExposureSeconds"),
                Is.EqualTo(0.05d).Within(1e-9)
            );
        }

        [Test]
        public void PlaybackStopClearsStateWhenHitEndedSubscriberThrows()
        {
            object state = CreatePointingState("target_a");
            AddEventHandler(
                state,
                "HitEnded",
                new Action<string, double>((_, __) =>
                    throw new InvalidOperationException("injected hit-end failure"))
            );

            Invoke(state, "BeginPlayback", 1d);
            Invoke(state, "ObserveHit", "target_a", 1d);
            Assert.Throws<InvalidOperationException>(() =>
                Invoke(state, "StopPlayback", 1.05d));

            Assert.That(Get<bool>(state, "PlaybackActive"), Is.False);
            Assert.That(Get<bool>(state, "VisualVisible"), Is.False);
            Assert.That(
                Get<string>(state, "ActiveTargetId"),
                Is.EqualTo(string.Empty)
            );
        }

        [Test]
        public void PhaseResetClearsExposureAndPreviousAllowlist()
        {
            object state = CreatePointingState("old_target");
            Invoke(state, "BeginPlayback", 0d);
            Invoke(state, "ObserveHit", "old_target", 0d);
            Invoke(state, "StopPlayback", 0.1d);

            Invoke(
                state,
                "ResetPhase",
                new[] { "new_target" },
                0.2d
            );
            Assert.That(Get<double>(state, "ExposureSeconds"), Is.Zero);
            Invoke(state, "BeginPlayback", 0.2d);
            Assert.That(
                (bool)Invoke(state, "ObserveHit", "old_target", 0.2d),
                Is.False
            );
            Assert.That(
                (bool)Invoke(state, "ObserveHit", "new_target", 0.2d),
                Is.True
            );
        }

        [Test]
        public void FirstPlayAndReplayUseOneFrozenArtifactAndRejectThirdPlay()
        {
            object state = CreatePlaybackState();
            Invoke(state, "Load", "wang/001/take_004.pose.jsonl");
            Assert.That((bool)Invoke(state, "Play"), Is.True);
            Assert.That(Get<string>(state, "ArtifactKey"),
                Is.EqualTo("wang/001/take_004.pose.jsonl"));
            Assert.That((bool)Invoke(state, "Complete"), Is.True);

            Assert.That((bool)Invoke(state, "Replay"), Is.True);
            Assert.That(Get<bool>(state, "ReplayConsumed"), Is.True);
            Assert.That(Get<string>(state, "ArtifactKey"),
                Is.EqualTo("wang/001/take_004.pose.jsonl"));
            Assert.That((bool)Invoke(state, "Complete"), Is.True);

            Assert.That((bool)Invoke(state, "Replay"), Is.False);
            Assert.That((bool)Invoke(state, "Play"), Is.False);
        }

        [Test]
        public void RepeatedLoadClearsEveryPreviousPlaybackFlag()
        {
            object state = CreatePlaybackState();
            Invoke(state, "Load", "artifact_a");
            Invoke(state, "Play");
            Invoke(state, "Complete");
            Invoke(state, "Replay");

            Invoke(state, "Load", "artifact_b");
            Assert.That(Get<string>(state, "ArtifactKey"), Is.EqualTo("artifact_b"));
            Assert.That(Get<bool>(state, "ReplayConsumed"), Is.False);
            Assert.That(Get<bool>(state, "FirstPlaybackCompleted"), Is.False);
            Assert.That(Get<string>(state, "CurrentPass"), Is.EqualTo("None"));
            Assert.That((bool)Invoke(state, "Play"), Is.True);
            Assert.That((bool)Invoke(state, "Play"), Is.False,
                "Repeated Play while already playing must not create a second timeline.");
        }

        [Test]
        public void StopCleansPlaybackAndRequiresAnotherLoad()
        {
            object state = CreatePlaybackState();
            Invoke(state, "Load", "artifact_a");
            Invoke(state, "Play");
            Invoke(state, "Stop");

            Assert.That(Get<string>(state, "Status"), Is.EqualTo("Stopped"));
            Assert.That(Get<string>(state, "CurrentPass"), Is.EqualTo("None"));
            Assert.That((bool)Invoke(state, "Play"), Is.False);
            Assert.That((bool)Invoke(state, "Replay"), Is.False);
        }

        [Test]
        public void PoseTimelineValidatesJointCountTimeAndExplicitEof()
        {
            object validator = CreatePoseValidator();
            Invoke(validator, "AcceptFrame", 0d, true, 2, 2, 2, 2);
            Invoke(validator, "AcceptFrame", 0.033d, false, 0, 0, 0, 0);
            Invoke(validator, "AcceptFrame", 0.067d, true, 2, 2, 2, 2);

            Assert.That(Get<bool>(validator, "IsComplete"), Is.False);
            Invoke(validator, "CompleteEof");
            Assert.That(Get<bool>(validator, "IsComplete"), Is.True);
            Assert.That(Get<int>(validator, "ExpectedJointCount"), Is.EqualTo(2));
            Assert.That(Get<int>(validator, "UsableFrameCount"), Is.EqualTo(2));
        }

        [Test]
        public void PoseTimelineRejectsMismatchedJointsAndDecreasingTime()
        {
            object jointValidator = CreatePoseValidator();
            Invoke(jointValidator, "AcceptFrame", 0d, true, 2, 2, 2, 2);
            Assert.Throws<InvalidOperationException>(() =>
                Invoke(jointValidator, "AcceptFrame", 0.1d, true, 3, 3, 3, 3));

            object timeValidator = CreatePoseValidator();
            Invoke(timeValidator, "AcceptFrame", 0.2d, true, 2, 2, 2, 2);
            Assert.Throws<InvalidOperationException>(() =>
                Invoke(timeValidator, "AcceptFrame", 0.1d, true, 2, 2, 2, 2));
        }

        [Test]
        public void PoseTimelineRejectsArrayMismatchAndEofWithoutUsablePose()
        {
            object arrayValidator = CreatePoseValidator();
            Assert.Throws<InvalidOperationException>(() =>
                Invoke(arrayValidator, "AcceptFrame", 0d, true, 2, 1, 2, 2));

            object emptyValidator = CreatePoseValidator();
            Invoke(emptyValidator, "AcceptFrame", 0d, false, 0, 0, 0, 0);
            Assert.Throws<InvalidOperationException>(() =>
                Invoke(emptyValidator, "CompleteEof"));
        }

        private static object CreatePresentationState()
        {
            return Activator.CreateInstance(RequireType(
                PresentationNamespace + "InstructionPhasePresentationState",
                AssemblyName
            ));
        }

        private static object CreatePointingState(params string[] targetIds)
        {
            return Activator.CreateInstance(
                RequireType(
                    PresentationNamespace + "GhostPointingState",
                    AssemblyName
                ),
                new object[] { targetIds }
            );
        }

        private static object CreatePlaybackState()
        {
            return Activator.CreateInstance(RequireType(
                PresentationNamespace + "InstructionGhostPlaybackState",
                AssemblyName
            ));
        }

        private static object CreatePoseValidator()
        {
            return Activator.CreateInstance(RequireType(
                PresentationNamespace + "InstructionPoseArtifactValidator",
                AssemblyName
            ));
        }

        private static object Assistance(string name)
        {
            return Enum.Parse(
                RequireType(
                    "SignVR.Interaction.Core.AssistanceCondition",
                    CoreAssemblyName
                ),
                name
            );
        }

        private static Type RequireType(string typeName, string assemblyName)
        {
            return Type.GetType(
                typeName + ", " + assemblyName,
                throwOnError: true
            );
        }

        private static object Invoke(
            object target,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.That(method, Is.Not.Null,
                $"Missing public method {target.GetType().Name}.{methodName}.");
            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static T Get<T>(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.That(property, Is.Not.Null,
                $"Missing public property {target.GetType().Name}.{propertyName}.");
            object value = property.GetValue(target);
            if (typeof(T) == typeof(string) && value != null &&
                value.GetType().IsEnum)
            {
                return (T)(object)value.ToString();
            }
            return (T)value;
        }

        private static void AddEventHandler(
            object target,
            string eventName,
            Delegate handler)
        {
            EventInfo eventInfo = target.GetType().GetEvent(
                eventName,
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.That(eventInfo, Is.Not.Null,
                $"Missing public event {target.GetType().Name}.{eventName}.");
            eventInfo.AddEventHandler(target, handler);
        }
    }
}
