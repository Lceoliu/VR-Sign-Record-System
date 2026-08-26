using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SignVR.Interaction.Editor.Tests
{
    public sealed class W6InteractionCaptureHostTests
    {
        private const string DriverTypeName =
            "SignVR.Interaction.CaptureHost.W6InteractionCaptureHostTestDriver, " +
            "Assembly-CSharp";

        [Test]
        public void ManifestMatchesFrozenContract()
        {
            Invoke(nameof(ManifestMatchesFrozenContract));
        }

        [Test]
        public void FrozenRegistrationRetriesExactBytesAfter409()
        {
            Invoke(nameof(FrozenRegistrationRetriesExactBytesAfter409));
        }

        [Test]
        public void ConflictRecoveryRequiresMatchingSnapshot()
        {
            Invoke(nameof(ConflictRecoveryRequiresMatchingSnapshot));
        }

        [Test]
        public void HostNotReadyAndConflictNeverRedrawPlan()
        {
            Invoke(nameof(HostNotReadyAndConflictNeverRedrawPlan));
        }

        [Test]
        public void StudyReadinessRequiresMatchingFreshParticipant()
        {
            Invoke(nameof(StudyReadinessRequiresMatchingFreshParticipant));
        }

        [Test]
        public void StudyReadinessRejectsQuestIdCaseMismatch()
        {
            Invoke(nameof(StudyReadinessRejectsQuestIdCaseMismatch));
        }

        [Test]
        public void ScheduledStartGateBlocksPreStartCapture()
        {
            Invoke(nameof(ScheduledStartGateBlocksPreStartCapture));
        }

        [Test]
        public void PresentationHandshakeUsesActualPlaybackOrigin()
        {
            Invoke(nameof(PresentationHandshakeUsesActualPlaybackOrigin));
        }

        [Test]
        public void StudyCapturePrerequisitesFailClosed()
        {
            Invoke(nameof(StudyCapturePrerequisitesFailClosed));
        }

        [Test]
        public void CompleteAndAbortSealAllLocalFiles()
        {
            Invoke(nameof(CompleteAndAbortSealAllLocalFiles));
        }

        [Test]
        public void EventSequenceAndMonotonicTimeAreStrict()
        {
            Invoke(nameof(EventSequenceAndMonotonicTimeAreStrict));
        }

        [Test]
        public void EventSinkSeamWorksWithFakeAdapter()
        {
            Invoke(nameof(EventSinkSeamWorksWithFakeAdapter));
        }

        [Test]
        public void BufferOverflowEmitsCaptureGap()
        {
            Invoke(nameof(BufferOverflowEmitsCaptureGap));
        }

        [Test]
        public void EveryPhaseFlushFlushesAllStreams()
        {
            Invoke(nameof(EveryPhaseFlushFlushesAllStreams));
        }

        [Test]
        public void RestartDiscoversPendingRun()
        {
            Invoke(nameof(RestartDiscoversPendingRun));
        }

        [Test]
        public void RestartFlagsAbnormalPartialRunForRecovery()
        {
            Invoke(nameof(RestartFlagsAbnormalPartialRunForRecovery));
        }

        [Test]
        public void PartialRunCanBeTerminalizedAfterRestart()
        {
            Invoke(nameof(PartialRunCanBeTerminalizedAfterRestart));
        }

        [Test]
        public void AtomicBackupRecoversBeforeRead()
        {
            Invoke(nameof(AtomicBackupRecoversBeforeRead));
        }

        [Test]
        public void AckRequiresWebcamAndAllFiveArtifacts()
        {
            Invoke(nameof(AckRequiresWebcamAndAllFiveArtifacts));
        }

        [Test]
        public void AckStateNeverMutatesSealedArtifacts()
        {
            Invoke(nameof(AckStateNeverMutatesSealedArtifacts));
        }

        [Test]
        public void ArtifactRetrySnapshotStaysByteIdentical()
        {
            Invoke(nameof(ArtifactRetrySnapshotStaysByteIdentical));
        }

        [Test]
        public void FrozenArtifactsEnforceHostByteLimits()
        {
            Invoke(nameof(FrozenArtifactsEnforceHostByteLimits));
        }

        [Test]
        public void W3AccurateResponsesAndTerminalBodiesMatch()
        {
            Invoke(nameof(W3AccurateResponsesAndTerminalBodiesMatch));
        }

        [Test]
        public void QuestHeartbeatContractIsExplicit()
        {
            Invoke(nameof(QuestHeartbeatContractIsExplicit));
        }

        [Test]
        public void HeartbeatLifecycleHasOneRoutineAcrossPauseResume()
        {
            Invoke(nameof(HeartbeatLifecycleHasOneRoutineAcrossPauseResume));
        }

        [Test]
        public void ReadinessGenerationRejectsStaleCallbacks()
        {
            Invoke(nameof(ReadinessGenerationRejectsStaleCallbacks));
        }

        [Test]
        public void WriterWaitsHaveShortFiniteLimits()
        {
            Invoke(nameof(WriterWaitsHaveShortFiniteLimits));
        }

        [Test]
        public void BackgroundOperationRunsOffCallingThread()
        {
            Invoke(nameof(BackgroundOperationRunsOffCallingThread));
        }

        [Test]
        public void ArtifactIntegrityWorkRunsOffCallingThread()
        {
            Invoke(nameof(ArtifactIntegrityWorkRunsOffCallingThread));
        }

        [Test]
        public void CheckpointAndSealRunSeriallyOffCallingThread()
        {
            Invoke(nameof(CheckpointAndSealRunSeriallyOffCallingThread));
        }

        [Test]
        public void CaptureBudgetsFailClosed()
        {
            Invoke(nameof(CaptureBudgetsFailClosed));
        }

        [Test]
        public void LowDiskSealRetainsPartial()
        {
            Invoke(nameof(LowDiskSealRetainsPartial));
        }

        [Test]
        public void CompletedSealRejectsEmptyCaptureWhileAbortAllowsIt()
        {
            Invoke(nameof(CompletedSealRejectsEmptyCaptureWhileAbortAllowsIt));
        }

        [Test]
        public void LifecycleShutdownAndRequestCancellationAreIdempotent()
        {
            Invoke(nameof(LifecycleShutdownAndRequestCancellationAreIdempotent));
        }

        [Test]
        public void TerminalSealArbitrationPreventsDoubleSeal()
        {
            Invoke(nameof(TerminalSealArbitrationPreventsDoubleSeal));
        }

        [Test]
        public void LifecycleTerminalizationHandoffPublishesAtomically()
        {
            Invoke(nameof(LifecycleTerminalizationHandoffPublishesAtomically));
        }

        [Test]
        public void LifecycleTerminalizationOwnsLateInitialization()
        {
            Invoke(nameof(LifecycleTerminalizationOwnsLateInitialization));
        }

        [Test]
        public void LifecycleTerminalizationOwnsSlowSeal()
        {
            Invoke(nameof(LifecycleTerminalizationOwnsSlowSeal));
        }

        [Test]
        public void ArtifactFreezeOwnerCancelsAndReapsWithoutOverlap()
        {
            Invoke(nameof(ArtifactFreezeOwnerCancelsAndReapsWithoutOverlap));
        }

        [Test]
        public void ArtifactVerifyOwnerCancelsAndReapsWithoutOverlap()
        {
            Invoke(nameof(ArtifactVerifyOwnerCancelsAndReapsWithoutOverlap));
        }

        [Test]
        public void HeartbeatLifecycleResumePolicyIsPreStartOnly()
        {
            Invoke(nameof(HeartbeatLifecycleResumePolicyIsPreStartOnly));
        }

        [Test]
        public void CompletedSealRejectsAbortThroughController()
        {
            InvokeController(nameof(CompletedSealRejectsAbortThroughController));
        }

        [Test]
        public void CaptureSamplerEnforcesTwentyHertzCadence()
        {
            InvokeController(nameof(CaptureSamplerEnforcesTwentyHertzCadence));
        }

        [Test]
        public void SetupPolicySeparatesStructureAndStudyReadiness()
        {
            Invoke(nameof(SetupPolicySeparatesStructureAndStudyReadiness));
        }

        [Test]
        public void SetupUndoGroupRollsBackOnFailure()
        {
            const string heartbeatKey =
                "SignVR.Interaction.QuestHeartbeatGeneration.v1";
            const string interactionLabAssetPath =
                "Assets/Scenes/InteractionLab.unity";
            bool heartbeatExisted = PlayerPrefs.HasKey(heartbeatKey);
            string heartbeatBefore = PlayerPrefs.GetString(
                heartbeatKey,
                string.Empty
            );
            Scene originalActive = SceneManager.GetActiveScene();
            LoadedSceneSnapshot[] originalScenes = CaptureLoadedScenes();
            string interactionLabPath = Path.GetFullPath(
                interactionLabAssetPath
            );
            Assert.That(
                File.Exists(interactionLabPath),
                Is.True,
                "InteractionLab disk scene is unavailable; refusing unsafe setup test."
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    interactionLabAssetPath),
                Is.Not.Null,
                "InteractionLab scene asset is unavailable; refusing unsafe setup test."
            );
            string diskHashBefore = HashFile(interactionLabPath);
            Scene guardScene = default(Scene);
            Scene targetScene = default(Scene);
            ulong? guardSceneHandle = null;
            ulong? targetSceneHandle = null;
            GameObject sentinel = null;
            string temporaryFolderAssetPath =
                "Assets/__W6CaptureHostSetupTest_" +
                Guid.NewGuid().ToString("N");
            string guardSceneAssetPath =
                temporaryFolderAssetPath + "/Guard.unity";
            string targetSceneAssetPath =
                temporaryFolderAssetPath + "/Target.unity";
            var cleanupFailures = new List<Exception>();
            Exception primaryFailure = null;
            try
            {
                string temporaryFolderGuid = AssetDatabase.CreateFolder(
                    "Assets",
                    Path.GetFileName(temporaryFolderAssetPath)
                );
                Assert.That(
                    string.IsNullOrEmpty(temporaryFolderGuid),
                    Is.False,
                    "Could not create an isolated guard-scene asset folder."
                );
                Assert.That(
                    AssetDatabase.CopyAsset(
                        interactionLabAssetPath,
                        guardSceneAssetPath
                    ),
                    Is.True,
                    "Could not create the saved guard-scene copy."
                );
                Assert.That(
                    AssetDatabase.CopyAsset(
                        interactionLabAssetPath,
                        targetSceneAssetPath
                    ),
                    Is.True,
                    "Could not create the saved target-scene copy."
                );
                guardScene = EditorSceneManager.OpenScene(
                    guardSceneAssetPath,
                    OpenSceneMode.Additive
                );
                guardSceneHandle = guardScene.handle.GetRawData();
                Assert.That(SceneManager.SetActiveScene(guardScene), Is.True);
                Assert.That(
                    guardScene.path,
                    Is.EqualTo(guardSceneAssetPath),
                    "Guard scene did not acquire its isolated saved asset path."
                );
                Assert.That(guardScene.isDirty, Is.False);
                ClearTestSceneRoots(guardScene);
                sentinel = new GameObject("W6 Dirty Scene Sentinel");
                sentinel.transform.localPosition = new Vector3(3f, 4f, 5f);
                sentinel.SetActive(false);
                EditorSceneManager.MarkSceneDirty(guardScene);
                Assert.That(
                    guardScene.GetRootGameObjects().Length,
                    Is.EqualTo(1),
                    "Guard copy contains objects other than its sentinel."
                );
                string guardSignatureBefore = CaptureSceneObjects(guardScene);

                try
                {
                    targetScene = EditorSceneManager.OpenScene(
                        targetSceneAssetPath,
                        OpenSceneMode.Additive
                    );
                    targetSceneHandle = targetScene.handle.GetRawData();
                    Assert.That(
                        SceneManager.SetActiveScene(targetScene),
                        Is.True
                    );
                    ClearTestSceneRoots(targetScene);
                    Assert.That(
                        targetScene.GetRootGameObjects().Length,
                        Is.EqualTo(0),
                        "Target template roots survived isolation."
                    );
                    CreateIsolatedInteractionHierarchy(
                        out GameObject runtimeAnchor,
                        out _
                    );
                    Assert.That(
                        targetScene.GetRootGameObjects().Length,
                        Is.EqualTo(1),
                        "Target copy contains objects outside the minimal W6 hierarchy."
                    );
                    Type controllerType = Type.GetType(
                        "SignVR.Interaction.CaptureHost.InteractionRunController, " +
                        "Assembly-CSharp",
                        throwOnError: true
                    );
                    Component existingController =
                        runtimeAnchor.AddComponent(controllerType);
                    Assert.That(existingController, Is.Not.Null);
                    Type setup = Type.GetType(
                        "SignVR.Editor.Interaction.W6InteractionCaptureHostSetup, " +
                        "Assembly-CSharp-Editor",
                        throwOnError: true
                    );
                    MethodInfo execute = setup.GetMethod(
                        "ConfigureSceneUnsavedForAutomation",
                        BindingFlags.Public | BindingFlags.Static
                    );
                    Assert.That(execute, Is.Not.Null);
                    string sceneBefore = CaptureW6SceneState();
                    Action failAfterWiring = () =>
                        throw new InvalidOperationException("rollback probe");
                    TargetInvocationException thrown =
                        Assert.Throws<TargetInvocationException>(() =>
                            execute.Invoke(
                                null,
                                new object[]
                                {
                                    targetScene,
                                    false,
                                    failAfterWiring
                                }
                            )
                        );
                    Assert.That(
                        thrown.InnerException,
                        Is.TypeOf<InvalidOperationException>()
                    );
                    Assert.That(
                        thrown.InnerException.Message,
                        Is.EqualTo("rollback probe")
                    );
                    Assert.That(
                        CaptureW6SceneState(),
                        Is.EqualTo(sceneBefore),
                        "Actual W6 setup failure left components or references behind."
                    );
                }
                finally
                {
                    TryEditorCleanup(
                        cleanupFailures,
                        "close isolated target scene",
                        () => CloseLoadedSceneOrThrow(
                            targetScene,
                            "isolated target scene"
                        )
                    );
                    TryEditorCleanup(
                        cleanupFailures,
                        "verify isolated target scene unloaded",
                        () => RequireSceneUnloaded(
                            targetSceneHandle,
                            "isolated target scene"
                        )
                    );
                    TryEditorCleanup(
                        cleanupFailures,
                        "restore original active scene after target setup",
                        () => RestoreActiveSceneOrThrow(originalActive)
                    );
                }

                ThrowCleanupFailuresWhenNoPrimary(
                    null,
                    cleanupFailures,
                    "Isolated target cleanup failed."
                );

                AssertOriginalScenesUnchanged(originalScenes);
                Assert.That(
                    SceneManager.GetActiveScene().handle.GetRawData(),
                    Is.EqualTo(originalActive.handle.GetRawData())
                );
                Assert.That(guardScene.isLoaded, Is.True);
                Assert.That(guardScene.isDirty, Is.True);
                Assert.That(
                    CaptureSceneObjects(guardScene),
                    Is.EqualTo(guardSignatureBefore),
                    "The pre-existing dirty guard scene changed during setup."
                );
                Assert.That(sentinel, Is.Not.Null);
                Assert.That(sentinel.name, Is.EqualTo("W6 Dirty Scene Sentinel"));
                Assert.That(sentinel.activeSelf, Is.False);
                Assert.That(
                    sentinel.transform.localPosition,
                    Is.EqualTo(new Vector3(3f, 4f, 5f))
                );
                Assert.That(
                    PlayerPrefs.HasKey(heartbeatKey),
                    Is.EqualTo(heartbeatExisted),
                    "Edit-mode setup allocated heartbeat PlayerPrefs."
                );
                Assert.That(
                    PlayerPrefs.GetString(heartbeatKey, string.Empty),
                    Is.EqualTo(heartbeatBefore),
                    "Edit-mode setup changed heartbeat generation state."
                );
                Assert.That(
                    HashFile(interactionLabPath),
                    Is.EqualTo(diskHashBefore),
                    "W6 setup test changed InteractionLab disk bytes."
                );
            }
            catch (Exception exception)
            {
                primaryFailure = exception;
                throw;
            }
            finally
            {
                TryEditorCleanup(
                    cleanupFailures,
                    "close isolated target scene",
                    () => CloseLoadedSceneOrThrow(
                        targetScene,
                        "isolated target scene"
                    )
                );
                TryEditorCleanup(
                    cleanupFailures,
                    "verify isolated target scene unloaded",
                    () => RequireSceneUnloaded(
                        targetSceneHandle,
                        "isolated target scene"
                    )
                );
                TryEditorCleanup(
                    cleanupFailures,
                    "close isolated guard scene",
                    () => CloseLoadedSceneOrThrow(
                        guardScene,
                        "isolated guard scene"
                    )
                );
                TryEditorCleanup(
                    cleanupFailures,
                    "verify isolated guard scene unloaded",
                    () => RequireSceneUnloaded(
                        guardSceneHandle,
                        "isolated guard scene"
                    )
                );
                TryEditorCleanup(
                    cleanupFailures,
                    "restore original active scene",
                    () => RestoreActiveSceneOrThrow(originalActive)
                );
                TryEditorCleanup(
                    cleanupFailures,
                    "delete isolated target scene asset",
                    () => DeleteAssetOrThrow(
                        targetSceneAssetPath,
                        "isolated target scene asset"
                    )
                );
                TryEditorCleanup(
                    cleanupFailures,
                    "delete isolated guard scene asset",
                    () => DeleteAssetOrThrow(
                        guardSceneAssetPath,
                        "isolated guard scene asset"
                    )
                );
                TryEditorCleanup(
                    cleanupFailures,
                    "delete isolated guard scene folder",
                    () => DeleteAssetOrThrow(
                        temporaryFolderAssetPath,
                        "isolated scene asset folder"
                    )
                );
                TryEditorCleanup(
                    cleanupFailures,
                    "verify isolated scene assets and metadata deleted",
                    () => RequireAssetAndMetaDeleted(
                        temporaryFolderAssetPath,
                        guardSceneAssetPath,
                        targetSceneAssetPath
                    )
                );
                TryEditorCleanup(
                    cleanupFailures,
                    "restore heartbeat PlayerPrefs value",
                    () =>
                    {
                        if (heartbeatExisted)
                        {
                            PlayerPrefs.SetString(heartbeatKey, heartbeatBefore);
                        }
                        else
                        {
                            PlayerPrefs.DeleteKey(heartbeatKey);
                        }
                    }
                );
                TryEditorCleanup(
                    cleanupFailures,
                    "save restored heartbeat PlayerPrefs",
                    PlayerPrefs.Save
                );
                ThrowCleanupFailuresWhenNoPrimary(
                    primaryFailure,
                    cleanupFailures,
                    "W6 setup test cleanup failed."
                );
            }
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    guardSceneAssetPath),
                Is.Null,
                "Isolated guard scene asset survived test cleanup."
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    targetSceneAssetPath),
                Is.Null,
                "Isolated target scene asset survived test cleanup."
            );
            Assert.That(
                AssetDatabase.IsValidFolder(temporaryFolderAssetPath),
                Is.False,
                "Isolated guard scene folder survived test cleanup."
            );
            Assert.That(
                IsSceneHandleLoaded(targetSceneHandle),
                Is.False,
                "Isolated target scene survived test cleanup."
            );
            Assert.That(
                IsSceneHandleLoaded(guardSceneHandle),
                Is.False,
                "Isolated guard scene survived test cleanup."
            );
            AssertOriginalScenesUnchanged(originalScenes);
            Assert.That(
                SceneManager.GetActiveScene().handle.GetRawData(),
                Is.EqualTo(originalActive.handle.GetRawData())
            );
            Assert.That(HashFile(interactionLabPath), Is.EqualTo(diskHashBefore));
            RequireAssetAndMetaDeleted(
                temporaryFolderAssetPath,
                guardSceneAssetPath,
                targetSceneAssetPath
            );
            Assert.That(PlayerPrefs.HasKey(heartbeatKey), Is.EqualTo(heartbeatExisted));
            Assert.That(
                PlayerPrefs.GetString(heartbeatKey, string.Empty),
                Is.EqualTo(heartbeatBefore)
            );
        }

        [Test]
        public void HeartbeatGenerationPersistsAndStrictlyIncrements()
        {
            Invoke(nameof(HeartbeatGenerationPersistsAndStrictlyIncrements));
        }

        [Test]
        public void HeartbeatAckRequiresExactFreshEcho()
        {
            Invoke(nameof(HeartbeatAckRequiresExactFreshEcho));
        }

        [Test]
        public void HeartbeatDeadlineDoesNotDriftAfterSlowResponse()
        {
            Invoke(nameof(HeartbeatDeadlineDoesNotDriftAfterSlowResponse));
        }

        [Test]
        public void LifecyclePolicyTerminalizesConsumedRuns()
        {
            Invoke(nameof(LifecyclePolicyTerminalizesConsumedRuns));
        }

        [Test]
        public void GeneratedFixtureCarriesW3StrictIdentity()
        {
            Invoke(nameof(GeneratedFixtureCarriesW3StrictIdentity));
        }

        [Test]
        public void PathsFailClosedAgainstTraversalAndIllegalIds()
        {
            Invoke(nameof(PathsFailClosedAgainstTraversalAndIllegalIds));
        }

        [Test]
        public void StudyRejectsDebugOverrides()
        {
            Invoke(nameof(StudyRejectsDebugOverrides));
        }

        [Test]
        public void SummaryKeepsAccuracyAndStuckSeparate()
        {
            Invoke(nameof(SummaryKeepsAccuracyAndStuckSeparate));
        }

        [Test]
        public void InstructionManifestReaderMapsExactArtifacts()
        {
            Invoke(nameof(InstructionManifestReaderMapsExactArtifacts));
        }

        private static void Invoke(string methodName)
        {
            Type driver = Type.GetType(DriverTypeName, throwOnError: true);
            MethodInfo method = driver.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method, Is.Not.Null, "Missing W6 scenario " + methodName);
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static void TryEditorCleanup(
            ICollection<Exception> failures,
            string actionName,
            Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    actionName + " failed.",
                    exception
                ));
            }
        }

        private static void RestoreActiveSceneOrThrow(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "The original active scene is no longer loaded."
                );
            }
            ulong targetHandle = scene.handle.GetRawData();
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                ulong activeHandle = activeScene.handle.GetRawData();
                if (activeHandle == targetHandle)
                {
                    return;
                }
            }
            if (!SceneManager.SetActiveScene(scene))
            {
                throw new InvalidOperationException(
                    "Unity refused to restore the original active scene."
                );
            }
        }

        private static void CloseLoadedSceneOrThrow(
            Scene scene,
            string description)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }
            if (!EditorSceneManager.CloseScene(scene, true))
            {
                throw new InvalidOperationException(
                    "Unity refused to close the " + description + "."
                );
            }
        }

        private static void ClearTestSceneRoots(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            if (scene.GetRootGameObjects().Length != 0)
            {
                throw new InvalidOperationException(
                    "Could not clear the saved scene template roots."
                );
            }
        }

        private static void DeleteAssetOrThrow(
            string assetPath,
            string description)
        {
            string fullPath = Path.GetFullPath(assetPath);
            bool exists = AssetDatabase.IsValidFolder(assetPath) ||
                AssetDatabase.LoadMainAssetAtPath(assetPath) != null ||
                File.Exists(fullPath) || Directory.Exists(fullPath) ||
                File.Exists(fullPath + ".meta");
            if (exists && !AssetDatabase.DeleteAsset(assetPath))
            {
                throw new IOException(
                    "Unity refused to delete the " + description + "."
                );
            }
        }

        private static void RequireAssetAndMetaDeleted(params string[] assetPaths)
        {
            foreach (string assetPath in assetPaths)
            {
                string fullPath = Path.GetFullPath(assetPath);
                if (AssetDatabase.IsValidFolder(assetPath) ||
                    AssetDatabase.LoadMainAssetAtPath(assetPath) != null ||
                    File.Exists(fullPath) || Directory.Exists(fullPath) ||
                    File.Exists(fullPath + ".meta"))
                {
                    throw new InvalidOperationException(
                        "Temporary asset or metadata survived cleanup: " +
                        assetPath
                    );
                }
            }
        }

        private static void RequireSceneUnloaded(
            ulong? sceneHandle,
            string description)
        {
            if (IsSceneHandleLoaded(sceneHandle))
            {
                throw new InvalidOperationException(
                    "The " + description + " is still loaded after cleanup."
                );
            }
        }

        private static bool IsSceneHandleLoaded(ulong? sceneHandle)
        {
            if (!sceneHandle.HasValue)
            {
                return false;
            }
            Scene scene = FindLoadedScene(sceneHandle.Value);
            return scene.IsValid() && scene.isLoaded;
        }

        private static void ThrowCleanupFailuresWhenNoPrimary(
            Exception primaryFailure,
            ICollection<Exception> cleanupFailures,
            string message)
        {
            if (cleanupFailures.Count == 0)
            {
                return;
            }
            if (primaryFailure != null)
            {
                primaryFailure.Data["W6CleanupFailures"] = string.Join(
                    Environment.NewLine,
                    cleanupFailures.Select(failure => failure.ToString())
                );
                return;
            }
            throw new AggregateException(message, cleanupFailures);
        }

        private static void InvokeController(string methodName)
        {
            Type driver = Type.GetType(
                "SignVR.Interaction.CaptureHost." +
                "W6InteractionRunControllerTestDriver, Assembly-CSharp",
                throwOnError: true
            );
            MethodInfo method = driver.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method, Is.Not.Null,
                "Missing W6 Controller scenario " + methodName);
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static string CaptureW6SceneState()
        {
            Type client = Type.GetType(
                "SignVR.Interaction.CaptureHost.InteractionHostClient, " +
                "Assembly-CSharp",
                throwOnError: true
            );
            Type controller = Type.GetType(
                "SignVR.Interaction.CaptureHost.InteractionRunController, " +
                "Assembly-CSharp",
                throwOnError: true
            );
            Type sampler = Type.GetType(
                "SignVR.Interaction.CaptureHost.InteractionCaptureSampler, " +
                "Assembly-CSharp",
                throwOnError: true
            );
            var entries = new List<string>();
            foreach (Type type in new[] { client, controller, sampler })
            {
                foreach (Component component in FindSceneComponents(type))
                {
                    var serialized = new SerializedObject(component);
                    entries.Add(
                        type.FullName + ":" + component.GetEntityId() + ":" +
                        ReferenceId(serialized, "hostClient") + ":" +
                        ReferenceId(serialized, "captureSampler") + ":" +
                        ReferenceId(serialized, "controller")
                    );
                }
            }
            return string.Join("|", entries.OrderBy(value => value));
        }

        private static IEnumerable<Component> FindSceneComponents(Type type)
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in root.GetComponentsInChildren(
                    type,
                    true
                ))
                {
                    yield return component;
                }
            }
        }

        private static string ReferenceId(
            SerializedObject serialized,
            string propertyName)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            return property == null || property.objectReferenceValue == null
                ? "0"
                : property.objectReferenceValue.GetEntityId().ToString();
        }

        private static void CreateIsolatedInteractionHierarchy(
            out GameObject runtimeAnchor,
            out GameObject captureAnchor)
        {
            var root = new GameObject("InteractionSceneRoot");
            runtimeAnchor = new GameObject("RuntimeSystemsAnchor");
            runtimeAnchor.transform.SetParent(root.transform, false);
            var anchors = new GameObject("Anchors");
            anchors.transform.SetParent(root.transform, false);
            captureAnchor = new GameObject("ExperimentCaptureAnchor");
            captureAnchor.transform.SetParent(anchors.transform, false);
        }

        private static LoadedSceneSnapshot[] CaptureLoadedScenes()
        {
            var snapshots = new List<LoadedSceneSnapshot>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                snapshots.Add(new LoadedSceneSnapshot(
                    scene.handle.GetRawData(),
                    scene.isDirty,
                    CaptureSceneObjects(scene)
                ));
            }
            return snapshots.ToArray();
        }

        private static void AssertOriginalScenesUnchanged(
            IEnumerable<LoadedSceneSnapshot> snapshots)
        {
            foreach (LoadedSceneSnapshot snapshot in snapshots)
            {
                Scene scene = FindLoadedScene(snapshot.Handle);
                Assert.That(scene.IsValid() && scene.isLoaded, Is.True,
                    "A pre-existing scene was closed or reloaded.");
                Assert.That(scene.isDirty, Is.EqualTo(snapshot.Dirty),
                    "A pre-existing scene dirty flag changed.");
                Assert.That(CaptureSceneObjects(scene),
                    Is.EqualTo(snapshot.ObjectSignature),
                    "A pre-existing scene object or value changed.");
            }
        }

        private static Scene FindLoadedScene(ulong handle)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.handle.GetRawData() == handle)
                {
                    return scene;
                }
            }
            return default(Scene);
        }

        private static string CaptureSceneObjects(Scene scene)
        {
            var values = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<
                    Transform>(true))
                {
                    Vector3 position = transform.localPosition;
                    Quaternion rotation = transform.localRotation;
                    Vector3 scale = transform.localScale;
                    values.Add(
                        transform.GetEntityId() + ":" + transform.name + ":" +
                        transform.gameObject.activeSelf + ":" +
                        position.x.ToString("R", CultureInfo.InvariantCulture) +
                        "," + position.y.ToString("R", CultureInfo.InvariantCulture) +
                        "," + position.z.ToString("R", CultureInfo.InvariantCulture) +
                        ":" + rotation.x.ToString("R", CultureInfo.InvariantCulture) +
                        "," + rotation.y.ToString("R", CultureInfo.InvariantCulture) +
                        "," + rotation.z.ToString("R", CultureInfo.InvariantCulture) +
                        "," + rotation.w.ToString("R", CultureInfo.InvariantCulture) +
                        ":" + scale.x.ToString("R", CultureInfo.InvariantCulture) +
                        "," + scale.y.ToString("R", CultureInfo.InvariantCulture) +
                        "," + scale.z.ToString("R", CultureInfo.InvariantCulture)
                    );
                }
            }
            return string.Join("|", values.OrderBy(value => value));
        }

        private static string HashFile(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (SHA256 hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }

        private sealed class LoadedSceneSnapshot
        {
            public LoadedSceneSnapshot(
                ulong handle,
                bool dirty,
                string objectSignature)
            {
                Handle = handle;
                Dirty = dirty;
                ObjectSignature = objectSignature;
            }

            public ulong Handle { get; }
            public bool Dirty { get; }
            public string ObjectSignature { get; }
        }
    }
}
