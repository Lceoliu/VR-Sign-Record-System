using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SignVR.Interaction.Editor.Tests.Orchestration
{
    public sealed class W8LocalUnityIntegrationTests
    {
        private const string CanonicalScenePath =
            "Assets/Scenes/InteractionLab.unity";
        private const string DriverTypeName =
            "SignVR.Interaction.Orchestration." +
            "W8InteractionStudyFlowTestDriver, Assembly-CSharp";

        [Test]
        public void SixPhaseHappyPathAdvancesOnlyOnActualFirstFrames()
        {
            InvokeDriver(nameof(
                SixPhaseHappyPathAdvancesOnlyOnActualFirstFrames));
        }

        [Test]
        public void ReplayUsesOneW6TokenAndCannotStartTwice()
        {
            InvokeDriver(nameof(ReplayUsesOneW6TokenAndCannotStartTwice));
        }

        [Test]
        public void GiveUpRequiresCompletedReplayAndRetainsStuckResult()
        {
            InvokeDriver(nameof(
                GiveUpRequiresCompletedReplayAndRetainsStuckResult));
        }

        [Test]
        public void WrongInputResetsW7ProgressAndSnapshotNeverGuessesProgress()
        {
            InvokeDriver(nameof(
                WrongInputResetsW7ProgressAndSnapshotNeverGuessesProgress));
        }

        [Test]
        public void ValidationErrorsResynchronizeBeforeReplayGiveUp()
        {
            InvokeDriver(nameof(
                ValidationErrorsResynchronizeBeforeReplayGiveUp));
        }

        [Test]
        public void AbortEndsW5BeforeW6AndWaitsForTerminalBeforeW7Reset()
        {
            InvokeDriver(nameof(
                AbortEndsW5BeforeW6AndWaitsForTerminalBeforeW7Reset));
        }

        [Test]
        public void TerminalAdapterFailuresStillConvergeToPreStart()
        {
            InvokeDriver(nameof(
                TerminalAdapterFailuresStillConvergeToPreStart));
        }

        [Test]
        public void TerminalAdaptersRunOnceWhileW6ResetWaits()
        {
            InvokeDriver(nameof(TerminalAdaptersRunOnceWhileW6ResetWaits));
        }

        [Test]
        public void TerminalResetExceptionRetriesWithoutRepeatingAdapters()
        {
            InvokeDriver(nameof(
                TerminalResetExceptionRetriesWithoutRepeatingAdapters));
        }

        [Test]
        public void SuspendUsesTheSameSafeAbortAndResumeResubscribesOnce()
        {
            InvokeDriver(nameof(
                SuspendUsesTheSameSafeAbortAndResumeResubscribesOnce));
        }

        [Test]
        public void AbortFailureRetriesW5WithoutRepeatingItsCompletedWork()
        {
            InvokeDriver(nameof(
                AbortFailureRetriesW5WithoutRepeatingItsCompletedWork));
        }

        [Test]
        public void SuspendFailureStillDisablesAndResumeRetriesCleanup()
        {
            InvokeDriver(nameof(
                SuspendFailureStillDisablesAndResumeRetriesCleanup));
        }

        [Test]
        public void LifecycleAbortRetryIsBackedOffAndEventuallyConverges()
        {
            InvokeDriver(nameof(
                LifecycleAbortRetryIsBackedOffAndEventuallyConverges));
        }

        [Test]
        public void AcceptedAbortRetriesOnlyFailedTaskDisable()
        {
            InvokeDriver(nameof(AcceptedAbortRetriesOnlyFailedTaskDisable));
        }

        [Test]
        public void SuspendedAcceptedAbortRetainsFailedDisableRetry()
        {
            InvokeDriver(nameof(
                SuspendedAcceptedAbortRetainsFailedDisableRetry));
        }

        [Test]
        public void TerminalCleanupTakesOverPersistentlyFailedDisable()
        {
            InvokeDriver(nameof(
                TerminalCleanupTakesOverPersistentlyFailedDisable));
        }

        [Test]
        public void SuspendedPendingAbortDisposeRetriesAndDetaches()
        {
            InvokeDriver(nameof(
                SuspendedPendingAbortDisposeRetriesAndDetaches));
        }

        [Test]
        public void CleanupFailureThenReconfigureUsesFreshAttemptState()
        {
            InvokeDriver(nameof(
                CleanupFailureThenReconfigureUsesFreshAttemptState));
        }

        [Test]
        public void TerminalWarningBufferIsExactUnicodeSafeAndKeepsNewest()
        {
            InvokeDriver(nameof(
                TerminalWarningBufferIsExactUnicodeSafeAndKeepsNewest));
        }

        [Test]
        public void TerminalWarningBufferBoundsHugeCanonicalRetention()
        {
            InvokeDriver(nameof(
                TerminalWarningBufferBoundsHugeCanonicalRetention));
        }

        [Test]
        public void PresentationReplacementRejectsOnlyUnsettledCleanup()
        {
            InvokeDriver(nameof(
                PresentationReplacementRejectsOnlyUnsettledCleanup));
        }

        [Test]
        public void DuplicateAndStalePresentationCallbacksAreExactlyOnce()
        {
            InvokeDriver(nameof(
                DuplicateAndStalePresentationCallbacksAreExactlyOnce));
        }

        [Test]
        public void ReconfigureDetachesOldPublishersAndRejectsActiveReplacement()
        {
            InvokeDriver(nameof(
                ReconfigureDetachesOldPublishersAndRejectsActiveReplacement));
        }

        [Test]
        public void ControllerReconfigureFailurePreservesDependenciesAndSubscriptions()
        {
            InvokeDriver(nameof(
                ControllerReconfigureFailurePreservesDependenciesAndSubscriptions));
        }

        [Test]
        public void SlowReadinessResponseCannotBeStaledByPolling()
        {
            InvokeDriver(nameof(
                SlowReadinessResponseCannotBeStaledByPolling));
        }

        [Test]
        public void StartAndInitializationFailuresDoNotInventAnotherRunPlan()
        {
            InvokeDriver(nameof(
                StartAndInitializationFailuresDoNotInventAnotherRunPlan));
        }

        [Test]
        public void PresentationFaultUsesAbortPathWithoutAcknowledgingARequest()
        {
            InvokeDriver(nameof(
                PresentationFaultUsesAbortPathWithoutAcknowledgingARequest));
        }

        [Test]
        public void SuccessfulTaskMayEndDuringFirstOrReplayPlayback()
        {
            InvokeDriver(nameof(
                SuccessfulTaskMayEndDuringFirstOrReplayPlayback));
        }

        [Test]
        public void IdentityIsPreStartArmedAndLocksAfterConsumption()
        {
            InvokeDriver(nameof(
                IdentityIsPreStartArmedAndLocksAfterConsumption));
        }

        [Test]
        public void ArmedIdentityInputsCannotDivergeFromW6Identity()
        {
            InvokeDriver(nameof(
                ArmedIdentityInputsCannotDivergeFromW6Identity));
        }

        [Test]
        public void FixedStudyModeLeavesRuntimeTrackingOriginOwnedByXrRuntime()
        {
            InvokeDriver(nameof(
                FixedStudyModeLeavesRuntimeTrackingOriginOwnedByXrRuntime));
        }

        [Test]
        public void HeadLockedStudyUiInheritsHmdPoseWithoutLateWorldCopy()
        {
            InvokeDriver(nameof(
                HeadLockedStudyUiInheritsHmdPoseWithoutLateWorldCopy));
        }

        [Test]
        public void AutomaticHostIdentityIsAdoptedWithoutVrTextEntry()
        {
            InvokeDriver(nameof(
                AutomaticHostIdentityIsAdoptedWithoutVrTextEntry));
        }

        [Test]
        public void AutomaticIdentityDefersReadinessRepollUntilNextUpdate()
        {
            InvokeDriver(nameof(
                AutomaticIdentityDefersReadinessRepollUntilNextUpdate));
        }

        [Test]
        public void AutomaticIdentityHidesVrTextInputsAndStartIsSingleAction()
        {
            InvokeDriver(nameof(
                AutomaticIdentityHidesVrTextInputsAndStartIsSingleAction));
        }

        [Test]
        public void ManifestFailureRetryAndDisableCancelAreSafe()
        {
            InvokeDriver(nameof(
                ManifestFailureRetryAndDisableCancelAreSafe));
        }

        [Test]
        public void RealInstructionControlsRouteReplayThroughW8Sink()
        {
            InvokeDriver(nameof(
                RealInstructionControlsRouteReplayThroughW8Sink));
        }

        [Test]
        public void CaptureBindingRequiresRealMatchedMetaSourcesAndProbes()
        {
            InvokeDriver(nameof(
                CaptureBindingRequiresRealMatchedMetaSourcesAndProbes));
        }

        [Test]
        public void SetupSourceContractProvesRealRoutingAndRuntimeNUnitBoundary()
        {
            InvokeEditorSetup("ValidateSourceContractForAutomation");
        }

        [Test]
        public void SetupSceneCopyIsTransactionalValidatedAndIdempotent()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)
                ?.FullName ?? throw new InvalidOperationException(
                    "Unity project root is unavailable."
                );
            string canonicalDiskPath = Path.GetFullPath(Path.Combine(
                projectRoot,
                CanonicalScenePath.Replace('/', Path.DirectorySeparatorChar)
            ));
            string canonicalHash = HashFile(canonicalDiskPath);
            LoadedSceneSnapshot[] loadedBefore = CaptureLoadedScenes();
            Scene activeBefore = SceneManager.GetActiveScene();
            string folderName = "__W7InteractionPhaseAdaptersTests_" +
                Guid.NewGuid().ToString("N");
            string folderPath = "Assets/" + folderName;
            string copyPath = folderPath + "/InteractionLab_W7Test.unity";
            string copyDiskPath = Path.GetFullPath(Path.Combine(
                projectRoot,
                copyPath.Replace('/', Path.DirectorySeparatorChar)
            ));
            Scene copy = default;
            Exception testFailure = null;

            try
            {
                string folderGuid = AssetDatabase.CreateFolder(
                    "Assets",
                    folderName
                );
                Assert.That(folderGuid, Is.Not.Empty);
                Assert.That(
                    AssetDatabase.CopyAsset(CanonicalScenePath, copyPath),
                    Is.True
                );
                AssetDatabase.ImportAsset(
                    copyPath,
                    ImportAssetOptions.ForceSynchronousImport
                );
                copy = EditorSceneManager.OpenScene(
                    copyPath,
                    OpenSceneMode.Additive
                );
                Assert.That(copy.IsValid() && copy.isLoaded, Is.True);
                Assert.That(SceneManager.SetActiveScene(copy), Is.True);

                InvokeEditorType(
                    "SignVR.Editor.Interaction." +
                        "W6InteractionCaptureHostSetup, Assembly-CSharp-Editor",
                    "ConfigureSceneUnsavedForAutomation",
                    copy,
                    false,
                    null
                );
                InvokeEditorType(
                    "SignVR.Editor.Interaction." +
                        "W7InteractionPhaseAdaptersSetup, Assembly-CSharp-Editor",
                    "MarkTestOwnedSceneForAutomation",
                    copy
                );
                InvokeEditorType(
                    "SignVR.Editor.Interaction." +
                        "W7InteractionPhaseAdaptersSetup, Assembly-CSharp-Editor",
                    "SetupAndValidateTestOwnedSceneWithoutSaving",
                    copy
                );

                StripW8OwnedState(copy);
                Assert.That(
                    EditorSceneManager.SaveScene(copy, copyPath, false),
                    Is.True,
                    "Only the test-owned copy may be saved as the clean " +
                        "W6/W7 rollback baseline."
                );
                Assert.That(copy.isDirty, Is.False);
                string baseline = CaptureW8Signature(copy);
                AssertConfigureFailureRollsBack(
                    copy,
                    baseline,
                    expectedDirty: false
                );

                InvokeEditorSetup(
                    "ConfigureSceneUnsavedForAutomation",
                    copy,
                    false,
                    null
                );
                InvokeEditorSetup(
                    "ValidateSceneStructureForAutomation",
                    copy,
                    false
                );
                AssertConfiguredScene(copy);
                string[] configuredEntries = CaptureW8Entries(copy);
                string configured = CaptureW8Signature(copy);

                Assert.That(
                    EditorSceneManager.SaveScene(copy, copyPath, false),
                    Is.True,
                    "The test-owned integrated copy must serialize before " +
                        "the persisted-state gate."
                );
                Assert.That(EditorSceneManager.CloseScene(copy, true), Is.True);
                copy = EditorSceneManager.OpenScene(
                    copyPath,
                    OpenSceneMode.Additive
                );
                Assert.That(copy.IsValid() && copy.isLoaded, Is.True);
                Assert.That(SceneManager.SetActiveScene(copy), Is.True);
                InvokeEditorSetup(
                    "ValidateSceneStructureForAutomation",
                    copy,
                    false
                );
                AssertConfiguredScene(copy);
                Assert.That(
                    CaptureW8Entries(copy),
                    Is.EqualTo(configuredEntries),
                    "The saved and reopened test-owned scene must preserve " +
                        "all W8 wiring, skeletons, and probes."
                );

                InvokeEditorSetup(
                    "ConfigureSceneUnsavedForAutomation",
                    copy,
                    false,
                    null
                );
                InvokeEditorSetup(
                    "ValidateSceneStructureForAutomation",
                    copy,
                    false
                );
                Assert.That(
                    CaptureW8Entries(copy),
                    Is.EqualTo(configuredEntries),
                    "A successful W8 rerun must be structurally idempotent."
                );

                MutateExistingW8Ui(copy);
                string mutated = CaptureW8Signature(copy);
                Assert.That(mutated, Is.Not.EqualTo(configured));
                AssertConfigureFailureRollsBack(copy, mutated);
            }
            catch (Exception exception)
            {
                testFailure = exception;
            }

            var cleanupFailures = new List<Exception>();
            TryCleanup(cleanupFailures, "close copied scene", () =>
            {
                if (copy.IsValid() && copy.isLoaded)
                {
                    if (!EditorSceneManager.CloseScene(copy, true))
                    {
                        throw new InvalidOperationException(
                            "Unity rejected copied-scene close."
                        );
                    }
                }
            });
            TryCleanup(cleanupFailures, "restore active scene", () =>
            {
                if (activeBefore.IsValid() && activeBefore.isLoaded &&
                    SceneManager.GetActiveScene().handle != activeBefore.handle)
                {
                    if (!SceneManager.SetActiveScene(activeBefore))
                    {
                        throw new InvalidOperationException(
                            "Unity rejected active-scene restoration."
                        );
                    }
                }
            });
            TryCleanup(cleanupFailures, "delete copied scene asset", () =>
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(copyPath) != null)
                {
                    if (!AssetDatabase.DeleteAsset(copyPath))
                    {
                        throw new InvalidOperationException(
                            "Unity rejected copied-scene asset deletion."
                        );
                    }
                }
            });
            TryCleanup(cleanupFailures, "delete copied scene folder", () =>
            {
                if (AssetDatabase.IsValidFolder(folderPath))
                {
                    if (!AssetDatabase.DeleteAsset(folderPath))
                    {
                        throw new InvalidOperationException(
                            "Unity rejected copied-scene folder deletion."
                        );
                    }
                }
            });
            TryCleanup(cleanupFailures, "refresh AssetDatabase", () =>
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            });
            TryCleanup(cleanupFailures, "verify copied assets were removed", () =>
            {
                if (File.Exists(copyDiskPath) ||
                    File.Exists(copyDiskPath + ".meta") ||
                    Directory.Exists(Path.GetDirectoryName(copyDiskPath)))
                {
                    throw new InvalidOperationException(
                        "Copied scene files or their temporary folder remain."
                    );
                }
            });
            TryCleanup(cleanupFailures, "verify loaded-scene restoration", () =>
            {
                AssertLoadedScenesPreserved(loadedBefore, activeBefore);
            });
            TryCleanup(cleanupFailures, "verify canonical scene hash", () =>
            {
                if (!string.Equals(
                        HashFile(canonicalDiskPath),
                        canonicalHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The canonical InteractionLab file is no longer " +
                        "byte-identical."
                    );
                }
            });

            if (testFailure != null && cleanupFailures.Count == 0)
            {
                ExceptionDispatchInfo.Capture(testFailure).Throw();
            }
            if (testFailure != null)
            {
                cleanupFailures.Insert(0, testFailure);
            }
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "W8 copied-scene test or its independent cleanup failed.",
                    cleanupFailures
                );
            }
        }

        private static void TryCleanup(
            ICollection<Exception> failures,
            string operation,
            Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    "W8 fixture could not " + operation + ".",
                    exception
                ));
            }
        }

        private static void AssertConfigureFailureRollsBack(
            Scene scene,
            string expectedSignature,
            bool? expectedDirty = null)
        {
            string[] expectedEntries = CaptureW8Entries(scene);
            var failure = new InvalidOperationException(
                "forced_w8_after_wiring_failure"
            );
            Action afterWiring = () => throw failure;
            TargetInvocationException thrown = Assert.Throws<
                TargetInvocationException>(() => InvokeEditorSetup(
                    "ConfigureSceneUnsavedForAutomation",
                    scene,
                    false,
                    afterWiring
                ));
            Assert.That(thrown.InnerException, Is.SameAs(failure));
            string[] actualEntries = CaptureW8Entries(scene);
            int firstDifference = -1;
            int compared = Math.Min(
                expectedEntries.Length,
                actualEntries.Length
            );
            for (int index = 0; index < compared; index++)
            {
                if (!string.Equals(
                        expectedEntries[index],
                        actualEntries[index],
                        StringComparison.Ordinal))
                {
                    firstDifference = index;
                    break;
                }
            }
            if (firstDifference < 0 &&
                expectedEntries.Length != actualEntries.Length)
            {
                firstDifference = compared;
            }
            string differenceIdentity = firstDifference >= 0 &&
                firstDifference < expectedEntries.Length
                ? string.Join(
                    " | ",
                    expectedEntries[firstDifference].Split('|').Take(2)
                )
                : "signature length";
            Assert.That(
                actualEntries,
                Is.EqualTo(expectedEntries),
                "Actual W8 setup additions, references, and UI mutations " +
                    "must all roll back. First differing entry: " +
                    differenceIdentity
            );
            Assert.That(
                CaptureW8Signature(scene),
                Is.EqualTo(expectedSignature)
            );
            if (expectedDirty.HasValue)
            {
                Assert.That(
                    scene.isDirty,
                    Is.EqualTo(expectedDirty.Value),
                    "A failed W8 transaction must restore the scene's prior " +
                        "dirty state."
                );
            }
        }

        private static void StripW8OwnedState(Scene scene)
        {
            string[] removableTypes =
            {
                "SignVR.Interaction.Orchestration.InteractionStudyFlowControls",
                "SignVR.Interaction.Orchestration.InteractionStudyFlowController",
                "SignVR.Interaction.Orchestration.InteractionStudyCaptureBinding",
                "SignVR.Interaction.CaptureHost.InteractionObjectStateProbe",
                "OVRSkeleton"
            };
            foreach (string typeName in removableTypes)
            {
                Component[] values = AllComponents(scene).Where(component =>
                    string.Equals(
                        component.GetType().FullName,
                        typeName,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        component.GetType().Name,
                        typeName,
                        StringComparison.Ordinal)).ToArray();
                foreach (Component value in values)
                {
                    UnityEngine.Object.DestroyImmediate(value);
                }
            }

            Transform surface = FindNamedTransform(scene, "W8StudyStartSurface");
            if (surface != null)
            {
                UnityEngine.Object.DestroyImmediate(surface.gameObject);
            }

            Component instructionControls = FindSingleComponent(
                scene,
                "SignVR.Interaction.Presentation.InteractionInstructionControls"
            );
            SetSerializedBooleanWithoutUndo(
                instructionControls,
                "requireCommandSink",
                false
            );

            Component sampler = FindSingleComponent(
                scene,
                "SignVR.Interaction.CaptureHost.InteractionCaptureSampler"
            );
            var serialized = new SerializedObject(sampler);
            serialized.Update();
            foreach (string field in new[]
                     {
                         "hmd",
                         "leftHand",
                         "leftSkeleton",
                         "rightHand",
                         "rightSkeleton"
                     })
            {
                SerializedProperty property = serialized.FindProperty(field);
                Assert.That(property, Is.Not.Null, field);
                property.objectReferenceValue = null;
            }
            SerializedProperty probes = serialized.FindProperty(
                "objectStateProbes"
            );
            Assert.That(probes, Is.Not.Null);
            probes.arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssertConfiguredScene(Scene scene)
        {
            Component[] hands = AllComponents(scene).Where(component =>
                component.GetType().Name == "OVRHand").ToArray();
            Component[] skeletons = AllComponents(scene).Where(component =>
                component.GetType().Name == "OVRSkeleton").ToArray();
            Assert.That(hands.Length, Is.EqualTo(2));
            Assert.That(skeletons.Length, Is.EqualTo(2));
            var providerTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (Component hand in hands)
            {
                Component[] localSkeletons = hand.GetComponents<Component>()
                    .Where(component => component.GetType().Name ==
                        "OVRSkeleton").ToArray();
                Assert.That(localSkeletons.Length, Is.EqualTo(1));
                Type provider = hand.GetType().GetInterfaces().Single(
                    candidate => candidate.Name ==
                        "IOVRSkeletonDataProvider"
                );
                object providerType = provider.GetMethod("GetSkeletonType")
                    ?.Invoke(hand, null);
                object skeletonType = localSkeletons[0].GetType().GetMethod(
                    "GetSkeletonType",
                    BindingFlags.Public | BindingFlags.Instance
                )?.Invoke(localSkeletons[0], null);
                Assert.That(providerType, Is.Not.Null);
                Assert.That(skeletonType, Is.EqualTo(providerType));
                providerTypes.Add(providerType.ToString());
            }
            Assert.That(providerTypes.Count, Is.EqualTo(2));
            Assert.That(providerTypes.Any(value => value.Contains("Left")),
                Is.True);
            Assert.That(providerTypes.Any(value => value.Contains("Right")),
                Is.True);

            Component[] bindings = AllComponents(scene).Where(component =>
                component.GetType().FullName ==
                    "SignVR.Interaction.PhaseAdapters." +
                    "InteractionTargetBinding").ToArray();
            Component[] probes = AllComponents(scene).Where(component =>
                component.GetType().FullName ==
                    "SignVR.Interaction.CaptureHost." +
                    "InteractionObjectStateProbe").ToArray();
            Assert.That(bindings.Length, Is.GreaterThan(0));
            Assert.That(probes.Length, Is.EqualTo(bindings.Length));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Component binding in bindings)
            {
                string targetId = (string)binding.GetType().GetProperty(
                    "TargetId")?.GetValue(binding);
                Component[] local = binding.GetComponents<Component>().Where(
                    component => component.GetType().FullName ==
                        "SignVR.Interaction.CaptureHost." +
                        "InteractionObjectStateProbe").ToArray();
                Assert.That(local.Length, Is.EqualTo(1), targetId);
                string objectId = (string)local[0].GetType().GetProperty(
                    "ObjectId")?.GetValue(local[0]);
                Assert.That(objectId, Is.EqualTo(targetId));
                Assert.That(ids.Add(objectId), Is.True);
            }

            Component flow = RequireOneType(
                scene,
                "SignVR.Interaction.Orchestration." +
                    "InteractionStudyFlowController"
            );
            Component capture = RequireOneType(
                scene,
                "SignVR.Interaction.Orchestration." +
                    "InteractionStudyCaptureBinding"
            );
            Component controls = RequireOneType(
                scene,
                "SignVR.Interaction.Orchestration." +
                    "InteractionStudyFlowControls"
            );
            foreach (string property in new[]
                     {
                         "RunController",
                         "PresentationController",
                         "PhaseCoordinator",
                         "CaptureBinding"
                     })
            {
                AssertPublicReference(flow, property);
            }
            foreach (string property in new[]
                     {
                         "Sampler",
                         "Hmd",
                         "LeftHand",
                         "LeftSkeleton",
                         "RightHand",
                         "RightSkeleton"
                     })
            {
                AssertPublicReference(capture, property);
            }
            foreach (string property in new[]
                     {
                         "FlowController",
                         "InstructionControls",
                         "PreStartRoot",
                         "StartButton",
                         "ParticipantIdInput",
                         "BuildIdentityInput",
                         "ApplyIdentityButton",
                         "StatusLabel",
                         "ProgressLabel"
                     })
            {
                AssertPublicReference(controls, property);
            }

            Transform startSurface = FindNamedTransform(
                scene,
                "W8StudyStartSurface"
            );
            Assert.That(startSurface, Is.Not.Null);
            foreach (string child in new[]
                     {
                         "Start",
                         "ParticipantId",
                         "BuildIdentity",
                         "ApplyIdentity",
                         "Status",
                         "Progress"
                     })
            {
                Assert.That(startSurface.Find(child), Is.Not.Null, child);
            }

            Component realControls = FindSingleComponent(
                scene,
                "SignVR.Interaction.Presentation.InteractionInstructionControls"
            );
            Assert.That(
                (bool)realControls.GetType().GetProperty(
                    "RequireCommandSink")?.GetValue(realControls),
                Is.True
            );
        }

        private static void MutateExistingW8Ui(Scene scene)
        {
            Transform surface = FindNamedTransform(scene, "W8StudyStartSurface");
            Assert.That(surface, Is.Not.Null);

            Component background = FindComponentOn(
                surface.Find("Background").gameObject,
                "UnityEngine.UI.Image"
            );
            background.GetType().GetProperty("color")?.SetValue(
                background,
                new Color(1f, 0f, 1f, 0.31f)
            );

            Transform start = surface.Find("Start");
            Component startImage = FindComponentOn(
                start.gameObject,
                "UnityEngine.UI.Image"
            );
            startImage.GetType().GetProperty("color")?.SetValue(
                startImage,
                new Color(0.91f, 0.07f, 0.63f, 0.42f)
            );
            Component startButton = FindComponentOn(
                start.gameObject,
                "UnityEngine.UI.Button"
            );
            startButton.GetType().GetProperty("targetGraphic")?.SetValue(
                startButton,
                null
            );
            start.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(137f, -311f);

            Component input = FindComponentOn(
                surface.Find("ParticipantId").gameObject,
                "TMPro.TMP_InputField"
            );
            input.GetType().GetProperty("characterLimit")?.SetValue(input, 7);

            Component status = FindComponentOn(
                surface.Find("Status").gameObject,
                "TMPro.TextMeshProUGUI"
            );
            status.GetType().GetProperty("text")?.SetValue(
                status,
                "__W8_EXISTING_UI_SENTINEL__"
            );
        }

        private static string CaptureW8Signature(Scene scene)
        {
            return HashBytes(Encoding.UTF8.GetBytes(string.Join(
                "\n",
                CaptureW8Entries(scene)
            )));
        }

        private static string[] CaptureW8Entries(Scene scene)
        {
            var relevantTypes = new HashSet<string>(StringComparer.Ordinal)
            {
                "OVRHand",
                "OVRSkeleton",
                "SignVR.Interaction.CaptureHost.InteractionCaptureSampler",
                "SignVR.Interaction.CaptureHost.InteractionObjectStateProbe",
                "SignVR.Interaction.CaptureHost.InteractionRunController",
                "SignVR.Interaction.Presentation.InteractionInstructionControls",
                "SignVR.Interaction.Orchestration.InteractionStudyFlowController",
                "SignVR.Interaction.Orchestration.InteractionStudyCaptureBinding",
                "SignVR.Interaction.Orchestration.InteractionStudyFlowControls"
            };
            Transform surface = FindNamedTransform(scene, "W8StudyStartSurface");
            var entries = new List<string>();
            foreach (Component component in AllComponents(scene))
            {
                string fullName = component.GetType().FullName ??
                    component.GetType().Name;
                bool inSurface = surface != null &&
                    (component.transform == surface ||
                     component.transform.IsChildOf(surface));
                if (!inSurface && !relevantTypes.Contains(fullName) &&
                    !relevantTypes.Contains(component.GetType().Name))
                {
                    continue;
                }
                string serialized = EditorJsonUtility.ToJson(
                    component,
                    false
                );
                // Unity's editor JSON represents object references with
                // process-local instance IDs. Those necessarily change after
                // close/reopen even when the serialized scene wiring is
                // identical, so exclude only that volatile transport value.
                serialized = Regex.Replace(
                    serialized,
                    "\\\"instanceID\\\":-?[0-9]+",
                    "\\\"instanceID\\\":0"
                ).Replace("\u200B", string.Empty);
                entries.Add(
                    HierarchyPath(component.transform) + "|" + fullName +
                    "|" + serialized
                );
            }
            if (surface != null)
            {
                foreach (Transform value in surface.GetComponentsInChildren<
                             Transform>(true))
                {
                    entries.Add(
                        HierarchyPath(value) + "|GameObject|" +
                        value.gameObject.layer + "|" +
                        value.gameObject.activeSelf
                    );
                }
            }
            entries.Sort(StringComparer.Ordinal);
            return entries.ToArray();
        }

        private static IEnumerable<Component> AllComponents(Scene scene)
        {
            return scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<Component>(true)).Where(
                component => component != null
            );
        }

        private static Component RequireOneType(Scene scene, string fullName)
        {
            Component[] values = AllComponents(scene).Where(component =>
                component.GetType().FullName == fullName).ToArray();
            Assert.That(values.Length, Is.EqualTo(1), fullName);
            return values[0];
        }

        private static Component FindSingleComponent(
            Scene scene,
            string fullName)
        {
            return RequireOneType(scene, fullName);
        }

        private static Component FindComponentOn(
            GameObject gameObject,
            string fullName)
        {
            Component value = gameObject.GetComponents<Component>()
                .SingleOrDefault(component =>
                    component.GetType().FullName == fullName);
            return value ?? throw new MissingComponentException(
                gameObject.name + " lacks " + fullName
            );
        }

        private static void AssertPublicReference(
            Component component,
            string propertyName)
        {
            PropertyInfo property = component.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.That(property, Is.Not.Null, propertyName);
            Assert.That(property.GetValue(component), Is.Not.Null, propertyName);
        }

        private static Transform FindNamedTransform(Scene scene, string name)
        {
            Transform[] values = scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<Transform>(true)).Where(value =>
                value.name == name).ToArray();
            Assert.That(values.Length, Is.LessThanOrEqualTo(1), name);
            return values.SingleOrDefault();
        }

        private static void SetSerializedBooleanWithoutUndo(
            UnityEngine.Object target,
            string propertyName,
            bool value)
        {
            var serialized = new SerializedObject(target);
            serialized.Update();
            SerializedProperty property = serialized.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string HierarchyPath(Transform value)
        {
            var parts = new Stack<string>();
            Transform current = value;
            while (current != null)
            {
                parts.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }

        private static LoadedSceneSnapshot[] CaptureLoadedScenes()
        {
            var result = new LoadedSceneSnapshot[SceneManager.sceneCount];
            for (int index = 0; index < result.Length; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                result[index] = new LoadedSceneSnapshot(
                    scene,
                    scene.path,
                    scene.name,
                    scene.isDirty
                );
            }
            return result;
        }

        private static void AssertLoadedScenesPreserved(
            IReadOnlyList<LoadedSceneSnapshot> expected,
            Scene activeBefore)
        {
            Assert.That(SceneManager.sceneCount, Is.EqualTo(expected.Count));
            foreach (LoadedSceneSnapshot snapshot in expected)
            {
                Scene scene = snapshot.Scene;
                Assert.That(scene.IsValid() && scene.isLoaded, Is.True,
                    snapshot.Name);
                Assert.That(scene.path, Is.EqualTo(snapshot.Path));
                Assert.That(scene.name, Is.EqualTo(snapshot.Name));
                Assert.That(scene.isDirty, Is.EqualTo(snapshot.WasDirty));
            }
            if (activeBefore.IsValid())
            {
                Assert.That(
                    SceneManager.GetActiveScene().handle,
                    Is.EqualTo(activeBefore.handle)
                );
            }
        }

        private static string HashFile(string path)
        {
            return HashBytes(File.ReadAllBytes(path));
        }

        private static string HashBytes(byte[] value)
        {
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(value))
                .Replace("-", string.Empty);
        }

        private static void InvokeDriver(string methodName)
        {
            Type driver = Type.GetType(DriverTypeName, throwOnError: true);
            MethodInfo method = driver.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            ) ?? throw new MissingMethodException(driver.FullName, methodName);
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(
                    exception.InnerException ?? exception
                ).Throw();
                throw;
            }
        }

        private static void InvokeEditorSetup(
            string methodName,
            params object[] arguments)
        {
            InvokeEditorType(
                "SignVR.Editor.Interaction.W8LocalUnityIntegrationSetup, " +
                "Assembly-CSharp-Editor",
                methodName,
                arguments
            );
        }

        private static void InvokeEditorType(
            string typeName,
            string methodName,
            params object[] arguments)
        {
            Type setup = Type.GetType(typeName, throwOnError: true);
            MethodInfo method = setup.GetMethods(
                    BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(candidate =>
                    candidate.Name == methodName &&
                    candidate.GetParameters().Length == arguments.Length) ??
                throw new MissingMethodException(setup.FullName, methodName);
            method.Invoke(null, arguments);
        }

        private sealed class LoadedSceneSnapshot
        {
            public LoadedSceneSnapshot(
                Scene scene,
                string path,
                string name,
                bool wasDirty)
            {
                Scene = scene;
                Path = path;
                Name = name;
                WasDirty = wasDirty;
            }

            public Scene Scene { get; }
            public string Path { get; }
            public string Name { get; }
            public bool WasDirty { get; }
        }
    }
}
