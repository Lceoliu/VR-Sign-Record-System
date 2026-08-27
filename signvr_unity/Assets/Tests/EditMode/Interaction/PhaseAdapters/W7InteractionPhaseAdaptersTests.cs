using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests
{
    public sealed class W7InteractionPhaseAdaptersTests
    {
        private const string RuntimeAssembly = "Assembly-CSharp";
        private const string AdapterNamespace =
            "SignVR.Interaction.PhaseAdapters.";
        private const string ValidatorTypeName =
            "SignVR.Editor.Interaction.W7InteractionPhaseAdaptersSetup, " +
            "Assembly-CSharp-Editor";
        private const string InteractionSceneAssetPath =
            "Assets/Scenes/InteractionLab.unity";
        private const string CleanupFailuresDataKey =
            "W7FixtureCleanupFailures";
        private const string TestSceneMarkerName =
            "__W7_TEST_OWNED_INTERACTION_SCENE__";

        [Test]
        public void AllSixAdaptersExposeAuditableLifecycleAndInputApi()
        {
            string[] adapterNames =
            {
                "PhaseOneInteractionAdapter",
                "PhaseTwoInteractionAdapter",
                "PhaseThreeInteractionAdapter",
                "PhaseFourInteractionAdapter",
                "PhaseFiveInteractionAdapter",
                "PhaseSixInteractionAdapter"
            };

            foreach (string adapterName in adapterNames)
            {
                Type type = Type.GetType(
                    AdapterNamespace + adapterName + ", " + RuntimeAssembly,
                    throwOnError: true
                );
                AssertPublicInstanceMethod(type, "Configure");
                AssertPublicInstanceMethod(type, "Reset");
                AssertPublicInstanceMethod(type, "Enable");
                AssertPublicInstanceMethod(type, "Disable");
                AssertPublicInstanceMethod(type, "AcceptInput");
                AssertPublicInstanceMethod(type, "AcceptTarget");

                Assert.That(type.GetEvent("InputAccepted"), Is.Not.Null);
                Assert.That(type.GetEvent("InteractionError"), Is.Not.Null);
                Assert.That(type.GetEvent("TaskProgressReset"), Is.Not.Null);
                Assert.That(type.GetEvent("Completed"), Is.Not.Null);
                Assert.That(type.GetEvent("ResetPerformed"), Is.Not.Null);
            }

            Type backspace = RuntimeType(
                "InteractionPasswordBackspaceBinding"
            );
            AssertPublicInstanceMethod(backspace, "Configure");
            AssertPublicInstanceMethod(backspace, "AcceptInput");
            AssertPublicInstanceMethod(backspace, "Poke");
            AssertPublicInstanceMethod(backspace, "Trigger");
            Assert.That(
                backspace.GetInterface("IInteractionTriggerInput"),
                Is.Not.Null
            );

            Type targetBinding = RuntimeType("InteractionTargetBinding");
            Assert.That(
                targetBinding.GetMethod(
                    "RestoreAuthoredStateForTeardown",
                    BindingFlags.Public | BindingFlags.Instance
                ),
                Is.Null,
                "Runtime code must not expose a teardown half-state seam."
            );
            Assert.That(
                targetBinding.GetMethod(
                    "OnDestroy",
                    BindingFlags.NonPublic | BindingFlags.Instance
                ),
                Is.Not.Null,
                "Authored-state restoration must remain private and atomic " +
                "with component destruction."
            );
        }

        [Test]
        public void FixtureCleanupRunsEveryRecoveryAndPreservesPrimaryFailure()
        {
            bool activeSceneRestored = false;
            bool diskHashVerified = false;
            ExceptionDispatchInfo primary = ExceptionDispatchInfo.Capture(
                new InvalidOperationException("primary test failure")
            );

            InvalidOperationException failure = Assert.Throws<
                InvalidOperationException>(() =>
                RunFixtureCleanup(
                    primary,
                    () => throw new IOException("temporary close failed"),
                    () => activeSceneRestored = true,
                    () => diskHashVerified = true
                )
            );

            Assert.That(activeSceneRestored, Is.True);
            Assert.That(diskHashVerified, Is.True);
            Assert.That(failure.Message, Is.EqualTo("primary test failure"));
            Assert.That(
                ((AggregateException)failure.Data[CleanupFailuresDataKey])
                    .InnerExceptions,
                Has.Some.TypeOf<IOException>()
            );
        }

        [Test]
        public void CleanInteractionLabSetupIsCompleteAndAssetIsUnchanged()
        {
            WithCleanInteractionScene(scene =>
            {
                TargetInvocationException missingSetup = Assert.Throws<
                    TargetInvocationException>(() =>
                        InvokeValidateLoadedScene(scene)
                    );
                Assert.That(
                    missingSetup.InnerException.Message,
                    Does.Contain("Expected one W7 coordinator")
                );

                InvokeTestOwnedSetupAndValidate(scene);
                InvokeValidateLoadedScene(scene);

                Assert.That(
                    FindGameObjectsInScene(scene, "W7SafePasswordHint"),
                    Is.Empty
                );
                Assert.That(
                    FindGameObjectsInScene(scene, "W7SafeSubmit"),
                    Is.Empty
                );
                object boxProxy = FindGameObjectInScene(
                    scene,
                    "W7Target_box_stool"
                );
                DestroyImmediate(boxProxy);
                TargetInvocationException missingCritical = Assert.Throws<
                    TargetInvocationException>(() =>
                        InvokeValidateLoadedScene(scene)
                    );
                Assert.That(
                    missingCritical.InnerException.Message,
                    Does.Contain(
                        "Target 'box_stool' must use only its exact " +
                        "'W7Target_box_stool'"
                    )
                );
            });
        }

        [TestCase("dragon_coin")]
        [TestCase("golden_coin")]
        [TestCase("golden_coin (1)")]
        public void PhaseTwoMakesCoinGrabbableAndMovable(string coinName)
        {
            WithCleanInteractionScene(scene =>
            {
                InvokeTestOwnedSetupAndValidate(scene);
                InvokeTestOwnedSetupAndValidate(scene);

                object runtimeRoot = FindGameObjectInScene(
                    scene,
                    "W7PhaseInteractionAdapters"
                );
                object phaseTwoObject = GetGameObject(
                    FindChild(GetTransform(runtimeRoot), "Phase2Adapter")
                );
                object phaseTwo = GetComponent(
                    phaseTwoObject,
                    RuntimeType("PhaseTwoInteractionAdapter")
                );
                phaseTwo.GetType().GetMethod("Enable").Invoke(phaseTwo, null);

                object coin = FindGameObjectInScene(scene, coinName);
                object binding = GetComponent(
                    coin,
                    RuntimeType("InteractionTargetBinding")
                );
                Assert.That(
                    binding,
                    Is.Not.Null,
                    $"{coinName} must have its Phase 2 target binding."
                );
                binding.GetType().GetMethod(
                    "RefreshAvailabilityWithoutRuntimeSubscription",
                    BindingFlags.Instance | BindingFlags.NonPublic
                ).Invoke(binding, null);

                object[] grabBehaviours = GetComponentsInChildren(
                        coin,
                        UnityType("Behaviour"),
                        includeInactive: true
                    )
                    .Where(IsOculusGrabBehaviour)
                    .ToArray();
                Assert.That(
                    grabBehaviours,
                    Is.Not.Empty,
                    $"{coinName} must retain its authored Meta grab " +
                    "components."
                );
                Assert.That(
                    grabBehaviours.All(item =>
                        (bool)item.GetType().GetProperty("enabled")
                            .GetValue(item) &&
                        (bool)GetGameObject(item).GetType()
                            .GetProperty("activeInHierarchy")
                            .GetValue(GetGameObject(item))),
                    Is.True,
                    $"Phase 2 must activate and enable every authored " +
                    $"grab component for {coinName}."
                );

                object[] colliders = GetComponentsInChildren(
                    coin,
                    UnityPhysicsType("Collider"),
                    includeInactive: true
                );
                Assert.That(colliders, Is.Not.Empty);
                Assert.That(
                    colliders.Any(item =>
                        (bool)item.GetType().GetProperty("enabled")
                            .GetValue(item)),
                    Is.True,
                    $"Phase 2 must enable a physical collider for " +
                    $"{coinName}."
                );

                object[] bodies = GetComponentsInChildren(
                    coin,
                    UnityPhysicsType("Rigidbody"),
                    includeInactive: true
                );
                Assert.That(bodies, Has.Length.EqualTo(1));
                object body = bodies[0];
                Assert.That(
                    body.GetType().GetProperty("isKinematic")
                        .GetValue(body),
                    Is.False,
                    $"Phase 2 must make {coinName} movable."
                );
                Assert.That(
                    Convert.ToInt32(body.GetType()
                        .GetProperty("constraints").GetValue(body)),
                    Is.Zero,
                    $"Phase 2 must remove recording FreezeAll from " +
                    $"{coinName}."
                );
                Assert.That(
                    body.GetType().GetProperty("detectCollisions")
                        .GetValue(body),
                    Is.True,
                    $"Phase 2 must restore collisions for {coinName}."
                );
                Assert.That(
                    body.GetType().GetProperty("useGravity")
                        .GetValue(body),
                    Is.False,
                    $"Phase 2 must not turn gravity on for {coinName}."
                );
            });
        }

        [Test]
        public void SetupRoutesEachKeyThroughItsPhaseFourProxyOnly()
        {
            WithCleanInteractionScene(scene =>
            {
                InvokeTestOwnedSetupAndValidate(scene);

                foreach (string targetId in
                         new[] { "key_a", "key_b", "motorbike_key" })
                {
                    object proxy = FindGameObjectInScene(
                        scene,
                        "W7Target_" + targetId
                    );
                    object proxyCollider = GetComponent(
                        proxy,
                        UnityPhysicsType("BoxCollider")
                    );
                    object relay = GetComponent(
                        proxy,
                        RuntimeType("InteractionTriggerRelay")
                    );
                    object binding = relay.GetType()
                        .GetProperty("InputReceiver").GetValue(relay);

                    Assert.That(binding, Is.Not.Null);
                    Assert.That(
                        binding.GetType().GetProperty("TargetId")
                            .GetValue(binding),
                        Is.EqualTo(targetId)
                    );
                    Assert.That(
                        binding.GetType().GetProperty("PhaseId")
                            .GetValue(binding),
                        Is.EqualTo(4),
                        $"{targetId} must use the Phase 4 adapter."
                    );

                    Array inputColliders = (Array)binding.GetType()
                        .GetProperty("InputColliders").GetValue(binding);
                    Assert.That(inputColliders, Has.Length.EqualTo(1));
                    Assert.That(
                        inputColliders.GetValue(0),
                        Is.SameAs(proxyCollider),
                        $"{targetId} must keep W7Target_{targetId} as its " +
                        "only input collider."
                    );

                    object keyBody = GetGameObject(binding);
                    Assert.That(
                        GetComponentsInChildren(
                            keyBody,
                            RuntimeType("InteractionTriggerRelay"),
                            includeInactive: true
                        ),
                        Is.Empty,
                        $"{targetId}'s physical body must not gain a second " +
                        "trigger input channel."
                    );
                }
            });
        }

        [Test]
        public void TestOwnedSceneGuardRejectsMissingDuplicateAndMalformedOwners()
        {
            string validToken = Guid.NewGuid().ToString("N");
            string uniqueMalformedToken =
                Guid.NewGuid().ToString("N").Substring(0, 31) + "z";
            Assert.That(uniqueMalformedToken, Has.Length.EqualTo(32));
            Assert.That(uniqueMalformedToken, Does.EndWith("z"));
            AssertGuardSceneRejected(
                "__W7InteractionPhaseAdaptersTests_" + validToken,
                "InteractionLab_W7Test.unity",
                markerCount: 0,
                expectedMessage: "exactly one test-owned marker"
            );
            AssertGuardSceneRejected(
                "__W7InteractionPhaseAdaptersTests_" +
                    Guid.NewGuid().ToString("N"),
                "InteractionLab_W7Test.unity",
                markerCount: 2,
                expectedMessage: "exactly one test-owned marker"
            );
            AssertGuardSceneRejected(
                "__W7InteractionPhaseAdaptersTests_" +
                    uniqueMalformedToken,
                "InteractionLab_W7Test.unity",
                markerCount: 1,
                expectedMessage: "exact ownership token"
            );
            AssertGuardSceneRejected(
                "__W7InteractionPhaseAdaptersGuard_" +
                    Guid.NewGuid().ToString("N"),
                "InteractionLab_W7Test.unity",
                markerCount: 1,
                expectedMessage: "strict temporary InteractionLab"
            );
            AssertGuardSceneRejected(
                "__W7InteractionPhaseAdaptersTests_" +
                    Guid.NewGuid().ToString("N"),
                "MalformedInteractionLab.unity",
                markerCount: 1,
                expectedMessage: "strict temporary InteractionLab"
            );
        }

        [Test]
        public void CleanActiveSavedSceneSurvivesSuccessfulAndFailedFixtures()
        {
            string fullScenePath = Path.GetFullPath(
                InteractionSceneAssetPath
            );
            byte[] bytesBefore = File.ReadAllBytes(fullScenePath);
            string hashBefore = ComputeSha256(bytesBefore);
            Type editorSceneManager = EditorType(
                "UnityEditor.SceneManagement.EditorSceneManager"
            );
            Type assetDatabase = EditorType("UnityEditor.AssetDatabase");
            object previousActiveScene = GetActiveScene();
            string ownerId = Guid.NewGuid().ToString("N");
            string sourceFolderName =
                "__W7CleanPreviousActive_" + ownerId;
            string sourceFolderPath = "Assets/" + sourceFolderName;
            string sourceScenePath = sourceFolderPath +
                "/PreviousActive.unity";
            object sourceScene = null;
            object sentinel = null;
            object child = null;
            bool sourceFolderCreated = false;
            ExceptionDispatchInfo primaryFailure = null;
            try
            {
                string folderGuid = (string)assetDatabase.GetMethod(
                    "CreateFolder",
                    new[] { typeof(string), typeof(string) }
                ).Invoke(
                    null,
                    new object[] { "Assets", sourceFolderName }
                );
                if (string.IsNullOrWhiteSpace(folderGuid))
                {
                    throw new InvalidOperationException(
                        "Unity could not create the clean-source folder."
                    );
                }
                sourceFolderCreated = true;
                bool copied = (bool)assetDatabase.GetMethod(
                    "CopyAsset",
                    new[] { typeof(string), typeof(string) }
                ).Invoke(
                    null,
                    new object[]
                    {
                        InteractionSceneAssetPath,
                        sourceScenePath
                    }
                );
                if (!copied)
                {
                    throw new InvalidOperationException(
                        "Unity could not create the clean-source scene copy."
                    );
                }
                sourceScene = OpenAdditiveSceneAsset(sourceScenePath);
                SetActiveSceneForIsolatedCreation(sourceScene);
                sentinel = CreateGameObjectInActiveScene(
                    sourceScene,
                    "W7CleanSourceSentinel"
                );
                child = CreateGameObjectInActiveScene(
                    sourceScene,
                    "W7CleanSourceChild"
                );
                SetParent(GetTransform(child), GetTransform(sentinel));
                GetTransform(sentinel).GetType()
                    .GetProperty("localPosition")
                    .SetValue(
                        GetTransform(sentinel),
                        CreateVector3(2f, 4f, 6f)
                    );
                GetTransform(child).GetType()
                    .GetProperty("localPosition")
                    .SetValue(
                        GetTransform(child),
                        CreateVector3(1f, 3f, 5f)
                    );

                bool saved = (bool)editorSceneManager.GetMethod(
                    "SaveScene",
                    new[]
                    {
                        sourceScene.GetType(),
                        typeof(string),
                        typeof(bool)
                    }
                ).Invoke(
                    null,
                    new[]
                    {
                        sourceScene,
                        (object)sourceScenePath,
                        false
                    }
                );
                Assert.That(saved, Is.True);
                int rootCount = GetSceneRoots(sourceScene).Length;
                AssertCleanActiveSceneUnchanged(
                    sourceScene,
                    sentinel,
                    child,
                    rootCount
                );

                WithCleanInteractionScene(scene =>
                {
                    Assert.That(
                        scene.Equals(GetActiveScene()),
                        Is.True,
                        "The isolated copy must be active before setup."
                    );
                    InvokeTestOwnedSetupAndValidate(scene);
                    InvokeValidateLoadedScene(scene);

                    Array markers = FindGameObjectsInScene(
                        scene,
                        TestSceneMarkerName
                    );
                    Assert.That(markers, Has.Length.EqualTo(1));
                    DestroyImmediate(markers.GetValue(0));
                    SetActiveSceneForIsolatedCreation(sourceScene);
                    TargetInvocationException inactiveTarget = Assert.Throws<
                        TargetInvocationException>(() =>
                            Type.GetType(
                                ValidatorTypeName,
                                throwOnError: true
                            ).GetMethod(
                                "MarkTestOwnedSceneForAutomation",
                                BindingFlags.Public | BindingFlags.Static
                            ).Invoke(null, new[] { scene })
                    );
                    Assert.That(
                        inactiveTarget.InnerException.Message,
                        Does.Contain("loaded and active")
                    );
                });
                AssertCleanActiveSceneUnchanged(
                    sourceScene,
                    sentinel,
                    child,
                    rootCount
                );

                InvalidOperationException expectedFailure = Assert.Throws<
                    InvalidOperationException>(() =>
                        WithCleanInteractionScene(scene =>
                        {
                            Assert.That(
                                scene.Equals(GetActiveScene()),
                                Is.True
                            );
                            throw new InvalidOperationException(
                                "expected isolated fixture failure"
                            );
                        })
                );
                Assert.That(
                    expectedFailure.Message,
                    Is.EqualTo("expected isolated fixture failure")
                );
                Assert.That(
                    expectedFailure.Data.Contains(CleanupFailuresDataKey),
                    Is.False,
                    "Successful fixture cleanup must not annotate the " +
                        "expected primary failure."
                );
                AssertCleanActiveSceneUnchanged(
                    sourceScene,
                    sentinel,
                    child,
                    rootCount
                );

                AssertGuardSceneRejected(
                    "__W7InteractionPhaseAdaptersTests_" +
                        Guid.NewGuid().ToString("N"),
                    "InteractionLab_W7Test.unity",
                    markerCount: 0,
                    expectedMessage: "exactly one test-owned marker"
                );
                AssertCleanActiveSceneUnchanged(
                    sourceScene,
                    sentinel,
                    child,
                    rootCount
                );

                Assert.Throws<InvalidOperationException>(() =>
                    CreateGameObjectInActiveScene(
                        sourceScene,
                        "W7InjectedCreationFailure",
                        _ => throw new InvalidOperationException(
                            "injected post-create failure"
                        )
                    )
                );
                AssertCleanActiveSceneUnchanged(
                    sourceScene,
                    sentinel,
                    child,
                    rootCount
                );
            }
            catch (Exception exception)
            {
                primaryFailure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                RunFixtureCleanup(
                    primaryFailure,
                    () =>
                    {
                        if (sourceScene != null &&
                            IsSceneValid(sourceScene) &&
                            GetSceneBoolean(sourceScene, "isLoaded"))
                        {
                            CloseScene(editorSceneManager, sourceScene);
                        }
                    },
                    () => RestoreActiveScene(previousActiveScene),
                    () =>
                    {
                        if (!sourceFolderCreated)
                        {
                            return;
                        }
                        bool deleted = (bool)assetDatabase.GetMethod(
                            "DeleteAsset",
                            new[] { typeof(string) }
                        ).Invoke(null, new object[] { sourceFolderPath });
                        if (!deleted)
                        {
                            throw new InvalidOperationException(
                                "Unity could not delete the clean-source " +
                                "folder."
                            );
                        }
                    },
                    () => AssertSceneAssetUnchanged(
                        fullScenePath,
                        bytesBefore,
                        hashBefore
                    )
                );
            }
        }

        [Test]
        public void IsolatedSetupPreservesDirtyUnsavedUserScene()
        {
            string fullScenePath = Path.GetFullPath(
                InteractionSceneAssetPath
            );
            byte[] bytesBefore = File.ReadAllBytes(fullScenePath);
            string hashBefore = ComputeSha256(bytesBefore);
            Type editorSceneManager = EditorType(
                "UnityEditor.SceneManagement.EditorSceneManager"
            );
            Type assetDatabase = EditorType("UnityEditor.AssetDatabase");
            object previousActiveScene = GetActiveScene();
            string ownerId = Guid.NewGuid().ToString("N");
            string userFolderName = "__W7DirtyUserScene_" + ownerId;
            string userFolderPath = "Assets/" + userFolderName;
            string userScenePath = userFolderPath + "/DirtyUser.unity";
            object userScene = null;
            object sentinel = null;
            object sentinelTransform = null;
            bool userFolderCreated = false;
            ExceptionDispatchInfo primaryFailure = null;
            try
            {
                string folderGuid = (string)assetDatabase.GetMethod(
                    "CreateFolder",
                    new[] { typeof(string), typeof(string) }
                ).Invoke(
                    null,
                    new object[] { "Assets", userFolderName }
                );
                if (string.IsNullOrWhiteSpace(folderGuid))
                {
                    throw new InvalidOperationException(
                        "Unity could not create the dirty-user folder."
                    );
                }
                userFolderCreated = true;
                bool copied = (bool)assetDatabase.GetMethod(
                    "CopyAsset",
                    new[] { typeof(string), typeof(string) }
                ).Invoke(
                    null,
                    new object[]
                    {
                        InteractionSceneAssetPath,
                        userScenePath
                    }
                );
                if (!copied)
                {
                    throw new InvalidOperationException(
                        "Unity could not create the dirty-user scene copy."
                    );
                }
                userScene = OpenAdditiveSceneAsset(userScenePath);
                SetActiveSceneForIsolatedCreation(userScene);
                sentinel = CreateGameObjectInActiveScene(
                    userScene,
                    "W7DirtyUserSentinel"
                );
                sentinelTransform = GetTransform(sentinel);
                object authoredPosition = CreateVector3(3f, 5f, 7f);
                sentinelTransform.GetType().GetProperty("localPosition")
                    .SetValue(sentinelTransform, authoredPosition);
                editorSceneManager.GetMethod(
                    "MarkSceneDirty",
                    new[] { userScene.GetType() }
                ).Invoke(null, new[] { userScene });

                Assert.That(GetSceneBoolean(userScene, "isLoaded"), Is.True);
                Assert.That(GetSceneBoolean(userScene, "isDirty"), Is.True);
                int rootCountBefore = GetSceneRoots(userScene).Length;

                WithCleanInteractionScene(scene =>
                {
                    InvokeTestOwnedSetupAndValidate(scene);
                    InvokeValidateLoadedScene(scene);
                });

                Assert.That(
                    userScene.Equals(GetActiveScene()),
                    Is.True,
                    "The fixture did not restore the dirty user scene as active."
                );
                Assert.That(
                    GetSceneRoots(userScene).Length,
                    Is.EqualTo(rootCountBefore),
                    "The fixture leaked roots into the dirty user scene."
                );
                Assert.That(
                    GetSceneBoolean(userScene, "isLoaded"),
                    Is.True,
                    "The fixture closed an unrelated unsaved user scene."
                );
                Assert.That(
                    GetSceneBoolean(userScene, "isDirty"),
                    Is.True,
                    "The fixture cleared unrelated dirty scene state."
                );
                Assert.That(
                    sentinel.GetType().GetProperty("name").GetValue(sentinel),
                    Is.EqualTo("W7DirtyUserSentinel")
                );
                AssertVector3(
                    sentinelTransform.GetType().GetProperty("localPosition")
                        .GetValue(sentinelTransform),
                    3f,
                    5f,
                    7f,
                    "Unsaved user scene values changed."
                );
            }
            catch (Exception exception)
            {
                primaryFailure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                RunFixtureCleanup(
                    primaryFailure,
                    () =>
                    {
                        if (userScene != null && IsSceneValid(userScene) &&
                            GetSceneBoolean(userScene, "isLoaded"))
                        {
                            CloseScene(editorSceneManager, userScene);
                        }
                    },
                    () => RestoreActiveScene(previousActiveScene),
                    () =>
                    {
                        if (!userFolderCreated)
                        {
                            return;
                        }
                        bool deleted = (bool)assetDatabase.GetMethod(
                            "DeleteAsset",
                            new[] { typeof(string) }
                        ).Invoke(null, new object[] { userFolderPath });
                        if (!deleted)
                        {
                            throw new InvalidOperationException(
                                "Unity could not delete the dirty-user folder."
                            );
                        }
                    },
                    () => AssertSceneAssetUnchanged(
                        fullScenePath,
                        bytesBefore,
                        hashBefore
                    )
                );
            }
        }

        [Test]
        public void LoadedInteractionLabMemoryStateSurvivesIsolatedSetup()
        {
            Type editorSceneManager = EditorType(
                "UnityEditor.SceneManagement.EditorSceneManager"
            );
            Type openModeType = EditorType(
                "UnityEditor.SceneManagement.OpenSceneMode"
            );
            object previousActiveScene = GetActiveScene();
            object interactionScene = GetSceneByPath(InteractionSceneAssetPath);
            bool openedByTest = !IsSceneValid(interactionScene) ||
                !GetSceneBoolean(interactionScene, "isLoaded");
            object sentinel = null;
            ExceptionDispatchInfo primaryFailure = null;
            try
            {
                if (openedByTest)
                {
                    interactionScene = editorSceneManager.GetMethod(
                        "OpenScene",
                        new[] { typeof(string), openModeType }
                    ).Invoke(
                        null,
                        new[]
                        {
                            (object)InteractionSceneAssetPath,
                            Enum.Parse(openModeType, "Additive")
                        }
                    );
                    SetActiveSceneForIsolatedCreation(interactionScene);
                    sentinel = CreateGameObjectInActiveScene(
                        interactionScene,
                        "W7LoadedInteractionLabDirtySentinel"
                    );
                    GetTransform(sentinel).GetType()
                        .GetProperty("localPosition")
                        .SetValue(
                            GetTransform(sentinel),
                            CreateVector3(11f, 13f, 17f)
                        );
                    MarkSceneDirty(editorSceneManager, interactionScene);
                }
                else
                {
                    Array roots = GetSceneRoots(interactionScene);
                    Assert.That(
                        roots.Length,
                        Is.GreaterThan(0),
                        "Loaded InteractionLab has no sentinel root."
                    );
                    sentinel = roots.GetValue(0);
                }

                bool dirtyBefore = GetSceneBoolean(
                    interactionScene,
                    "isDirty"
                );
                int rootCountBefore = GetSceneRoots(interactionScene).Length;
                string sentinelName = (string)sentinel.GetType()
                    .GetProperty("name").GetValue(sentinel);
                object sentinelPosition = GetTransform(sentinel).GetType()
                    .GetProperty("localPosition").GetValue(
                        GetTransform(sentinel)
                    );
                float sentinelX = ReadVector3Component(sentinelPosition, "x");
                float sentinelY = ReadVector3Component(sentinelPosition, "y");
                float sentinelZ = ReadVector3Component(sentinelPosition, "z");

                TargetInvocationException ownershipFailure = Assert.Throws<
                    TargetInvocationException>(() =>
                        InvokeValidateLoadedScene(interactionScene)
                    );
                Assert.That(
                    ownershipFailure.InnerException.Message,
                    Does.Contain("strict temporary InteractionLab")
                );

                WithCleanInteractionScene(scene =>
                {
                    InvokeTestOwnedSetupAndValidate(scene);
                    InvokeValidateLoadedScene(scene);
                });

                object reloadedReference = GetSceneByPath(
                    InteractionSceneAssetPath
                );
                Assert.That(IsSceneValid(reloadedReference), Is.True);
                Assert.That(
                    GetSceneBoolean(reloadedReference, "isLoaded"),
                    Is.True,
                    "The loaded InteractionLab was closed or replaced."
                );
                Assert.That(
                    GetSceneBoolean(reloadedReference, "isDirty"),
                    Is.EqualTo(dirtyBefore),
                    "The loaded InteractionLab dirty state changed."
                );
                Assert.That(
                    GetSceneRoots(reloadedReference).Length,
                    Is.EqualTo(rootCountBefore),
                    "The loaded InteractionLab hierarchy changed."
                );
                Assert.That(
                    sentinel.GetType().GetProperty("name").GetValue(sentinel),
                    Is.EqualTo(sentinelName)
                );
                AssertVector3(
                    GetTransform(sentinel).GetType()
                        .GetProperty("localPosition")
                        .GetValue(GetTransform(sentinel)),
                    sentinelX,
                    sentinelY,
                    sentinelZ,
                    "Loaded InteractionLab sentinel value changed."
                );
            }
            catch (Exception exception)
            {
                primaryFailure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                RunFixtureCleanup(
                    primaryFailure,
                    () =>
                    {
                        if (openedByTest && IsSceneValid(interactionScene) &&
                            GetSceneBoolean(interactionScene, "isLoaded"))
                        {
                            CloseScene(editorSceneManager, interactionScene);
                        }
                    },
                    () => RestoreActiveScene(previousActiveScene)
                );
            }
        }

        [Test]
        public void StripUsesExactOwnershipAndRestoresAuthoredRuntimeState()
        {
            WithCleanInteractionScene(scene =>
            {
                InvokeTestOwnedSetupAndValidate(scene);
                int setupOwnedCount = CountW7OwnedWiring(scene);
                Assert.That(setupOwnedCount, Is.GreaterThan(0));

                object notes = CreateGameObject("W7Notes");
                object userContent = CreateGameObject("W7UserContent");
                MoveGameObjectToScene(notes, scene);
                MoveGameObjectToScene(userContent, scene);
                Assert.That(
                    CountW7OwnedWiring(scene),
                    Is.EqualTo(setupOwnedCount),
                    "Arbitrary W7-prefixed user names must not confer ownership."
                );

                object relayProbe = FindGameObjectInScene(
                    scene,
                    "W7Target_box_stool"
                );
                object relayProbeCollider = GetComponent(
                    relayProbe,
                    UnityPhysicsType("BoxCollider")
                );
                object initialRelay = GetComponent(
                    relayProbe,
                    RuntimeType("InteractionTriggerRelay")
                );
                Assert.That(initialRelay, Is.Not.Null);
                DestroyImmediate(initialRelay);
                relayProbeCollider.GetType().GetProperty("isTrigger")
                    .SetValue(relayProbeCollider, false);

                InvokeTestOwnedSetupAndValidate(scene);
                object relay = GetComponent(
                    relayProbe,
                    RuntimeType("InteractionTriggerRelay")
                );
                Assert.That(relay, Is.Not.Null);
                Assert.That(
                    relayProbeCollider.GetType().GetProperty("isTrigger")
                        .GetValue(relayProbeCollider),
                    Is.True,
                    "Relay Configure must enable its generated trigger proxy."
                );
                object receiver = relay.GetType().GetProperty("InputReceiver")
                    .GetValue(relay);
                object allowedRoots = relay.GetType()
                    .GetProperty("AllowedInteractorRoots")
                    .GetValue(relay);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    relay.GetType().GetMethod("Configure").Invoke(
                        relay,
                        new[] { receiver, allowedRoots, (object)0.3f }
                    );
                }
                Assert.That(
                    relayProbeCollider.GetType().GetProperty("isTrigger")
                        .GetValue(relayProbeCollider),
                    Is.True,
                    "Repeated Configure must keep the proxy operational."
                );
                SetParent(
                    GetTransform(relayProbe),
                    GetTransform(userContent)
                );
                relayProbe.GetType().GetProperty("name").SetValue(
                    relayProbe,
                    "RelayAuthoredStateProbe"
                );

                object transform = GetTransform(userContent);
                transform.GetType().GetProperty("localPosition").SetValue(
                    transform,
                    CreateVector3(1f, 2f, 3f)
                );
                transform.GetType().GetProperty("localScale").SetValue(
                    transform,
                    CreateVector3(2f, 3f, 4f)
                );
                object interactionBehaviour = AddComponent(
                    userContent,
                    UnityAudioType("AudioSource")
                );
                object collider = AddComponent(
                    userContent,
                    UnityPhysicsType("BoxCollider")
                );
                object body = AddComponent(
                    userContent,
                    UnityPhysicsType("Rigidbody")
                );
                interactionBehaviour.GetType().GetProperty("enabled")
                    .SetValue(interactionBehaviour, true);
                collider.GetType().GetProperty("enabled")
                    .SetValue(collider, true);
                body.GetType().GetProperty("isKinematic")
                    .SetValue(body, false);
                body.GetType().GetProperty("useGravity")
                    .SetValue(body, true);

                object runtimeRoot = FindGameObjectInScene(
                    scene,
                    "W7PhaseInteractionAdapters"
                );
                object phaseOneObject = GetGameObject(
                    FindChild(GetTransform(runtimeRoot), "Phase1Adapter")
                );
                object phaseOne = GetComponent(
                    phaseOneObject,
                    RuntimeType("PhaseOneInteractionAdapter")
                );
                object binding = AddComponent(
                    userContent,
                    RuntimeType("InteractionTargetBinding")
                );
                binding.GetType().GetMethod("Configure").Invoke(
                    binding,
                    new object[]
                    {
                        "box_stool",
                        phaseOne,
                        TypedArray(
                            UnityType("Behaviour"),
                            interactionBehaviour
                        ),
                        TypedArray(UnityPhysicsType("Collider"), collider)
                    }
                );
                Assert.That(
                    interactionBehaviour.GetType().GetProperty("enabled")
                        .GetValue(interactionBehaviour),
                    Is.False
                );
                Assert.That(
                    collider.GetType().GetProperty("enabled")
                        .GetValue(collider),
                    Is.False
                );

                transform.GetType().GetProperty("localPosition").SetValue(
                    transform,
                    CreateVector3(9f, 9f, 9f)
                );
                transform.GetType().GetProperty("localScale").SetValue(
                    transform,
                    CreateVector3(0.5f, 0.5f, 0.5f)
                );
                body.GetType().GetProperty("isKinematic")
                    .SetValue(body, true);
                body.GetType().GetProperty("useGravity")
                    .SetValue(body, false);

                StripW7OwnedWiring(scene);

                Assert.That(
                    FindGameObjectInScene(scene, "W7Notes"),
                    Is.SameAs(notes)
                );
                Assert.That(
                    FindGameObjectInScene(scene, "W7UserContent"),
                    Is.SameAs(userContent)
                );
                Assert.That(
                    GetComponent(
                        userContent,
                        RuntimeType("InteractionTargetBinding")
                    ),
                    Is.Null
                );
                Assert.That(
                    interactionBehaviour.GetType().GetProperty("enabled")
                        .GetValue(interactionBehaviour),
                    Is.True
                );
                Assert.That(
                    collider.GetType().GetProperty("enabled")
                        .GetValue(collider),
                    Is.True
                );
                Assert.That(
                    body.GetType().GetProperty("isKinematic").GetValue(body),
                    Is.False
                );
                Assert.That(
                    body.GetType().GetProperty("useGravity").GetValue(body),
                    Is.True
                );
                AssertVector3(
                    transform.GetType().GetProperty("localPosition")
                        .GetValue(transform),
                    1f,
                    2f,
                    3f,
                    "Teardown did not restore authored position."
                );
                AssertVector3(
                    transform.GetType().GetProperty("localScale")
                        .GetValue(transform),
                    2f,
                    3f,
                    4f,
                    "Teardown did not restore authored scale."
                );
                Assert.That(
                    GetComponent(
                        relayProbe,
                        RuntimeType("InteractionTriggerRelay")
                    ),
                    Is.Null
                );
                Assert.That(
                    relayProbeCollider.GetType().GetProperty("isTrigger")
                        .GetValue(relayProbeCollider),
                    Is.False,
                    "Relay teardown did not restore authored isTrigger=false."
                );
                Assert.That(CountW7OwnedWiring(scene), Is.Zero);

                StripW7OwnedWiring(scene);
                Assert.That(
                    relayProbeCollider.GetType().GetProperty("isTrigger")
                        .GetValue(relayProbeCollider),
                    Is.False,
                    "Repeated teardown must preserve the authored trigger state."
                );
                Assert.That(CountW7OwnedWiring(scene), Is.Zero);

                object orphanGeneratedId = CreateGameObject("W7SafeSubmit");
                MoveGameObjectToScene(orphanGeneratedId, scene);
                Assert.That(
                    CountW7OwnedWiring(scene),
                    Is.EqualTo(1),
                    "Count=0 must reject component-free generated state."
                );
                StripW7OwnedWiring(scene);
                Assert.That(CountW7OwnedWiring(scene), Is.Zero);
                Assert.That(
                    FindGameObjectsInScene(scene, "W7SafeSubmit"),
                    Is.Empty
                );
                Assert.That(
                    FindGameObjectsInScene(scene, "W7Notes"),
                    Has.Length.EqualTo(1)
                );
                Assert.That(
                    FindGameObjectsInScene(scene, "W7UserContent"),
                    Has.Length.EqualTo(1)
                );
            });
        }

        [Test]
        public void AbsoluteHingeIgnoresCurrentPoseAndRebuildsIdempotently()
        {
            object root = CreateGameObject("W7AbsoluteHingeTest");
            try
            {
                object rootTransform = GetTransform(root);
                object movingPart = CreateVisual(rootTransform, "MovingPart");
                object hinge = CreateVisual(rootTransform, "Hinge");
                movingPart.GetType().GetProperty("localEulerAngles").SetValue(
                    movingPart,
                    CreateVector3(135f, 0f, 0f)
                );

                Type bindingType = RuntimeType("DeterministicHingeBinding");
                object binding = Activator.CreateInstance(bindingType);
                bindingType.GetMethod("ConfigureAbsolute").Invoke(
                    binding,
                    new[]
                    {
                        movingPart,
                        hinge,
                        CreateVector3(1f, 0f, 0f),
                        (object)50f,
                        CreateVector3(0f, 0f, 0f),
                        CreateVector3(10f, 0f, 0f)
                    }
                );

                bindingType.GetMethod("Reset").Invoke(binding, null);
                Assert.That(
                    ReadVector3Component(
                        movingPart.GetType().GetProperty("localEulerAngles")
                            .GetValue(movingPart),
                        "x"
                    ),
                    Is.EqualTo(10f).Within(0.01f),
                    "Reset must use the explicit closed pose, not 135 degrees."
                );

                bindingType.GetMethod("Open").Invoke(binding, null);
                Assert.That(
                    ReadVector3Component(
                        movingPart.GetType().GetProperty("localEulerAngles")
                            .GetValue(movingPart),
                        "x"
                    ),
                    Is.EqualTo(60f).Within(0.01f)
                );

                movingPart.GetType().GetProperty("localEulerAngles").SetValue(
                    movingPart,
                    CreateVector3(200f, 0f, 0f)
                );
                bindingType.GetMethod("Open").Invoke(binding, null);
                Assert.That(
                    ReadVector3Component(
                        movingPart.GetType().GetProperty("localEulerAngles")
                            .GetValue(movingPart),
                        "x"
                    ),
                    Is.EqualTo(60f).Within(0.01f),
                    "Repeated open must restore one absolute open pose."
                );

                bindingType.GetMethod("CaptureClosedPose").Invoke(
                    binding,
                    null
                );
                bindingType.GetMethod("Reset").Invoke(binding, null);
                Assert.That(
                    ReadVector3Component(
                        movingPart.GetType().GetProperty("localEulerAngles")
                            .GetValue(movingPart),
                        "x"
                    ),
                    Is.EqualTo(10f).Within(0.01f),
                    "Runtime capture must not replace an explicit baseline."
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void PhaseFourEntryOpensChestAndShowsVisualOnlyKeys()
        {
            object root = CreateGameObject("W7PhaseFourEntryPresentationTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );

                object lid = CreateVisual(rootTransform, "ChestLid");
                object hinge = CreateVisual(rootTransform, "ChestHinge");
                Type hingeType = RuntimeType("DeterministicHingeBinding");
                object chestHinge = Activator.CreateInstance(hingeType);
                hingeType.GetMethod("ConfigureAbsolute").Invoke(
                    chestHinge,
                    new[]
                    {
                        lid,
                        hinge,
                        CreateVector3(1f, 0f, 0f),
                        (object)60f,
                        CreateVector3(0f, 0f, 0f),
                        CreateVector3(0f, 0f, 0f)
                    }
                );

                object[] legacyButtons =
                {
                    CreateVisual(rootTransform, "blue"),
                    CreateVisual(rootTransform, "red"),
                    CreateVisual(rootTransform, "yellow"),
                    CreateVisual(rootTransform, "green")
                };
                Array chestButtons = CreateTargetStateBindings(
                    legacyButtons,
                    new[] { "blue", "red", "yellow", "green" }
                );

                Type keyType = RuntimeType("PlannedKeyReleaseBinding");
                Array keys = Array.CreateInstance(keyType, 3);
                object[] keyModels = new object[3];
                object[] keyProxies = new object[3];
                object[] keyBodies = new object[3];
                object[] keyColliders = new object[3];
                string[] keyIds = { "key_a", "key_b", "motorbike_key" };
                for (int index = 0; index < keyIds.Length; index++)
                {
                    keyModels[index] = CreateGameObject(keyIds[index]);
                    SetParent(GetTransform(keyModels[index]), rootTransform);
                    keyBodies[index] = AddComponent(
                        keyModels[index],
                        UnityPhysicsType("Rigidbody")
                    );
                    keyColliders[index] = AddComponent(
                        keyModels[index],
                        UnityPhysicsType("BoxCollider")
                    );
                    keyProxies[index] = CreateGameObject(
                        "W7Target_" + keyIds[index]
                    );
                    SetParent(GetTransform(keyProxies[index]), rootTransform);

                    object key = Activator.CreateInstance(keyType);
                    keyType.GetMethod("ConfigureVisualOnly").Invoke(
                        key,
                        new[]
                        {
                            keyIds[index],
                            keyModels[index],
                            keyProxies[index],
                            TypedArray(
                                UnityPhysicsType("Rigidbody"),
                                keyBodies[index]
                            ),
                            Array.CreateInstance(UnityType("Behaviour"), 0),
                            TypedArray(
                                UnityPhysicsType("Collider"),
                                keyColliders[index]
                            )
                        }
                    );
                    keys.SetValue(key, index);
                }

                Type presentationType = RuntimeType(
                    "InteractionDeterministicPresentation"
                );
                object presentation = AddComponent(root, presentationType);
                Type stateType = RuntimeType(
                    "DeterministicTargetStateBinding"
                );
                Array noStates = Array.CreateInstance(stateType, 0);
                presentationType.GetMethod("Configure").Invoke(
                    presentation,
                    new object[]
                    {
                        coordinator,
                        Activator.CreateInstance(hingeType),
                        chestHinge,
                        Activator.CreateInstance(hingeType),
                        Activator.CreateInstance(hingeType),
                        Activator.CreateInstance(hingeType),
                        chestButtons,
                        noStates,
                        noStates,
                        keys
                    }
                );

                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 4);
                presentationType.GetMethod("RebuildFromAuthority")
                    .Invoke(presentation, null);

                Assert.That(
                    RotationAngleFromIdentity(lid),
                    Is.EqualTo(60f).Within(0.01f),
                    "Phase 4 entry must open from the explicit closed pose."
                );
                foreach (object legacyButton in legacyButtons)
                {
                    Assert.That(
                        IsComponentGameObjectActive(legacyButton),
                        Is.False,
                        "Legacy colour controls must not participate in Phase 4."
                    );
                }
                for (int index = 0; index < keyIds.Length; index++)
                {
                    Assert.That(
                        IsComponentGameObjectActive(GetTransform(
                            keyModels[index]
                        )),
                        Is.True
                    );
                    Assert.That(
                        IsComponentGameObjectActive(GetTransform(
                            keyProxies[index]
                        )),
                        Is.True
                    );
                    Assert.That(
                        keyBodies[index].GetType().GetProperty("isKinematic")
                            .GetValue(keyBodies[index]),
                        Is.True
                    );
                    Assert.That(
                        keyBodies[index].GetType().GetProperty("useGravity")
                            .GetValue(keyBodies[index]),
                        Is.False
                    );
                    Assert.That(
                        keyColliders[index].GetType().GetProperty("enabled")
                            .GetValue(keyColliders[index]),
                        Is.False,
                        "The key model is visual-only; only its proxy is input."
                    );
                }
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void PhaseFiveEntryAndFeedbackAreVisibleUntilTimedReset()
        {
            object root = CreateGameObject("W7PhaseFiveFeedbackTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { CreateSixAdapters(rootTransform) }
                );

                Type hingeType = RuntimeType("DeterministicHingeBinding");
                object leftDoor = CreateVisual(rootTransform, "LeftDoor");
                object leftHinge = CreateVisual(rootTransform, "LeftHinge");
                object rightDoor = CreateVisual(rootTransform, "RightDoor");
                object rightHinge = CreateVisual(rootTransform, "RightHinge");
                object leftBinding = Activator.CreateInstance(hingeType);
                object rightBinding = Activator.CreateInstance(hingeType);
                MethodInfo configureHinge = hingeType.GetMethod(
                    "ConfigureAbsolute"
                );
                configureHinge.Invoke(
                    leftBinding,
                    new[]
                    {
                        leftDoor,
                        leftHinge,
                        CreateVector3(0f, 1f, 0f),
                        (object)70f,
                        CreateVector3(0f, 0f, 0f),
                        CreateVector3(0f, 0f, 0f)
                    }
                );
                configureHinge.Invoke(
                    rightBinding,
                    new[]
                    {
                        rightDoor,
                        rightHinge,
                        CreateVector3(0f, 1f, 0f),
                        (object)(-70f),
                        CreateVector3(0f, 0f, 0f),
                        CreateVector3(0f, 0f, 0f)
                    }
                );

                Type stateType = RuntimeType(
                    "DeterministicTargetStateBinding"
                );
                Type rendererType = UnityType("Renderer");
                Type meshRendererType = UnityType("MeshRenderer");
                Array cabinetBindings = Array.CreateInstance(stateType, 3);
                object[] buttons = new object[3];
                object[] renderers = new object[3];
                string[] buttonIds = { "button_a", "button_b", "button_c" };
                for (int index = 0; index < buttonIds.Length; index++)
                {
                    buttons[index] = CreateVisual(
                        rootTransform,
                        buttonIds[index]
                    );
                    renderers[index] = AddComponent(
                        GetGameObject(buttons[index]),
                        meshRendererType
                    );
                    object binding = Activator.CreateInstance(stateType);
                    stateType.GetMethod("ConfigureFeedback").Invoke(
                        binding,
                        new[]
                        {
                            buttonIds[index],
                            buttons[index],
                            CreateVector3(0f, -0.01f, 0f),
                            CreateVector3(-10f, 0f, 0f),
                            TypedArray(rendererType, renderers[index]),
                            CreateColor(0f, 1f, 0f, 1f),
                            CreateColor(1f, 0f, 0f, 1f)
                        }
                    );
                    cabinetBindings.SetValue(binding, index);
                }

                Type presentationType = RuntimeType(
                    "InteractionDeterministicPresentation"
                );
                object presentation = AddComponent(root, presentationType);
                Array noStates = Array.CreateInstance(stateType, 0);
                Array noKeys = Array.CreateInstance(
                    RuntimeType("PlannedKeyReleaseBinding"),
                    0
                );
                presentationType.GetMethod("Configure").Invoke(
                    presentation,
                    new object[]
                    {
                        coordinator,
                        Activator.CreateInstance(hingeType),
                        Activator.CreateInstance(hingeType),
                        leftBinding,
                        rightBinding,
                        Activator.CreateInstance(hingeType),
                        noStates,
                        cabinetBindings,
                        noStates,
                        noKeys
                    }
                );

                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                AdvanceToPhaseFive(coordinator);
                presentationType.GetMethod("RebuildFromAuthority")
                    .Invoke(presentation, null);

                Assert.That(
                    RotationAngleFromIdentity(leftDoor),
                    Is.EqualTo(70f).Within(0.01f)
                );
                Assert.That(
                    RotationAngleFromIdentity(rightDoor),
                    Is.EqualTo(70f).Within(0.01f)
                );

                presentationType.GetMethod("TickFeedback").Invoke(
                    presentation,
                    new object[] { 100d }
                );
                object accepted = AcceptTarget(
                    coordinator,
                    5,
                    "button_a"
                );
                presentationType.GetMethod("ApplyValidationResult").Invoke(
                    presentation,
                    new[] { accepted }
                );
                object firstBinding = cabinetBindings.GetValue(0);
                Assert.That(
                    stateType.GetProperty("VisualState")
                        .GetValue(firstBinding).ToString(),
                    Is.EqualTo("Accepted")
                );
                Assert.That(
                    RotationAngleFromIdentity(buttons[0]),
                    Is.GreaterThan(0.1f)
                );
                AssertColor(
                    ReadRendererPropertyBlockColor(renderers[0]),
                    0f,
                    1f,
                    0f
                );

                object reset = AcceptTarget(coordinator, 5, "button_a");
                presentationType.GetMethod("ApplyValidationResult").Invoke(
                    presentation,
                    new[] { reset }
                );
                Assert.That(
                    stateType.GetProperty("VisualState")
                        .GetValue(firstBinding).ToString(),
                    Is.EqualTo("Error")
                );
                AssertColor(
                    ReadRendererPropertyBlockColor(renderers[0]),
                    1f,
                    0f,
                    0f
                );

                presentationType.GetMethod("TickFeedback").Invoke(
                    presentation,
                    new object[] { 100.59d }
                );
                Assert.That(
                    stateType.GetProperty("VisualState")
                        .GetValue(firstBinding).ToString(),
                    Is.EqualTo("Error")
                );
                presentationType.GetMethod("TickFeedback").Invoke(
                    presentation,
                    new object[] { 100.61d }
                );
                for (int index = 0; index < cabinetBindings.Length; index++)
                {
                    Assert.That(
                        stateType.GetProperty("VisualState").GetValue(
                            cabinetBindings.GetValue(index)
                        ).ToString(),
                        Is.EqualTo("Idle")
                    );
                    Assert.That(
                        RotationAngleFromIdentity(buttons[index]),
                        Is.LessThan(0.001f)
                    );
                }
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void BreakerStateUsesExactHandlerAndPositiveAbsoluteX()
        {
            object root = CreateGameObject("free_switch_handler_ue5");
            try
            {
                object rootTransform = GetTransform(root);
                object exactHandler = CreateChildPath(
                    rootTransform,
                    "switchHandler.fbx/RootNode/handler"
                );
                object decoy = CreateVisual(rootTransform, "handler");
                Type setupType = Type.GetType(
                    ValidatorTypeName,
                    throwOnError: true
                );
                object resolved = setupType.GetMethod(
                    "ResolveBreakerHandler"
                ).Invoke(null, new[] { rootTransform });
                Assert.That(resolved, Is.SameAs(exactHandler));

                Type stateType = RuntimeType(
                    "DeterministicTargetStateBinding"
                );
                object binding = Activator.CreateInstance(stateType);
                stateType.GetMethod("ConfigureAbsolute").Invoke(
                    binding,
                    new[]
                    {
                        (object)"breaker_a",
                        exactHandler,
                        CreateVector3(0f, 0f, 0f),
                        CreateVector3(0f, 0f, 0f),
                        CreateVector3(0f, 0f, 0f),
                        CreateVector3(60f, 0f, 0f)
                    }
                );
                stateType.GetMethod("CaptureIdlePose").Invoke(binding, null);
                stateType.GetMethod("Activate").Invoke(binding, null);

                Assert.That(
                    RotationAngleFromIdentity(rootTransform),
                    Is.LessThan(0.001f),
                    "The physical switch root must remain fixed."
                );
                Assert.That(
                    RotationAngleFromIdentity(decoy),
                    Is.LessThan(0.001f),
                    "A same-named decoy must not be animated."
                );
                Assert.That(
                    ReadVector3Component(
                        exactHandler.GetType().GetProperty(
                            "localEulerAngles"
                        ).GetValue(exactHandler),
                        "x"
                    ),
                    Is.EqualTo(60f).Within(0.01f)
                );

                SetLocalEulerAngles(
                    exactHandler,
                    CreateVector3(200f, 0f, 0f)
                );
                stateType.GetMethod("Activate").Invoke(binding, null);
                Assert.That(
                    ReadVector3Component(
                        exactHandler.GetType().GetProperty(
                            "localEulerAngles"
                        ).GetValue(exactHandler),
                        "x"
                    ),
                    Is.EqualTo(60f).Within(0.01f),
                    "Repeated rebuilds must restore one absolute down pose."
                );
                stateType.GetMethod("Reset").Invoke(binding, null);
                Assert.That(
                    RotationAngleFromIdentity(exactHandler),
                    Is.LessThan(0.001f)
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void SetupFactoriesUseExplicitChestAndBreakerPoses()
        {
            object root = CreateGameObject("W7AbsoluteSetupFactoriesTest");
            try
            {
                object rootTransform = GetTransform(root);
                object chest = CreateVisual(rootTransform, "chest");
                object lid = CreateChildPath(
                    chest,
                    "Collada visual scene group/ChestUpper_low"
                );
                AddComponent(
                    GetGameObject(lid),
                    UnityType("MeshRenderer")
                );
                SetLocalEulerAngles(lid, CreateVector3(200f, 0f, 0f));

                Type setupType = Type.GetType(
                    ValidatorTypeName,
                    throwOnError: true
                );
                object firstChestBinding = setupType.GetMethod(
                    "CreateChestLidBinding"
                ).Invoke(null, new[] { chest });
                Type hingeType = RuntimeType("DeterministicHingeBinding");
                Assert.That(
                    hingeType.GetProperty("UsesExplicitClosedPose")
                        .GetValue(firstChestBinding),
                    Is.True
                );
                Assert.That(
                    RotationAngleFromIdentity(lid),
                    Is.EqualTo(108.03f).Within(0.02f)
                );
                hingeType.GetMethod("Open").Invoke(firstChestBinding, null);
                float firstOpenAngle = RotationAngleFromIdentity(lid);

                object secondChestBinding = setupType.GetMethod(
                    "CreateChestLidBinding"
                ).Invoke(null, new[] { chest });
                Assert.That(
                    RotationAngleFromIdentity(lid),
                    Is.EqualTo(108.03f).Within(0.02f),
                    "Repeated setup must restore the explicit closed pose."
                );
                hingeType.GetMethod("Open").Invoke(secondChestBinding, null);
                Assert.That(
                    RotationAngleFromIdentity(lid),
                    Is.EqualTo(firstOpenAngle).Within(0.02f)
                );

                object switchRoot = CreateVisual(
                    rootTransform,
                    "free_switch_handler_ue5"
                );
                object handler = CreateChildPath(
                    switchRoot,
                    "switchHandler.fbx/RootNode/handler"
                );
                SetLocalEulerAngles(handler, CreateVector3(-18f, 0f, 0f));
                object breakerBinding = setupType.GetMethod(
                    "CreateBreakerStateBinding"
                ).Invoke(
                    null,
                    new[] { (object)"breaker_a", switchRoot }
                );
                Type stateType = RuntimeType(
                    "DeterministicTargetStateBinding"
                );
                Assert.That(
                    stateType.GetProperty("Target").GetValue(breakerBinding),
                    Is.SameAs(handler)
                );
                stateType.GetMethod("Activate").Invoke(breakerBinding, null);
                Assert.That(
                    ReadVector3Component(
                        handler.GetType().GetProperty("localEulerAngles")
                            .GetValue(handler),
                        "x"
                    ),
                    Is.EqualTo(60f).Within(0.01f)
                );
                Assert.That(
                    RotationAngleFromIdentity(switchRoot),
                    Is.LessThan(0.001f)
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void PhaseSixPointingReportsRealHitNoHitAndDiagnostics()
        {
            object root = CreateGameObject("W7PhaseSixPointingTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type detectorType = PresentationType(
                    "GhostPointingDetector"
                );
                Type highlightType = PresentationType(
                    "InteractionTargetHighlightVisual"
                );
                Type bindingType = PresentationType(
                    "GhostPointingTargetBinding"
                );
                object detector = AddComponent(root, detectorType);
                object highlight = AddComponent(root, highlightType);
                detectorType.GetMethod("ConfigureHighlight").Invoke(
                    detector,
                    new[] { highlight }
                );
                Assert.That(
                    detectorType.GetProperty("DiagnosticStatus")
                        .GetValue(detector).ToString(),
                    Is.EqualTo("PhaseNotConfigured")
                );

                string[] targetIds =
                    { "breaker_a", "breaker_b", "breaker_c" };
                Array bindings = Array.CreateInstance(bindingType, 3);
                object[] targets = new object[3];
                for (int index = 0; index < targetIds.Length; index++)
                {
                    targets[index] = CreateVisual(
                        rootTransform,
                        targetIds[index]
                    );
                    SetLocalPosition(
                        targets[index],
                        CreateVector3(index * 2f, 0f, 2f)
                    );
                    AddComponent(
                        GetGameObject(targets[index]),
                        UnityPhysicsType("BoxCollider")
                    );
                    bindings.SetValue(
                        Activator.CreateInstance(
                            bindingType,
                            new[] { (object)targetIds[index], targets[index] }
                        ),
                        index
                    );
                }
                detectorType.GetMethod("ConfigureTargetBindings").Invoke(
                    detector,
                    new object[] { bindings }
                );

                object runPlan = CreateRunPlan();
                object phaseSixVariant = GetRunPlanTaskVariant(runPlan, 5);
                object textAndPointing = Enum.Parse(
                    CoreType("AssistanceCondition"),
                    "TextAndPointing"
                );
                detectorType.GetMethod(
                    "ConfigurePhase",
                    new[]
                    {
                        CoreType("AssistanceCondition"),
                        CoreType("TaskVariant")
                    }
                ).Invoke(
                    detector,
                    new[] { textAndPointing, phaseSixVariant }
                );
                Assert.That(
                    ((Array)detectorType.GetProperty("EligibleTargetIds")
                        .GetValue(detector)).Length,
                    Is.EqualTo(3)
                );
                Assert.That(
                    detectorType.GetProperty("DiagnosticStatus")
                        .GetValue(detector).ToString(),
                    Is.EqualTo("IncompleteFingerRig")
                );

                object leftDistal = CreateVisual(
                    rootTransform,
                    "Left_IndexDistal"
                );
                object leftTip = CreateVisual(rootTransform, "Left_IndexTip");
                object rightDistal = CreateVisual(
                    rootTransform,
                    "Right_IndexDistal"
                );
                object rightTip = CreateVisual(
                    rootTransform,
                    "Right_IndexTip"
                );
                SetLocalPosition(leftDistal, CreateVector3(0f, 0f, 0f));
                SetLocalPosition(leftTip, CreateVector3(0f, 0f, 0.1f));
                SetLocalPosition(rightDistal, CreateVector3(8f, 0f, 0f));
                SetLocalPosition(rightTip, CreateVector3(8f, 0f, 0.1f));
                detectorType.GetMethod("ConfigureFingerBones").Invoke(
                    detector,
                    new[] { leftDistal, leftTip, rightDistal, rightTip }
                );
                Assert.That(
                    detectorType.GetProperty("DiagnosticStatus")
                        .GetValue(detector).ToString(),
                    Is.EqualTo("PlaybackNotConfigured")
                );

                double now = (double)UnityType("Time").GetProperty(
                    "realtimeSinceStartupAsDouble",
                    BindingFlags.Public | BindingFlags.Static
                ).GetValue(null);
                detectorType.GetMethod("SynchronizePlayback").Invoke(
                    detector,
                    new object[] { true, now }
                );
                detectorType.GetMethod("EvaluatePointing").Invoke(
                    detector,
                    new object[] { now + 0.01d }
                );
                Assert.That(
                    detectorType.GetProperty("DiagnosticStatus")
                        .GetValue(detector).ToString(),
                    Is.EqualTo("Hit")
                );
                Assert.That(
                    detectorType.GetProperty("DiagnosticTargetId")
                        .GetValue(detector),
                    Is.EqualTo("breaker_a")
                );
                Assert.That(
                    highlightType.GetProperty("IsVisible")
                        .GetValue(highlight),
                    Is.True
                );

                SetLocalPosition(leftDistal, CreateVector3(8f, 0f, 0f));
                SetLocalPosition(leftTip, CreateVector3(8f, 0f, 0.1f));
                detectorType.GetMethod("EvaluatePointing").Invoke(
                    detector,
                    new object[] { now + 0.02d }
                );
                Assert.That(
                    detectorType.GetProperty("DiagnosticStatus")
                        .GetValue(detector).ToString(),
                    Is.EqualTo("NoHit")
                );
                Assert.That(
                    highlightType.GetProperty("IsVisible")
                        .GetValue(highlight),
                    Is.True,
                    "Loss grace must preserve the current visual briefly."
                );
                detectorType.GetMethod("EvaluatePointing").Invoke(
                    detector,
                    new object[] { now + 0.2d }
                );
                Assert.That(
                    highlightType.GetProperty("IsVisible")
                        .GetValue(highlight),
                    Is.False
                );

                object signOnly = Enum.Parse(
                    CoreType("AssistanceCondition"),
                    "SignOnly"
                );
                detectorType.GetMethod(
                    "ConfigurePhase",
                    new[]
                    {
                        CoreType("AssistanceCondition"),
                        CoreType("TaskVariant")
                    }
                ).Invoke(detector, new[] { signOnly, phaseSixVariant });
                Assert.That(
                    detectorType.GetProperty("DiagnosticStatus")
                        .GetValue(detector).ToString(),
                    Is.EqualTo("ConditionDoesNotIncludePointing")
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void KeyProxyHitHighlightsProxyAndVisualCompanion()
        {
            object root = CreateGameObject("W7KeyCompanionPointingTest");
            try
            {
                object rootTransform = GetTransform(root);
                object proxy = CreateVisual(rootTransform, "W7Target_key_a");
                object model = CreateVisual(rootTransform, "key");
                SetLocalPosition(proxy, CreateVector3(0f, 0f, 2f));
                SetLocalPosition(model, CreateVector3(0.2f, 0f, 2f));
                AddComponent(
                    GetGameObject(proxy),
                    UnityPhysicsType("BoxCollider")
                );

                Type detectorType = PresentationType(
                    "GhostPointingDetector"
                );
                Type highlightType = PresentationType(
                    "InteractionTargetHighlightVisual"
                );
                Type bindingType = PresentationType(
                    "GhostPointingTargetBinding"
                );
                object detector = AddComponent(root, detectorType);
                object highlight = AddComponent(root, highlightType);
                object binding = Activator.CreateInstance(
                    bindingType,
                    new object[]
                    {
                        "key_a",
                        proxy,
                        TypedArray(
                            UnityType("Transform"),
                            proxy,
                            model
                        )
                    }
                );
                Array bindings = Array.CreateInstance(bindingType, 1);
                bindings.SetValue(binding, 0);
                detectorType.GetMethod("ConfigureHighlight").Invoke(
                    detector,
                    new[] { highlight }
                );
                detectorType.GetMethod("ConfigureTargetBindings").Invoke(
                    detector,
                    new object[] { bindings }
                );

                object distal = CreateVisual(rootTransform, "IndexDistal");
                object tip = CreateVisual(rootTransform, "IndexTip");
                object otherDistal = CreateVisual(
                    rootTransform,
                    "OtherDistal"
                );
                object otherTip = CreateVisual(rootTransform, "OtherTip");
                SetLocalPosition(tip, CreateVector3(0f, 0f, 0.1f));
                SetLocalPosition(otherDistal, CreateVector3(8f, 0f, 0f));
                SetLocalPosition(otherTip, CreateVector3(8f, 0f, 0.1f));
                detectorType.GetMethod("ConfigureFingerBones").Invoke(
                    detector,
                    new[] { distal, tip, otherDistal, otherTip }
                );
                object variant = GetRunPlanTaskVariant(CreateRunPlan(), 3);
                object condition = Enum.Parse(
                    CoreType("AssistanceCondition"),
                    "TextAndPointing"
                );
                detectorType.GetMethod(
                    "ConfigurePhase",
                    new[]
                    {
                        CoreType("AssistanceCondition"),
                        CoreType("TaskVariant")
                    }
                ).Invoke(detector, new[] { condition, variant });
                double now = (double)UnityType("Time").GetProperty(
                    "realtimeSinceStartupAsDouble",
                    BindingFlags.Public | BindingFlags.Static
                ).GetValue(null);
                detectorType.GetMethod("SynchronizePlayback").Invoke(
                    detector,
                    new object[] { true, now }
                );
                detectorType.GetMethod("EvaluatePointing").Invoke(
                    detector,
                    new object[] { now + 0.01d }
                );

                Assert.That(
                    detectorType.GetProperty("DiagnosticTargetId")
                        .GetValue(detector),
                    Is.EqualTo("key_a")
                );
                Array highlightedRoots = (Array)highlightType.GetProperty(
                    "HighlightedRoots"
                ).GetValue(highlight);
                Assert.That(highlightedRoots.Length, Is.EqualTo(2));
                Assert.That(highlightedRoots.Cast<object>(), Does.Contain(proxy));
                Assert.That(highlightedRoots.Cast<object>(), Does.Contain(model));
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void ExactMovingPartPathRejectsDecoyNamedFallbacks()
        {
            object root = CreateGameObject("chest");
            try
            {
                object rootTransform = GetTransform(root);
                object exact = CreateChildPath(
                    rootTransform,
                    "Collada visual scene group/ChestUpper_low"
                );
                object decoy = CreateVisual(rootTransform, "lid");
                object hinge = CreateVisual(rootTransform, "hinge");
                Type hingeType = RuntimeType("DeterministicHingeBinding");
                object binding = Activator.CreateInstance(hingeType);
                MethodInfo configure = hingeType.GetMethod("Configure");
                MethodInfo exactPath = Type.GetType(
                    ValidatorTypeName,
                    throwOnError: true
                ).GetMethod(
                    "HasExactMovingPartPath",
                    BindingFlags.Public | BindingFlags.Static
                );

                configure.Invoke(
                    binding,
                    new object[]
                    {
                        decoy,
                        hinge,
                        CreateVector3(1f, 0f, 0f),
                        -90f
                    }
                );
                Assert.That(
                    exactPath.Invoke(
                        null,
                        new[]
                        {
                            rootTransform,
                            binding,
                            "Collada visual scene group/ChestUpper_low"
                        }
                    ),
                    Is.False
                );

                configure.Invoke(
                    binding,
                    new object[]
                    {
                        exact,
                        hinge,
                        CreateVector3(1f, 0f, 0f),
                        -90f
                    }
                );
                Assert.That(
                    exactPath.Invoke(
                        null,
                        new[]
                        {
                            rootTransform,
                            binding,
                            "Collada visual scene group/ChestUpper_low"
                        }
                    ),
                    Is.True
                );

                object door = CreateGameObject("door");
                SetParent(GetTransform(door), rootTransform);
                object doorRoot = GetTransform(door);
                object exactDoor = CreateChildPath(
                    doorRoot,
                    "ce5f462b0dd34333a6588509140a7fb8.fbx/RootNode/Door"
                );
                object decoyLeft = CreateVisual(doorRoot, "left");
                configure.Invoke(
                    binding,
                    new object[]
                    {
                        decoyLeft,
                        hinge,
                        CreateVector3(0f, 1f, 0f),
                        -90f
                    }
                );
                Assert.That(
                    exactPath.Invoke(
                        null,
                        new[]
                        {
                            doorRoot,
                            binding,
                            "ce5f462b0dd34333a6588509140a7fb8.fbx/" +
                            "RootNode/Door"
                        }
                    ),
                    Is.False
                );
                configure.Invoke(
                    binding,
                    new object[]
                    {
                        exactDoor,
                        hinge,
                        CreateVector3(0f, 1f, 0f),
                        -90f
                    }
                );
                Assert.That(
                    exactPath.Invoke(
                        null,
                        new[]
                        {
                            doorRoot,
                            binding,
                            "ce5f462b0dd34333a6588509140a7fb8.fbx/" +
                            "RootNode/Door"
                        }
                    ),
                    Is.True
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void SceneValidatorRejectsWrongMovingPartHierarchy()
        {
            WithCleanInteractionScene(scene =>
            {
                    InvokeTestOwnedSetupAndValidate(scene);
                object runtimeRoot = FindGameObjectInScene(
                    scene,
                    "W7PhaseInteractionAdapters"
                );
                object presentation = GetComponent(
                    runtimeRoot,
                    RuntimeType("InteractionDeterministicPresentation")
                );
                object chest = FindGameObjectInScene(scene, "chest");
                object decoy = CreateGameObject("lid");
                SetParent(GetTransform(decoy), GetTransform(chest));
                try
                {
                    object binding = presentation.GetType()
                        .GetProperty("ChestLid").GetValue(presentation);
                    binding.GetType().GetMethod("Configure").Invoke(
                        binding,
                        new object[]
                        {
                            GetTransform(decoy),
                            GetTransform(chest),
                            CreateVector3(1f, 0f, 0f),
                            -90f
                        }
                    );

                    TargetInvocationException failure = Assert.Throws<
                        TargetInvocationException>(() =>
                            InvokeValidateLoadedScene(scene)
                        );

                    Assert.That(
                        failure.InnerException.Message,
                        Does.Contain(
                            "Chest lid must use its frozen exact path"
                        )
                    );
                }
                finally
                {
                    DestroyImmediate(decoy);
                }
            });
        }

        [Test]
        public void FailedSetupRollsBackChangesToExistingObjects()
        {
            WithCleanInteractionScene(scene =>
            {
                InvokeTestOwnedSetupAndValidate(scene);
                object hint = FindGameObjectInScene(
                    scene,
                    "W7ChestOrderHint"
                );
                object label = GetComponent(
                    hint,
                    Type.GetType(
                        "UnityEngine.TextMesh, UnityEngine.TextRenderingModule",
                        throwOnError: true
                    )
                );
                object poison = CreateGameObject("W7RollbackPoison");
                SetParent(
                    GetTransform(poison),
                    GetTransform(FindGameObjectInScene(
                        scene,
                        "W7PhaseInteractionAdapters"
                    ))
                );
                try
                {
                    label.GetType().GetProperty("text")
                        .SetValue(label, "WRONG");
                    AddComponent(
                        poison,
                        UnityPhysicsType("BoxCollider")
                    );
                    object poisonRelay = AddComponent(
                        poison,
                        RuntimeType("InteractionTriggerRelay")
                    );
                    Assert.That(poisonRelay, Is.Not.Null);

                    TargetInvocationException failure = Assert.Throws<
                        TargetInvocationException>(() =>
                            InvokeSetupLoadedScene(scene)
                        );

                    Assert.That(
                        failure.InnerException,
                        Is.TypeOf<InvalidOperationException>()
                    );
                    Assert.That(
                        label.GetType().GetProperty("text").GetValue(label),
                        Is.EqualTo("WRONG"),
                        "A failed setup must Undo existing-object changes."
                    );
                }
                finally
                {
                    DestroyImmediate(poison);
                }
            });
        }

        [Test]
        public void FailedSetupRestoresReferencesWithoutEditorSubscriptions()
        {
            WithCleanInteractionScene(scene =>
            {
                InvokeTestOwnedSetupAndValidate(scene);
                object runtimeRoot = FindGameObjectInScene(
                    scene,
                    "W7PhaseInteractionAdapters"
                );
                object box = FindGameObjectInScene(scene, "box");
                object binding = GetComponent(
                    box,
                    RuntimeType("InteractionTargetBinding")
                );
                object setupAdapter = binding.GetType()
                    .GetProperty("Adapter").GetValue(binding);

                object previousAdapterObject = CreateGameObject(
                    "W7PreviousPhase1Adapter"
                );
                SetParent(
                    GetTransform(previousAdapterObject),
                    GetTransform(runtimeRoot)
                );
                object previousAdapter = AddComponent(
                    previousAdapterObject,
                    RuntimeType("PhaseOneInteractionAdapter")
                );
                object colliderObject = CreateGameObject(
                    "W7EditorSubscriptionProbe"
                );
                SetParent(
                    GetTransform(colliderObject),
                    GetTransform(runtimeRoot)
                );
                object probeCollider = AddComponent(
                    colliderObject,
                    UnityPhysicsType("BoxCollider")
                );
                binding.GetType().GetMethod("Configure").Invoke(
                    binding,
                    new object[]
                    {
                        "box_stool",
                        previousAdapter,
                        null,
                        TypedArray(
                            UnityPhysicsType("Collider"),
                            probeCollider
                        )
                    }
                );

                Assert.That(
                    binding.GetType().GetProperty("Adapter")
                        .GetValue(binding),
                    Is.SameAs(previousAdapter)
                );
                AssertEditorPublisherDoesNotReachBinding(
                    binding,
                    previousAdapter,
                    probeCollider
                );
                AssertEditorPublisherDoesNotReachBinding(
                    binding,
                    setupAdapter,
                    probeCollider
                );

                object poison = CreateGameObject("W7SubscriptionPoison");
                SetParent(GetTransform(poison), GetTransform(runtimeRoot));
                AddComponent(poison, UnityPhysicsType("BoxCollider"));
                object poisonRelay = AddComponent(
                    poison,
                    RuntimeType("InteractionTriggerRelay")
                );
                Assert.That(poisonRelay, Is.Not.Null);
                try
                {
                    Assert.Throws<TargetInvocationException>(() =>
                        InvokeSetupLoadedScene(scene)
                    );

                    Assert.That(
                        binding.GetType().GetProperty("Adapter")
                            .GetValue(binding),
                        Is.SameAs(previousAdapter),
                        "Undo must restore the pre-setup serialized reference."
                    );
                    AssertEditorPublisherDoesNotReachBinding(
                        binding,
                        previousAdapter,
                        probeCollider
                    );
                    AssertEditorPublisherDoesNotReachBinding(
                        binding,
                        setupAdapter,
                        probeCollider
                    );
                    binding.GetType().GetMethod("Configure").Invoke(
                        binding,
                        new object[]
                        {
                            "box_stool",
                            setupAdapter,
                            null,
                            TypedArray(
                                UnityPhysicsType("Collider"),
                                probeCollider
                            )
                        }
                    );
                    binding.GetType().GetMethod("Configure").Invoke(
                        binding,
                        new object[]
                        {
                            "box_stool",
                            setupAdapter,
                            null,
                            TypedArray(
                                UnityPhysicsType("Collider"),
                                probeCollider
                            )
                        }
                    );
                    AssertEditorPublisherDoesNotReachBinding(
                        binding,
                        previousAdapter,
                        probeCollider
                    );
                    AssertEditorPublisherDoesNotReachBinding(
                        binding,
                        setupAdapter,
                        probeCollider
                    );
                }
                finally
                {
                    DestroyImmediate(poison);
                }
            });
        }

        [Test]
        public void CoordinatorNeedsW1SnapshotAndSelfLocksCompletedTask()
        {
            object root = CreateGameObject("W7SnapshotAuthorityTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);

                Assert.That(
                    coordinatorType.GetProperty("CurrentPhaseId")
                        .GetValue(coordinator),
                    Is.Null
                );
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.False
                );

                SynchronizePhase(coordinator, 1);
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.True
                );

                object phaseOneAdapter = adapters.GetValue(0);
                CreateBoundTargetWithCollider(
                    rootTransform,
                    phaseOneAdapter,
                    "box_stool",
                    out object targetBinding,
                    out object targetCollider
                );
                Assert.That(
                    targetBinding.GetType().GetProperty("IsInputAvailable")
                        .GetValue(targetBinding),
                    Is.True
                );
                Assert.That(
                    targetCollider.GetType().GetProperty("enabled")
                        .GetValue(targetCollider),
                    Is.True
                );
                var resultEvents = new PublicResultEventCounter();
                SubscribePublicResultProduced(coordinator, resultEvents);

                AcceptTarget(coordinator, 1, "box_stool");

                Assert.That(
                    coordinatorType.GetProperty("CurrentPhaseId")
                        .GetValue(coordinator),
                    Is.EqualTo(1)
                );
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.False
                );
                Assert.That(
                    adapters.GetValue(0).GetType().GetProperty("IsEnabled")
                        .GetValue(adapters.GetValue(0)),
                    Is.False,
                    "A completed task must close its adapter even when " +
                    "EditMode intentionally has no CLR result subscription."
                );
                Assert.That(
                    targetBinding.GetType().GetProperty("IsInputAvailable")
                        .GetValue(targetBinding),
                    Is.False,
                    "Completion fallback did not close the target binding."
                );
                Assert.That(
                    targetCollider.GetType().GetProperty("enabled")
                        .GetValue(targetCollider),
                    Is.False,
                    "Completion fallback did not close the target collider."
                );
                Assert.That(
                    resultEvents.Count,
                    Is.Zero,
                    "EditMode input leaked through a session CLR " +
                    "subscription into Coordinator.ResultProduced."
                );

                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.False,
                    "Local Enable must not bypass a completed task lock."
                );
                phaseOneAdapter.GetType().GetMethod("Enable")
                    .Invoke(phaseOneAdapter, null);
                Assert.That(
                    phaseOneAdapter.GetType().GetProperty("IsEnabled")
                        .GetValue(phaseOneAdapter),
                    Is.False,
                    "Adapter.Enable must not bypass a completed task lock."
                );
                Assert.That(
                    targetBinding.GetType().GetProperty("IsInputAvailable")
                        .GetValue(targetBinding),
                    Is.False
                );
                Assert.That(
                    targetCollider.GetType().GetProperty("enabled")
                        .GetValue(targetCollider),
                    Is.False
                );
                Assert.That(resultEvents.Count, Is.Zero);

                SynchronizePhase(coordinator, 2);

                Assert.That(
                    coordinatorType.GetProperty("CurrentPhaseId")
                        .GetValue(coordinator),
                    Is.EqualTo(2)
                );
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.True
                );

                coordinatorType.GetMethod("Synchronize").Invoke(
                    coordinator,
                    new[] { CreateTerminalPhaseSnapshot(2) }
                );
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.False,
                    "A terminal W1 snapshot must close local interaction."
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.False,
                    "Local Enable must not bypass the terminal W1 snapshot."
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void AdapterEnableCannotBypassConfiguredCoordinatorAuthority()
        {
            WithCleanInteractionScene(scene =>
            {
                object root = CreateGameObjectInActiveScene(
                    scene,
                    "W7AdapterEnableAuthorityTest"
                );
                try
                {
                    object rootTransform = GetTransform(root);
                    Type coordinatorType = RuntimeType(
                        "InteractionPhaseCoordinator"
                    );
                    object coordinator = AddComponent(root, coordinatorType);
                    Array adapters = CreateSixAdapters(rootTransform);
                    coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                        coordinator,
                        new object[] { adapters }
                    );
                    coordinatorType.GetMethod("Configure").Invoke(
                        coordinator,
                        new[] { CreateRunPlan() }
                    );

                    object phaseOneAdapter = adapters.GetValue(0);
                    object phaseTwoAdapter = adapters.GetValue(1);
                    phaseOneAdapter.GetType().GetMethod("Enable")
                        .Invoke(phaseOneAdapter, null);
                    Assert.That(
                        phaseOneAdapter.GetType().GetProperty("IsEnabled")
                            .GetValue(phaseOneAdapter),
                        Is.False,
                        "A configured adapter must stay closed before the " +
                        "W1 snapshot selects a current phase."
                    );

                    coordinatorType.GetMethod("Enable")
                        .Invoke(coordinator, null);
                    phaseOneAdapter.GetType().GetMethod("Enable")
                        .Invoke(phaseOneAdapter, null);
                    Assert.That(
                        phaseOneAdapter.GetType().GetProperty("IsEnabled")
                            .GetValue(phaseOneAdapter),
                        Is.False,
                        "Local coordinator enable cannot replace a W1 " +
                        "snapshot."
                    );

                    SynchronizePhase(coordinator, 1);
                    Assert.That(
                        phaseOneAdapter.GetType().GetProperty("IsEnabled")
                            .GetValue(phaseOneAdapter),
                        Is.True,
                        "The selected phase must be available while " +
                        "authority is enabled."
                    );
                    phaseTwoAdapter.GetType().GetMethod("Enable")
                        .Invoke(phaseTwoAdapter, null);
                    Assert.That(
                        phaseTwoAdapter.GetType().GetProperty("IsEnabled")
                            .GetValue(phaseTwoAdapter),
                        Is.False,
                        "A future phase must not be opened by its public " +
                        "Enable."
                    );

                    coordinatorType.GetMethod("Disable")
                        .Invoke(coordinator, null);
                    phaseOneAdapter.GetType().GetMethod("Enable")
                        .Invoke(phaseOneAdapter, null);
                    phaseTwoAdapter.GetType().GetMethod("Enable")
                        .Invoke(phaseTwoAdapter, null);
                    Assert.That(
                        phaseOneAdapter.GetType().GetProperty("IsEnabled")
                            .GetValue(phaseOneAdapter),
                        Is.False,
                        "The current adapter must respect a disabled " +
                        "authority."
                    );
                    Assert.That(
                        phaseTwoAdapter.GetType().GetProperty("IsEnabled")
                            .GetValue(phaseTwoAdapter),
                        Is.False,
                        "A non-current adapter must remain closed while the " +
                        "authority is disabled."
                    );

                    object standaloneObject =
                        CreateGameObjectInActiveScene(
                            scene,
                            "StandalonePhaseOneAdapter"
                        );
                    SetParent(
                        GetTransform(standaloneObject),
                        rootTransform
                    );
                    object standalone = AddComponent(
                        standaloneObject,
                        RuntimeType("PhaseOneInteractionAdapter")
                    );
                    standalone.GetType().GetMethod("Enable")
                        .Invoke(standalone, null);
                    Assert.That(
                        standalone.GetType().GetProperty("IsEnabled")
                            .GetValue(standalone),
                        Is.True,
                        "An intentionally standalone adapter has no " +
                        "coordinator authority to violate."
                    );
                }
                finally
                {
                    DestroyImmediate(root);
                }
            });
        }

        [Test]
        public void GiveUpFallbackLocksAdapterWithoutEditorResultForwarding()
        {
            object root = CreateGameObject("W7GiveUpFallbackAuthorityTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 1, giveUpAvailable: true);

                object phaseOneAdapter = adapters.GetValue(0);
                CreateBoundTargetWithCollider(
                    rootTransform,
                    phaseOneAdapter,
                    "box_stool",
                    out object targetBinding,
                    out object targetCollider
                );
                Assert.That(
                    targetBinding.GetType().GetProperty("IsInputAvailable")
                        .GetValue(targetBinding),
                    Is.True
                );
                Assert.That(
                    targetCollider.GetType().GetProperty("enabled")
                        .GetValue(targetCollider),
                    Is.True
                );
                var resultEvents = new PublicResultEventCounter();
                SubscribePublicResultProduced(coordinator, resultEvents);

                object result = GiveUp(coordinator, 1);
                Assert.That(
                    result.GetType().GetProperty("PhaseGivenUp")
                        .GetValue(result),
                    Is.True
                );
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.False
                );
                Assert.That(
                    phaseOneAdapter.GetType().GetProperty("IsEnabled")
                        .GetValue(phaseOneAdapter),
                    Is.False,
                    "GiveUp fallback did not close the phase adapter."
                );
                Assert.That(
                    targetBinding.GetType().GetProperty("IsInputAvailable")
                        .GetValue(targetBinding),
                    Is.False,
                    "GiveUp fallback did not close the target binding."
                );
                Assert.That(
                    targetCollider.GetType().GetProperty("enabled")
                        .GetValue(targetCollider),
                    Is.False,
                    "GiveUp fallback did not close the target collider."
                );
                Assert.That(
                    resultEvents.Count,
                    Is.Zero,
                    "EditMode GiveUp leaked through a session CLR " +
                    "subscription into Coordinator.ResultProduced."
                );

                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                phaseOneAdapter.GetType().GetMethod("Enable")
                    .Invoke(phaseOneAdapter, null);
                targetBinding.GetType().GetProperty("enabled")
                    .SetValue(targetBinding, false);
                targetBinding.GetType().GetProperty("enabled")
                    .SetValue(targetBinding, true);
                Assert.That(
                    targetBinding.GetType().GetProperty("enabled")
                        .GetValue(targetBinding),
                    Is.True,
                    "The binding re-enable counterexample did not execute."
                );
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.False,
                    "Coordinator.Enable must not bypass a GiveUp lock."
                );
                Assert.That(
                    phaseOneAdapter.GetType().GetProperty("IsEnabled")
                        .GetValue(phaseOneAdapter),
                    Is.False,
                    "Adapter.Enable must not bypass a GiveUp lock."
                );
                Assert.That(
                    targetBinding.GetType().GetProperty("IsInputAvailable")
                        .GetValue(targetBinding),
                    Is.False
                );
                Assert.That(
                    targetCollider.GetType().GetProperty("enabled")
                        .GetValue(targetCollider),
                    Is.False,
                    "Binding re-enable must not reopen a GiveUp-locked " +
                    "collider."
                );
                Assert.That(
                    targetBinding.GetType().GetMethod("AcceptInput")
                        .Invoke(targetBinding, null),
                    Is.Null
                );
                Assert.That(resultEvents.Count, Is.Zero);
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void TriggerRelayAcceptsOnlyConfiguredBareHandRoot()
        {
            object root = CreateGameObject("W7BareHandRelayTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 1);

                object target = CreateGameObject("box_stool");
                SetParent(GetTransform(target), rootTransform);
                object binding = AddComponent(
                    target,
                    RuntimeType("InteractionTargetBinding")
                );
                binding.GetType().GetMethod("Configure").Invoke(
                    binding,
                    new object[]
                    {
                        "box_stool",
                        adapters.GetValue(0),
                        null,
                        null
                    }
                );

                object handRoot = CreateGameObject("HandInteractorsLeft");
                SetParent(GetTransform(handRoot), rootTransform);
                object handColliderObject = CreateGameObject("HandCapsule");
                SetParent(GetTransform(handColliderObject), GetTransform(handRoot));
                object handCollider = AddComponent(
                    handColliderObject,
                    UnityPhysicsType("BoxCollider")
                );
                object stranger = CreateGameObject("UntrustedCollider");
                SetParent(GetTransform(stranger), rootTransform);
                object strangerCollider = AddComponent(
                    stranger,
                    UnityPhysicsType("BoxCollider")
                );

                object relayObject = CreateGameObject("Relay");
                SetParent(GetTransform(relayObject), rootTransform);
                AddComponent(relayObject, UnityPhysicsType("BoxCollider"));
                object relay = AddComponent(
                    relayObject,
                    RuntimeType("InteractionTriggerRelay")
                );
                Array allowedRoots = Array.CreateInstance(
                    UnityType("Transform"),
                    1
                );
                allowedRoots.SetValue(GetTransform(handRoot), 0);
                relay.GetType().GetMethod("Configure").Invoke(
                    relay,
                    new object[] { binding, allowedRoots, 0.05f }
                );

                Assert.That(
                    relay.GetType().GetMethod("IsAllowedInteractor").Invoke(
                        relay,
                        new[] { strangerCollider }
                    ),
                    Is.False
                );
                Assert.That(
                    relay.GetType().GetMethod("AcceptTrigger").Invoke(
                        relay,
                        new[] { strangerCollider }
                    ),
                    Is.Null
                );
                object accepted = relay.GetType()
                    .GetMethod("AcceptTrigger")
                    .Invoke(relay, new[] { handCollider });
                Assert.That(accepted, Is.Not.Null);
                Assert.That(
                    accepted.GetType().GetProperty("Accepted")
                        .GetValue(accepted),
                    Is.True
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void PhaseTwoCoinKeepsGravityOffAndCorrectPlacementSnapLocks()
        {
            object root = CreateGameObject("W7CoinPhysicsTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 2);

                object coin = CreateGameObject("coin_dragon");
                SetParent(GetTransform(coin), rootTransform);
                object body = AddComponent(
                    coin,
                    UnityPhysicsType("Rigidbody")
                );
                body.GetType().GetProperty("isKinematic")
                    .SetValue(body, true);
                body.GetType().GetProperty("useGravity")
                    .SetValue(body, false);
                object coinCollider = AddComponent(
                    coin,
                    UnityPhysicsType("BoxCollider")
                );
                object coinBinding = AddComponent(
                    coin,
                    RuntimeType("InteractionTargetBinding")
                );
                coinBinding.GetType().GetMethod("ConfigureMovable").Invoke(
                    coinBinding,
                    new object[]
                    {
                        "coin_dragon",
                        adapters.GetValue(1),
                        TypedArray(UnityType("Behaviour")),
                        TypedArray(
                            UnityPhysicsType("Collider"),
                            coinCollider
                        ),
                        TypedArray(UnityType("GameObject"))
                    }
                );

                Assert.That(
                    GetComponent(coin, UnityPhysicsType("Rigidbody")),
                    Is.SameAs(body),
                    "Phase 2 must retain the authored Rigidbody."
                );
                Assert.That(
                    body.GetType().GetProperty("isKinematic")
                        .GetValue(body),
                    Is.False,
                    "The available coin must be movable."
                );
                Assert.That(
                    body.GetType().GetProperty("useGravity")
                        .GetValue(body),
                    Is.False,
                    "Phase 2 availability must not turn coin gravity on."
                );

                object plate = CreateGameObject("plate_dragon");
                SetParent(GetTransform(plate), rootTransform);
                object placementCollider = AddComponent(
                    plate,
                    UnityPhysicsType("BoxCollider")
                );
                object snap = CreateGameObject("SnapPoint");
                SetParent(GetTransform(snap), GetTransform(plate));
                GetTransform(snap).GetType().GetProperty("position")
                    .SetValue(GetTransform(snap), CreateVector3(8f, 9f, 10f));
                object placement = AddComponent(
                    plate,
                    RuntimeType("InteractionPlacementBinding")
                );
                placement.GetType().GetMethod("Configure").Invoke(
                    placement,
                    new object[]
                    {
                        "plate_dragon",
                        adapters.GetValue(1),
                        placementCollider,
                        GetTransform(snap)
                    }
                );

                object result = placement.GetType()
                    .GetMethod("AcceptTrigger").Invoke(
                        placement,
                        new[] { coinCollider }
                    );
                Assert.That(result, Is.Not.Null);
                Assert.That(
                    result.GetType().GetProperty("Accepted").GetValue(result),
                    Is.True
                );
                Assert.That(
                    body.GetType().GetProperty("isKinematic")
                        .GetValue(body),
                    Is.True,
                    "A correct placement must lock the coin."
                );
                Assert.That(
                    body.GetType().GetProperty("useGravity")
                        .GetValue(body),
                    Is.False
                );
                AssertVector3(
                    GetTransform(coin).GetType().GetProperty("position")
                        .GetValue(GetTransform(coin)),
                    8f,
                    9f,
                    10f,
                    "A correct placement must snap to the plate."
                );
                Type.GetType(
                    "UnityEngine.TestTools.LogAssert, UnityEngine.TestRunner",
                    throwOnError: true
                ).GetMethod(
                    "NoUnexpectedReceived",
                    BindingFlags.Public | BindingFlags.Static
                ).Invoke(null, null);
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void TargetContactCycleDeduplicatesEveryPublicInputPerTarget()
        {
            object root = CreateGameObject("W7TargetContactCycleTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 4);

                object leftRoot = CreateGameObject("LeftHandInteractor");
                SetParent(GetTransform(leftRoot), rootTransform);
                object leftFirstObject = CreateGameObject("LeftColliderA");
                SetParent(GetTransform(leftFirstObject), GetTransform(leftRoot));
                object leftFirst = AddComponent(
                    leftFirstObject,
                    UnityPhysicsType("BoxCollider")
                );
                object leftSecondObject = CreateGameObject("LeftColliderB");
                SetParent(GetTransform(leftSecondObject), GetTransform(leftRoot));
                object leftSecond = AddComponent(
                    leftSecondObject,
                    UnityPhysicsType("BoxCollider")
                );
                object rightRoot = CreateGameObject("RightHandInteractor");
                SetParent(GetTransform(rightRoot), rootTransform);
                object rightColliderObject = CreateGameObject("RightCollider");
                SetParent(
                    GetTransform(rightColliderObject),
                    GetTransform(rightRoot)
                );
                object right = AddComponent(
                    rightColliderObject,
                    UnityPhysicsType("BoxCollider")
                );
                Array allowedRoots = Array.CreateInstance(
                    UnityType("Transform"),
                    2
                );
                allowedRoots.SetValue(GetTransform(leftRoot), 0);
                allowedRoots.SetValue(GetTransform(rightRoot), 1);

                object blueObject = CreateGameObject("W7Target_key_b");
                SetParent(GetTransform(blueObject), rootTransform);
                object blueCollider = AddComponent(
                    blueObject,
                    UnityPhysicsType("BoxCollider")
                );
                object blueBinding = AddComponent(
                    blueObject,
                    RuntimeType("InteractionTargetBinding")
                );
                blueBinding.GetType().GetMethod("Configure").Invoke(
                    blueBinding,
                    new object[]
                    {
                        "key_b",
                        adapters.GetValue(3),
                        null,
                        TypedArray(
                            UnityPhysicsType("Collider"),
                            blueCollider
                        )
                    }
                );
                object blueRelay = AddComponent(
                    blueObject,
                    RuntimeType("InteractionTriggerRelay")
                );
                blueRelay.GetType().GetMethod("Configure").Invoke(
                    blueRelay,
                    new object[] { blueBinding, allowedRoots, 0f }
                );

                object redObject = CreateGameObject(
                    "W7Target_motorbike_key"
                );
                SetParent(GetTransform(redObject), rootTransform);
                object redCollider = AddComponent(
                    redObject,
                    UnityPhysicsType("BoxCollider")
                );
                object redBinding = AddComponent(
                    redObject,
                    RuntimeType("InteractionTargetBinding")
                );
                redBinding.GetType().GetMethod("Configure").Invoke(
                    redBinding,
                    new object[]
                    {
                        "motorbike_key",
                        adapters.GetValue(3),
                        null,
                        TypedArray(
                            UnityPhysicsType("Collider"),
                            redCollider
                        )
                    }
                );

                object first = blueRelay.GetType()
                    .GetMethod("AcceptTrigger").Invoke(
                        blueRelay,
                        new[] { leftFirst }
                    );
                Assert.That(first, Is.Not.Null);
                Assert.That(
                    blueRelay.GetType().GetMethod("AcceptTrigger").Invoke(
                        blueRelay,
                        new[] { leftSecond }
                    ),
                    Is.Null
                );
                Assert.That(
                    blueRelay.GetType().GetMethod("AcceptTrigger").Invoke(
                        blueRelay,
                        new[] { right }
                    ),
                    Is.Null
                );
                Assert.That(
                    blueBinding.GetType().GetMethod("AcceptInput").Invoke(
                        blueBinding,
                        null
                    ),
                    Is.Null,
                    "The direct/Poke seam must share the trigger latch."
                );
                Assert.That(
                    redBinding.GetType().GetMethod("AcceptInput").Invoke(
                        redBinding,
                        null
                    ),
                    Is.Not.Null,
                    "A separate target must own a separate gate."
                );

                blueRelay.GetType().GetMethod("ReleaseTrigger").Invoke(
                    blueRelay,
                    new[] { leftFirst }
                );
                blueRelay.GetType().GetMethod("ReleaseTrigger").Invoke(
                    blueRelay,
                    new[] { leftSecond }
                );
                Assert.That(
                    blueRelay.GetType().GetMethod("AcceptTrigger").Invoke(
                        blueRelay,
                        new[] { leftFirst }
                    ),
                    Is.Null,
                    "The right hand still holds the contact cycle."
                );
                blueRelay.GetType().GetMethod("ReleaseTrigger").Invoke(
                    blueRelay,
                    new[] { right }
                );
                blueRelay.GetType().GetMethod("ReleaseTrigger").Invoke(
                    blueRelay,
                    new[] { leftFirst }
                );
                Assert.That(
                    blueRelay.GetType().GetMethod("AcceptTrigger").Invoke(
                        blueRelay,
                        new[] { leftSecond }
                    ),
                    Is.Not.Null,
                    "All colliders exiting must rearm the target."
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void DisabledTargetAndKeypadBindingsCloseAndRejectInputs()
        {
            object root = CreateGameObject("W7DisabledInputBoundaryTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 1);

                object targetObject = CreateGameObject("box_stool");
                SetParent(GetTransform(targetObject), rootTransform);
                object targetCollider = AddComponent(
                    targetObject,
                    UnityPhysicsType("BoxCollider")
                );
                object target = AddComponent(
                    targetObject,
                    RuntimeType("InteractionTargetBinding")
                );
                target.GetType().GetMethod("Configure").Invoke(
                    target,
                    new object[]
                    {
                        "box_stool",
                        adapters.GetValue(0),
                        null,
                        TypedArray(
                            UnityPhysicsType("Collider"),
                            targetCollider
                        )
                    }
                );

                object digit = CreateKeypadBinding(
                    rootTransform,
                    "InteractionDigitBinding",
                    adapters.GetValue(0),
                    out object digitCollider,
                    1
                );
                object backspace = CreateKeypadBinding(
                    rootTransform,
                    "InteractionPasswordBackspaceBinding",
                    adapters.GetValue(0),
                    out object backspaceCollider
                );
                object submit = CreateKeypadBinding(
                    rootTransform,
                    "InteractionPasswordSubmitBinding",
                    adapters.GetValue(0),
                    out object submitCollider
                );

                AssertDisabledInputBoundary(
                    coordinator,
                    target,
                    targetCollider
                );
                AssertDisabledInputBoundary(
                    coordinator,
                    digit,
                    digitCollider
                );
                AssertDisabledInputBoundary(
                    coordinator,
                    backspace,
                    backspaceCollider
                );
                AssertDisabledInputBoundary(
                    coordinator,
                    submit,
                    submitCollider
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void DisabledConfigureCannotResubscribeOrReopenInputsUntilEnable()
        {
            object root = CreateGameObject("W7DisabledConfigureBoundaryTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                object phaseOneAdapter = adapters.GetValue(0);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 1);

                object targetObject = CreateGameObject("box_stool");
                SetParent(GetTransform(targetObject), rootTransform);
                object targetCollider = AddComponent(
                    targetObject,
                    UnityPhysicsType("BoxCollider")
                );
                object targetBehaviour = AddComponent(
                    targetObject,
                    RuntimeType("InteractionFeedbackPresenter")
                );
                object target = AddComponent(
                    targetObject,
                    RuntimeType("InteractionTargetBinding")
                );
                ReconfigureBinding(
                    target,
                    phaseOneAdapter,
                    targetCollider,
                    targetBehaviour
                );

                AssertDisabledConfigureBoundary(
                    target,
                    phaseOneAdapter,
                    targetCollider,
                    targetBehaviour
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void PlacementIgnoresNonCoinsAndDeduplicatesCoinColliders()
        {
            object root = CreateGameObject("W7PlacementDedupeTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 2);

                object placementObject = CreateGameObject("plate_a");
                SetParent(GetTransform(placementObject), rootTransform);
                object placementCollider = AddComponent(
                    placementObject,
                    UnityPhysicsType("BoxCollider")
                );
                object snap = CreateGameObject("SnapPoint");
                SetParent(GetTransform(snap), GetTransform(placementObject));
                object placement = AddComponent(
                    placementObject,
                    RuntimeType("InteractionPlacementBinding")
                );
                placement.GetType().GetMethod("Configure").Invoke(
                    placement,
                    new object[]
                    {
                        "plate_a",
                        adapters.GetValue(1),
                        placementCollider,
                        GetTransform(snap)
                    }
                );

                object ignored = CreateBoundTarget(
                    rootTransform,
                    adapters.GetValue(1),
                    "plate_dragon"
                );
                object ignoredCollider = AddComponent(
                    ignored,
                    UnityPhysicsType("BoxCollider")
                );
                object coin = CreateBoundTarget(
                    rootTransform,
                    adapters.GetValue(1),
                    "coin_dragon"
                );
                object firstCollider = AddComponent(
                    coin,
                    UnityPhysicsType("BoxCollider")
                );
                object secondCollider = AddComponent(
                    coin,
                    UnityPhysicsType("BoxCollider")
                );

                Assert.That(
                    placement.GetType().GetMethod("AcceptTrigger").Invoke(
                        placement,
                        new[] { ignoredCollider }
                    ),
                    Is.Null
                );
                object first = placement.GetType()
                    .GetMethod("AcceptTrigger")
                    .Invoke(placement, new[] { firstCollider });
                object duplicate = placement.GetType()
                    .GetMethod("AcceptTrigger")
                    .Invoke(placement, new[] { secondCollider });
                Assert.That(first, Is.Not.Null);
                Assert.That(
                    first.GetType().GetProperty("InteractionError")
                        .GetValue(first),
                    Is.True,
                    "coin_dragon on plate_a is intentionally the wrong pair."
                );
                Assert.That(duplicate, Is.Null);
                placement.GetType().GetMethod("ReleaseTrigger").Invoke(
                    placement,
                    new[] { firstCollider }
                );
                Assert.That(
                    placement.GetType().GetMethod("AcceptTrigger").Invoke(
                        placement,
                        new[] { firstCollider }
                    ),
                    Is.Null,
                    "One collider still overlapping must keep the coin latched."
                );
                placement.GetType().GetMethod("ReleaseTrigger").Invoke(
                    placement,
                    new[] { secondCollider }
                );
                placement.GetType().GetMethod("ReleaseTrigger").Invoke(
                    placement,
                    new[] { firstCollider }
                );
                Assert.That(
                    placement.GetType().GetMethod("AcceptTrigger").Invoke(
                        placement,
                        new[] { secondCollider }
                    ),
                    Is.Not.Null,
                    "A full exit must rearm the coin placement contact cycle."
                );
                Assert.That(
                    placement.GetType().GetMethod(
                        "AcceptPlacement",
                        new[] { typeof(string) }
                    ).Invoke(placement, new object[] { "box_stool" }),
                    Is.Null
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void DeprecatedPlanHintsStayHiddenAcrossLifecycle()
        {
            object root = CreateGameObject("W7HintLifecycleTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                object plan = CreateRunPlan();
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { plan }
                );

                object safeText = CreateTextMesh(rootTransform, "SafeHint");
                object chestText = CreateTextMesh(rootTransform, "ChestHint");
                object hints = AddComponent(
                    root,
                    RuntimeType("InteractionPlanHintPresenter")
                );
                hints.GetType().GetMethod("Configure").Invoke(
                    hints,
                    new[] { coordinator, safeText, chestText }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 1);

                DispatchHintResult(
                    hints,
                    AcceptTarget(coordinator, 1, "box_stool")
                );
                Assert.That(IsComponentGameObjectActive(safeText), Is.False);
                DispatchHintResult(
                    hints,
                    AcceptInput(
                        coordinator,
                        1,
                        CoreType("PhaseInput").GetMethod("Submit")
                            .Invoke(null, null)
                    )
                );
                Assert.That(IsComponentGameObjectActive(safeText), Is.False);

                DispatchHintResult(
                    hints,
                    AcceptTarget(coordinator, 1, "box_stool")
                );
                for (int digit = 1; digit <= 4; digit++)
                {
                    AcceptInput(
                        coordinator,
                        1,
                        CoreType("PhaseInput").GetMethod("Digit").Invoke(
                            null,
                            new object[] { digit }
                        )
                    );
                }
                DispatchHintResult(
                    hints,
                    AcceptInput(
                        coordinator,
                        1,
                        CoreType("PhaseInput").GetMethod("Submit")
                            .Invoke(null, null)
                    )
                );
                Assert.That(IsComponentGameObjectActive(safeText), Is.False);

                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { plan }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 1);
                DispatchHintResult(
                    hints,
                    AcceptTarget(coordinator, 1, "box_stool")
                );
                SynchronizePhase(coordinator, 1, giveUpAvailable: true);
                DispatchHintResult(hints, GiveUp(coordinator, 1));
                Assert.That(IsComponentGameObjectActive(safeText), Is.False);

                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { plan }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 3, giveUpAvailable: true);
                DispatchHintResult(hints, GiveUp(coordinator, 3));
                Assert.That(IsComponentGameObjectActive(chestText), Is.False);

                SynchronizePhase(coordinator, 4);
                DispatchHintResult(
                    hints,
                    AcceptTarget(coordinator, 4, "key_a")
                );
                Assert.That(IsComponentGameObjectActive(chestText), Is.False);

                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { plan }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 3, giveUpAvailable: true);
                DispatchHintResult(hints, GiveUp(coordinator, 3));
                Assert.That(IsComponentGameObjectActive(chestText), Is.False);
                SynchronizePhase(coordinator, 4, giveUpAvailable: true);
                DispatchHintResult(hints, GiveUp(coordinator, 4));
                Assert.That(IsComponentGameObjectActive(chestText), Is.False);
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void PhaseFourGiveUpKeepsKeyModelVisibleAndVisualOnly()
        {
            object root = CreateGameObject("W7PhaseFourGiveUpFallbackTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );

                object lid = CreateVisual(rootTransform, "ChestLid");
                object hinge = CreateVisual(rootTransform, "ChestHinge");
                Type hingeType = RuntimeType("DeterministicHingeBinding");
                object chestHinge = Activator.CreateInstance(hingeType);
                hingeType.GetMethod("Configure").Invoke(
                    chestHinge,
                    new object[]
                    {
                        lid,
                        hinge,
                        CreateVector3(1f, 0f, 0f),
                        -90f
                    }
                );

                object key = CreateGameObject("key_a");
                SetParent(GetTransform(key), rootTransform);
                object keyBody = AddComponent(
                    key,
                    UnityPhysicsType("Rigidbody")
                );
                object keyCollider = AddComponent(
                    key,
                    UnityPhysicsType("BoxCollider")
                );
                Type keyBindingType = RuntimeType(
                    "PlannedKeyReleaseBinding"
                );
                object keyBinding = Activator.CreateInstance(keyBindingType);
                Array bodies = TypedArray(
                    UnityPhysicsType("Rigidbody"),
                    keyBody
                );
                Array behaviours = Array.CreateInstance(
                    UnityType("Behaviour"),
                    0
                );
                Array colliders = TypedArray(
                    UnityPhysicsType("Collider"),
                    keyCollider
                );
                keyBindingType.GetMethod("Configure").Invoke(
                    keyBinding,
                    new object[]
                    {
                        "key_a",
                        key,
                        bodies,
                        behaviours,
                        colliders
                    }
                );

                Type presentationType = RuntimeType(
                    "InteractionDeterministicPresentation"
                );
                object presentation = AddComponent(root, presentationType);
                Type stateType = RuntimeType(
                    "DeterministicTargetStateBinding"
                );
                Array noStates = Array.CreateInstance(stateType, 0);
                Array keys = TypedArray(keyBindingType, keyBinding);
                presentationType.GetMethod("Configure").Invoke(
                    presentation,
                    new object[]
                    {
                        coordinator,
                        Activator.CreateInstance(hingeType),
                        chestHinge,
                        Activator.CreateInstance(hingeType),
                        Activator.CreateInstance(hingeType),
                        Activator.CreateInstance(hingeType),
                        noStates,
                        noStates,
                        noStates,
                        keys
                    }
                );

                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 4, giveUpAvailable: true);
                object result = GiveUp(coordinator, 4);
                presentationType.GetMethod("RebuildFromAuthority")
                    .Invoke(presentation, null);

                Assert.That(
                    result.GetType().GetProperty("PhaseGivenUp")
                        .GetValue(result),
                    Is.True
                );
                Assert.That(
                    RotationAngleFromIdentity(lid),
                    Is.GreaterThan(0.1f)
                );
                Assert.That(
                    keyBody.GetType().GetProperty("isKinematic")
                        .GetValue(keyBody),
                    Is.True
                );
                Assert.That(
                    keyBody.GetType().GetProperty("useGravity")
                        .GetValue(keyBody),
                    Is.False
                );
                Assert.That(
                    keyCollider.GetType().GetProperty("enabled")
                        .GetValue(keyCollider),
                    Is.False
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void PhaseFiveProgressResetRestoresAllCabinetButtonVisuals()
        {
            object root = CreateGameObject("W7PhaseFiveVisualResetTest");
            try
            {
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                object rootTransform = GetTransform(root);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );

                object buttonA = CreateVisual(rootTransform, "button_a");
                object buttonB = CreateVisual(rootTransform, "button_b");
                object buttonC = CreateVisual(rootTransform, "button_c");
                Array cabinetBindings = CreateTargetStateBindings(
                    new[] { buttonA, buttonB, buttonC },
                    new[] { "button_a", "button_b", "button_c" }
                );
                object presentation = ConfigurePresentation(
                    root,
                    coordinator,
                    cabinetBindings
                );

                object runPlan = CreateRunPlan();
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { runPlan }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                AdvanceToPhaseFive(coordinator);

                AcceptTarget(coordinator, 5, "button_a");
                AcceptTarget(coordinator, 5, "button_b");
                presentation.GetType().GetMethod("RebuildFromAuthority")
                    .Invoke(presentation, null);
                Assert.That(
                    RotationAngleFromIdentity(buttonA),
                    Is.GreaterThan(0.1f)
                );
                Assert.That(
                    RotationAngleFromIdentity(buttonB),
                    Is.GreaterThan(0.1f)
                );

                object resetResult = AcceptTarget(
                    coordinator,
                    5,
                    "button_b"
                );
                presentation.GetType().GetMethod("RebuildFromAuthority")
                    .Invoke(presentation, null);

                Assert.That(
                    (bool)resetResult.GetType()
                        .GetProperty("ProgressReset")
                        .GetValue(resetResult),
                    Is.True
                );
                Assert.That(
                    (int)resetResult.GetType()
                        .GetProperty("Progress")
                        .GetValue(resetResult),
                    Is.EqualTo(0)
                );
                Assert.That(
                    RotationAngleFromIdentity(buttonA),
                    Is.LessThan(0.001f)
                );
                Assert.That(
                    RotationAngleFromIdentity(buttonB),
                    Is.LessThan(0.001f)
                );
                Assert.That(
                    RotationAngleFromIdentity(buttonC),
                    Is.LessThan(0.001f)
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void DisabledPresentationRebuildsFromAuthorityWhenEnabled()
        {
            object root = CreateGameObject("W7DisabledPresentationTest");
            try
            {
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                object rootTransform = GetTransform(root);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                object buttonA = CreateVisual(rootTransform, "button_a");
                Array bindings = CreateTargetStateBindings(
                    new[] { buttonA },
                    new[] { "button_a" }
                );
                object presentation = ConfigurePresentation(
                    root,
                    coordinator,
                    bindings
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                AdvanceToPhaseFive(coordinator);
                presentation.GetType().GetProperty("enabled")
                    .SetValue(presentation, false);

                AcceptTarget(coordinator, 5, "key_a");
                AcceptTarget(coordinator, 5, "button_a");

                Assert.That(
                    RotationAngleFromIdentity(buttonA),
                    Is.LessThan(0.001f)
                );

                presentation.GetType().GetProperty("enabled")
                    .SetValue(presentation, true);

                presentation.GetType().GetMethod("RebuildFromAuthority")
                    .Invoke(presentation, null);

                Assert.That(
                    RotationAngleFromIdentity(buttonA),
                    Is.GreaterThan(0.1f),
                    "OnEnable must rebuild missed progress from W7 authority."
                );
                int editorInvocationCount =
                    GetSubscriptionInvocationCount(presentation);
                coordinatorType.GetMethod("Reset").Invoke(coordinator, null);
                Assert.That(
                    GetSubscriptionInvocationCount(presentation),
                    Is.EqualTo(editorInvocationCount),
                    "EditMode must not attach runtime presentation handlers."
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void ReenabledPresentationRestoresDirectKeyPhaseVisuals()
        {
            object root = CreateGameObject("W7PresentationRehydrateTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );

                object lid = CreateVisual(rootTransform, "ChestLid");
                object hinge = CreateVisual(rootTransform, "ChestHinge");
                Type hingeType = RuntimeType("DeterministicHingeBinding");
                object chestHinge = Activator.CreateInstance(hingeType);
                hingeType.GetMethod("Configure").Invoke(
                    chestHinge,
                    new object[]
                    {
                        lid,
                        hinge,
                        CreateVector3(1f, 0f, 0f),
                        -90f
                    }
                );

                object[] buttonTransforms =
                {
                    CreateVisual(rootTransform, "blue"),
                    CreateVisual(rootTransform, "red"),
                    CreateVisual(rootTransform, "yellow"),
                    CreateVisual(rootTransform, "green")
                };
                Array chestButtons = CreateTargetStateBindings(
                    buttonTransforms,
                    new[] { "blue", "red", "yellow", "green" }
                );

                object key = CreateGameObject("key_a");
                SetParent(GetTransform(key), rootTransform);
                object keyBody = AddComponent(
                    key,
                    UnityPhysicsType("Rigidbody")
                );
                object keyCollider = AddComponent(
                    key,
                    UnityPhysicsType("BoxCollider")
                );
                object keyProxy = CreateGameObject("W7Target_key_a");
                SetParent(GetTransform(keyProxy), rootTransform);
                Type keyType = RuntimeType("PlannedKeyReleaseBinding");
                object keyBinding = Activator.CreateInstance(keyType);
                keyType.GetMethod("ConfigureVisualOnly").Invoke(
                    keyBinding,
                    new object[]
                    {
                        "key_a",
                        key,
                        keyProxy,
                        TypedArray(
                            UnityPhysicsType("Rigidbody"),
                            keyBody
                        ),
                        Array.CreateInstance(UnityType("Behaviour"), 0),
                        TypedArray(
                            UnityPhysicsType("Collider"),
                            keyCollider
                        )
                    }
                );

                Type presentationType = RuntimeType(
                    "InteractionDeterministicPresentation"
                );
                Type stateType = RuntimeType(
                    "DeterministicTargetStateBinding"
                );
                object presentation = AddComponent(root, presentationType);
                Array noStates = Array.CreateInstance(stateType, 0);
                presentationType.GetMethod("Configure").Invoke(
                    presentation,
                    new object[]
                    {
                        coordinator,
                        Activator.CreateInstance(hingeType),
                        chestHinge,
                        Activator.CreateInstance(hingeType),
                        Activator.CreateInstance(hingeType),
                        Activator.CreateInstance(hingeType),
                        chestButtons,
                        noStates,
                        noStates,
                        TypedArray(keyType, keyBinding)
                    }
                );

                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 4);
                presentationType.GetMethod("RebuildFromAuthority")
                    .Invoke(presentation, null);
                Assert.That(
                    keyBody.GetType().GetProperty("isKinematic")
                        .GetValue(keyBody),
                    Is.True,
                    "The planned key lock baseline was not established."
                );
                Assert.That(
                    keyCollider.GetType().GetProperty("enabled")
                        .GetValue(keyCollider),
                    Is.False,
                    "The planned key collider baseline was not established."
                );
                presentationType.GetProperty("enabled")
                    .SetValue(presentation, false);

                object accepted = AcceptTarget(coordinator, 4, "key_a");
                Assert.That(
                    accepted.GetType().GetProperty("Accepted")
                        .GetValue(accepted),
                    Is.True
                );

                Assert.That(
                    RotationAngleFromIdentity(lid),
                    Is.GreaterThan(0.1f)
                );
                foreach (object button in buttonTransforms)
                {
                    Assert.That(
                        IsComponentGameObjectActive(button),
                        Is.False
                    );
                }
                Assert.That(
                    keyCollider.GetType().GetProperty("enabled")
                        .GetValue(keyCollider),
                    Is.False
                );

                presentationType.GetProperty("enabled")
                    .SetValue(presentation, true);

                presentationType.GetMethod("RebuildFromAuthority")
                    .Invoke(presentation, null);

                Assert.That(
                    RotationAngleFromIdentity(lid),
                    Is.GreaterThan(0.1f)
                );
                foreach (object button in buttonTransforms)
                {
                    Assert.That(
                        IsComponentGameObjectActive(button),
                        Is.False
                    );
                }
                Assert.That(
                    keyBody.GetType().GetProperty("isKinematic")
                        .GetValue(keyBody),
                    Is.True
                );
                Assert.That(
                    keyCollider.GetType().GetProperty("enabled")
                        .GetValue(keyCollider),
                    Is.False
                );
                Assert.That(
                    IsComponentGameObjectActive(GetTransform(keyProxy)),
                    Is.True
                );
                int editorInvocationCount =
                    GetSubscriptionInvocationCount(presentation);
                coordinatorType.GetMethod("Reset").Invoke(coordinator, null);
                Assert.That(
                    GetSubscriptionInvocationCount(presentation),
                    Is.EqualTo(editorInvocationCount),
                    "EditMode must not attach runtime presentation handlers."
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        private static void AssertGuardSceneRejected(
            string temporaryFolderName,
            string temporarySceneFileName,
            int markerCount,
            string expectedMessage)
        {
            string fullScenePath = Path.GetFullPath(
                InteractionSceneAssetPath
            );
            byte[] bytesBefore = File.ReadAllBytes(fullScenePath);
            string hashBefore = ComputeSha256(bytesBefore);
            Type sceneManager = EditorType(
                "UnityEditor.SceneManagement.EditorSceneManager"
            );
            Type assetDatabase = EditorType("UnityEditor.AssetDatabase");
            Type openModeType = EditorType(
                "UnityEditor.SceneManagement.OpenSceneMode"
            );
            object additive = Enum.Parse(openModeType, "Additive");
            object previousActiveScene = GetActiveScene();
            string temporaryFolderPath = "Assets/" + temporaryFolderName;
            string temporaryScenePath = temporaryFolderPath + "/" +
                temporarySceneFileName;
            object scene = null;
            bool temporaryFolderCreated = false;
            ExceptionDispatchInfo primaryFailure = null;
            try
            {
                string folderGuid = (string)assetDatabase.GetMethod(
                    "CreateFolder",
                    new[] { typeof(string), typeof(string) }
                ).Invoke(
                    null,
                    new object[] { "Assets", temporaryFolderName }
                );
                if (string.IsNullOrWhiteSpace(folderGuid))
                {
                    throw new InvalidOperationException(
                        "Unity could not create a guard-test asset folder."
                    );
                }
                temporaryFolderCreated = true;
                bool copied = (bool)assetDatabase.GetMethod(
                    "CopyAsset",
                    new[] { typeof(string), typeof(string) }
                ).Invoke(
                    null,
                    new object[]
                    {
                        InteractionSceneAssetPath,
                        temporaryScenePath
                    }
                );
                if (!copied)
                {
                    throw new InvalidOperationException(
                        "Unity could not create the guard-test scene copy."
                    );
                }
                scene = sceneManager.GetMethod(
                    "OpenScene",
                    new[] { typeof(string), openModeType }
                ).Invoke(
                    null,
                    new[] { (object)temporaryScenePath, additive }
                );
                SetActiveSceneForIsolatedCreation(scene);
                for (int index = 0; index < markerCount; index++)
                {
                    CreateGameObjectInActiveScene(
                        scene,
                        TestSceneMarkerName
                    );
                }

                TargetInvocationException rejected = Assert.Throws<
                    TargetInvocationException>(() =>
                        InvokeValidateLoadedScene(scene)
                    );
                Assert.That(
                    rejected.InnerException.Message,
                    Does.Contain(expectedMessage)
                );
            }
            catch (Exception exception)
            {
                primaryFailure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                RunFixtureCleanup(
                    primaryFailure,
                    () =>
                    {
                        if (scene != null && IsSceneValid(scene) &&
                            GetSceneBoolean(scene, "isLoaded"))
                        {
                            CloseScene(sceneManager, scene);
                        }
                    },
                    () => RestoreActiveScene(previousActiveScene),
                    () =>
                    {
                        if (!temporaryFolderCreated)
                        {
                            return;
                        }
                        bool deleted = (bool)assetDatabase.GetMethod(
                            "DeleteAsset",
                            new[] { typeof(string) }
                        ).Invoke(null, new object[] { temporaryFolderPath });
                        if (!deleted)
                        {
                            throw new InvalidOperationException(
                                "Unity could not delete the guard-test folder."
                            );
                        }
                    },
                    () => AssertSceneAssetUnchanged(
                        fullScenePath,
                        bytesBefore,
                        hashBefore
                    )
                );
            }
        }

        private static void WithCleanInteractionScene(Action<object> test)
        {
            if (test == null)
            {
                throw new ArgumentNullException(nameof(test));
            }

            string fullScenePath = Path.GetFullPath(
                InteractionSceneAssetPath
            );
            byte[] bytesBefore = File.ReadAllBytes(fullScenePath);
            string hashBefore = ComputeSha256(bytesBefore);
            Type sceneManager = EditorType(
                "UnityEditor.SceneManagement.EditorSceneManager"
            );
            object previousActiveScene = GetActiveScene();
            Type assetDatabase = EditorType("UnityEditor.AssetDatabase");
            Type openModeType = EditorType(
                "UnityEditor.SceneManagement.OpenSceneMode"
            );
            object additive = Enum.Parse(openModeType, "Additive");
            string ownerId = Guid.NewGuid().ToString("N");
            string temporaryFolderName =
                "__W7InteractionPhaseAdaptersTests_" + ownerId;
            string temporaryFolderPath = "Assets/" + temporaryFolderName;
            string temporaryScenePath = temporaryFolderPath +
                "/InteractionLab_W7Test.unity";
            object scene = null;
            bool temporaryFolderCreated = false;
            ExceptionDispatchInfo primaryFailure = null;
            try
            {
                string folderGuid = (string)assetDatabase.GetMethod(
                    "CreateFolder",
                    new[] { typeof(string), typeof(string) }
                ).Invoke(
                    null,
                    new object[] { "Assets", temporaryFolderName }
                );
                if (string.IsNullOrWhiteSpace(folderGuid))
                {
                    throw new InvalidOperationException(
                        "Unity could not create the W7 temporary asset folder."
                    );
                }
                temporaryFolderCreated = true;
                bool copied = (bool)assetDatabase.GetMethod(
                    "CopyAsset",
                    new[] { typeof(string), typeof(string) }
                ).Invoke(
                    null,
                    new object[]
                    {
                        InteractionSceneAssetPath,
                        temporaryScenePath
                    }
                );
                if (!copied)
                {
                    throw new InvalidOperationException(
                        "Unity could not create the isolated InteractionLab " +
                        "asset copy."
                    );
                }

                scene = sceneManager.GetMethod(
                    "OpenScene",
                    new[] { typeof(string), openModeType }
                ).Invoke(
                    null,
                    new[] { (object)temporaryScenePath, additive }
                );
                SetActiveSceneForIsolatedCreation(scene);
                Type setup = Type.GetType(
                    ValidatorTypeName,
                    throwOnError: true
                );
                setup.GetMethod(
                    "MarkTestOwnedSceneForAutomation",
                    BindingFlags.Public | BindingFlags.Static
                ).Invoke(null, new[] { scene });
                setup.GetMethod(
                    "StripW7OwnedWiringForTests",
                    BindingFlags.Public | BindingFlags.Static
                ).Invoke(null, new[] { scene });
                int remaining = (int)setup.GetMethod(
                    "CountW7OwnedWiringForTests",
                    BindingFlags.Public | BindingFlags.Static
                ).Invoke(null, new[] { scene });
                Assert.That(
                    remaining,
                    Is.Zero,
                    "The fixture must start with no W7-owned wiring."
                );
                EnsureTestBareHandInteractorRoots(scene);

                test(scene);
            }
            catch (Exception exception)
            {
                primaryFailure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                RunFixtureCleanup(
                    primaryFailure,
                    () =>
                    {
                        if (scene != null && IsSceneValid(scene) &&
                            GetSceneBoolean(scene, "isLoaded"))
                        {
                            CloseScene(sceneManager, scene);
                        }
                    },
                    () => RestoreActiveScene(previousActiveScene),
                    () =>
                    {
                        if (!temporaryFolderCreated)
                        {
                            return;
                        }
                        bool deleted = (bool)assetDatabase.GetMethod(
                            "DeleteAsset",
                            new[] { typeof(string) }
                        ).Invoke(null, new object[] { temporaryFolderPath });
                        if (!deleted)
                        {
                            throw new InvalidOperationException(
                                "Unity could not delete the W7 temporary " +
                                "asset folder."
                            );
                        }
                    },
                    () => AssertSceneAssetUnchanged(
                        fullScenePath,
                        bytesBefore,
                        hashBefore
                    )
                );
            }
        }

        private static void RunFixtureCleanup(
            ExceptionDispatchInfo primaryFailure,
            params Action[] recoveryActions)
        {
            var recoveryFailures = new List<Exception>();
            if (recoveryActions != null)
            {
                for (int index = 0; index < recoveryActions.Length; index++)
                {
                    try
                    {
                        recoveryActions[index]?.Invoke();
                    }
                    catch (Exception exception)
                    {
                        recoveryFailures.Add(exception);
                    }
                }
            }

            if (primaryFailure != null)
            {
                if (recoveryFailures.Count > 0)
                {
                    primaryFailure.SourceException.Data[
                        CleanupFailuresDataKey
                    ] = new AggregateException(
                        "One or more independent W7 fixture recovery " +
                        "actions failed after the primary test failure.",
                        recoveryFailures
                    );
                }
                primaryFailure.Throw();
                return;
            }

            if (recoveryFailures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(recoveryFailures[0]).Throw();
                return;
            }
            if (recoveryFailures.Count > 1)
            {
                throw new AggregateException(
                    "Multiple W7 fixture recovery actions failed.",
                    recoveryFailures
                );
            }
        }

        private static void AssertSceneAssetUnchanged(
            string fullScenePath,
            byte[] bytesBefore,
            string hashBefore)
        {
            byte[] bytesAfter = File.ReadAllBytes(fullScenePath);
            string hashAfter = ComputeSha256(bytesAfter);
            CollectionAssert.AreEqual(
                bytesBefore,
                bytesAfter,
                "InteractionLab scene bytes changed during an isolated test."
            );
            Assert.That(
                hashAfter,
                Is.EqualTo(hashBefore),
                "InteractionLab SHA-256 changed during an isolated test."
            );
        }

        private static object OpenAdditiveSceneAsset(string sceneAssetPath)
        {
            if (string.IsNullOrWhiteSpace(sceneAssetPath))
            {
                throw new ArgumentException(
                    "A temporary scene asset path is required.",
                    nameof(sceneAssetPath)
                );
            }
            Type editorSceneManager = EditorType(
                "UnityEditor.SceneManagement.EditorSceneManager"
            );
            Type openModeType = EditorType(
                "UnityEditor.SceneManagement.OpenSceneMode"
            );
            return editorSceneManager.GetMethod(
                "OpenScene",
                new[] { typeof(string), openModeType }
            ).Invoke(
                null,
                new[]
                {
                    (object)sceneAssetPath,
                    Enum.Parse(openModeType, "Additive")
                }
            );
        }

        private static void CloseScene(Type editorSceneManager, object scene)
        {
            bool closed = (bool)editorSceneManager.GetMethod(
                "CloseScene",
                new[] { scene.GetType(), typeof(bool) }
            ).Invoke(null, new[] { scene, (object)true });
            Assert.That(closed, Is.True, "Failed to close isolated scene.");
        }

        private static object GetActiveScene()
        {
            return SceneManagerType().GetMethod(
                "GetActiveScene",
                BindingFlags.Public | BindingFlags.Static
            ).Invoke(null, null);
        }

        private static object GetSceneByPath(string path)
        {
            return SceneManagerType().GetMethod(
                "GetSceneByPath",
                new[] { typeof(string) }
            ).Invoke(null, new object[] { path });
        }

        private static Array GetSceneRoots(object scene)
        {
            return (Array)scene.GetType().GetMethod(
                "GetRootGameObjects",
                Type.EmptyTypes
            ).Invoke(scene, null);
        }

        private static void MarkSceneDirty(
            Type editorSceneManager,
            object scene)
        {
            editorSceneManager.GetMethod(
                "MarkSceneDirty",
                new[] { scene.GetType() }
            ).Invoke(null, new[] { scene });
        }

        private static bool IsSceneValid(object scene)
        {
            return scene != null && (bool)scene.GetType().GetMethod(
                "IsValid",
                Type.EmptyTypes
            ).Invoke(scene, null);
        }

        private static bool GetSceneBoolean(object scene, string propertyName)
        {
            return (bool)scene.GetType().GetProperty(propertyName)
                .GetValue(scene);
        }

        private static void RestoreActiveScene(object scene)
        {
            if (!IsSceneValid(scene) ||
                !GetSceneBoolean(scene, "isLoaded"))
            {
                return;
            }
            if (scene.Equals(GetActiveScene()))
            {
                return;
            }
            bool restored = (bool)SceneManagerType().GetMethod(
                "SetActiveScene",
                new[] { scene.GetType() }
            ).Invoke(null, new[] { scene });
            Assert.That(restored, Is.True, "Failed to restore active scene.");
        }

        private static void SetActiveSceneForIsolatedCreation(object scene)
        {
            if (!IsSceneValid(scene) ||
                !GetSceneBoolean(scene, "isLoaded"))
            {
                throw new InvalidOperationException(
                    "A valid loaded isolated scene is required before " +
                    "creating test-owned objects."
                );
            }
            bool activated = (bool)SceneManagerType().GetMethod(
                "SetActiveScene",
                new[] { scene.GetType() }
            ).Invoke(null, new[] { scene });
            if (!activated || !scene.Equals(GetActiveScene()))
            {
                throw new InvalidOperationException(
                    "The isolated scene could not become active; no " +
                    "test-owned object may be created."
                );
            }
        }

        private static object CreateGameObjectInActiveScene(
            object scene,
            string name,
            Action<object> afterCreate = null)
        {
            if (!scene.Equals(GetActiveScene()))
            {
                throw new InvalidOperationException(
                    "Test-owned objects may be created only in the active " +
                    "isolated scene."
                );
            }

            object created = null;
            try
            {
                created = CreateGameObject(name);
                object createdScene = created.GetType().GetProperty("scene")
                    .GetValue(created);
                if (!scene.Equals(createdScene))
                {
                    throw new InvalidOperationException(
                        "A test-owned object was not born in the isolated " +
                        "scene."
                    );
                }
                afterCreate?.Invoke(created);
                return created;
            }
            catch
            {
                if (created != null)
                {
                    DestroyImmediate(created);
                }
                throw;
            }
        }

        private static void AssertCleanActiveSceneUnchanged(
            object scene,
            object sentinel,
            object child,
            int expectedRootCount)
        {
            Assert.That(IsSceneValid(scene), Is.True);
            Assert.That(GetSceneBoolean(scene, "isLoaded"), Is.True);
            Assert.That(scene.Equals(GetActiveScene()), Is.True);
            Assert.That(
                GetSceneBoolean(scene, "isDirty"),
                Is.False,
                "The previous active clean scene was dirtied."
            );
            Assert.That(
                GetSceneRoots(scene).Length,
                Is.EqualTo(expectedRootCount),
                "The previous active clean scene hierarchy changed."
            );
            Assert.That(
                sentinel.GetType().GetProperty("name").GetValue(sentinel),
                Is.EqualTo("W7CleanSourceSentinel")
            );
            Assert.That(
                child.GetType().GetProperty("name").GetValue(child),
                Is.EqualTo("W7CleanSourceChild")
            );
            Assert.That(
                child.GetType().GetProperty("scene").GetValue(child),
                Is.EqualTo(scene)
            );
            Assert.That(
                GetTransform(child).GetType().GetProperty("parent")
                    .GetValue(GetTransform(child)),
                Is.SameAs(GetTransform(sentinel)),
                "The clean-scene sentinel hierarchy changed."
            );
            AssertVector3(
                GetTransform(sentinel).GetType()
                    .GetProperty("localPosition")
                    .GetValue(GetTransform(sentinel)),
                2f,
                4f,
                6f,
                "The clean-scene sentinel value changed."
            );
            AssertVector3(
                GetTransform(child).GetType()
                    .GetProperty("localPosition")
                    .GetValue(GetTransform(child)),
                1f,
                3f,
                5f,
                "The clean-scene child value changed."
            );
        }

        private static void EnsureTestBareHandInteractorRoots(object scene)
        {
            EnsureTestBareHandInteractorRoot(scene, "left");
            EnsureTestBareHandInteractorRoot(scene, "right");
        }

        private static void EnsureTestBareHandInteractorRoot(
            object scene,
            string side)
        {
            var matches = new List<object>();
            Type transformType = UnityType("Transform");
            foreach (object root in GetSceneRoots(scene))
            {
                Array transforms = (Array)root.GetType().GetMethod(
                    "GetComponentsInChildren",
                    new[] { typeof(Type), typeof(bool) }
                ).Invoke(root, new object[] { transformType, true });
                foreach (object transform in transforms)
                {
                    if (IsTestBareHandInteractorRoot(transform, side))
                    {
                        matches.Add(transform);
                    }
                }
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"The isolated fixture found {matches.Count} {side} " +
                    "hand-only interactor roots."
                );
            }
            if (matches.Count == 1)
            {
                return;
            }

            CreateGameObjectInActiveScene(
                scene,
                "Test" + char.ToUpperInvariant(side[0]) +
                side.Substring(1) + "HandInteractors"
            );
        }

        private static bool IsTestBareHandInteractorRoot(
            object transform,
            string side)
        {
            if (transform == null || string.IsNullOrWhiteSpace(side))
            {
                return false;
            }

            Type transformType = transform.GetType();
            string normalized = NormalizeObjectName(transform);
            bool legacyNamedRoot =
                normalized.Contains("handinteractors") &&
                normalized.Contains(side) &&
                !normalized.Contains("controller");

            object parent = transformType.GetProperty("parent")
                .GetValue(transform);
            object grandparent = parent == null
                ? null
                : parent.GetType().GetProperty("parent").GetValue(parent);
            bool comprehensiveHandOnlyRoot =
                normalized == "handandnocontroller" &&
                NormalizeObjectName(parent) == "interactors" &&
                NormalizeObjectName(grandparent).Contains(
                    "comprehensiveinteractors" + side
                );
            return legacyNamedRoot || comprehensiveHandOnlyRoot;
        }

        private static string NormalizeObjectName(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            string name = (string)value.GetType().GetProperty("name")
                .GetValue(value);
            return new string((name ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static void MoveGameObjectToScene(
            object gameObject,
            object scene)
        {
            SceneManagerType().GetMethod(
                "MoveGameObjectToScene",
                new[] { UnityType("GameObject"), scene.GetType() }
            ).Invoke(null, new[] { gameObject, scene });
        }

        private static Type SceneManagerType()
        {
            return Type.GetType(
                "UnityEngine.SceneManagement.SceneManager, " +
                "UnityEngine.CoreModule",
                throwOnError: true
            );
        }

        private static void InvokeValidateLoadedScene(object scene)
        {
            Type.GetType(
                ValidatorTypeName,
                throwOnError: true
            ).GetMethod(
                "ValidateTestOwnedSceneForAutomation",
                BindingFlags.Public | BindingFlags.Static
            ).Invoke(null, new[] { scene });
        }

        private static void InvokeTestOwnedSetupAndValidate(object scene)
        {
            Type.GetType(
                ValidatorTypeName,
                throwOnError: true
            ).GetMethod(
                "SetupAndValidateTestOwnedSceneWithoutSaving",
                BindingFlags.Public | BindingFlags.Static
            ).Invoke(null, new[] { scene });
        }

        private static void InvokeSetupLoadedScene(object scene)
        {
            Type.GetType(
                ValidatorTypeName,
                throwOnError: true
            ).GetMethod(
                "SetupTestOwnedSceneForAutomation",
                BindingFlags.Public | BindingFlags.Static
            ).Invoke(null, new[] { scene });
        }

        private static object FindGameObjectInScene(
            object scene,
            string name)
        {
            return Type.GetType(
                ValidatorTypeName,
                throwOnError: true
            ).GetMethod(
                "FindGameObjectInSceneForTests",
                BindingFlags.Public | BindingFlags.Static
            ).Invoke(null, new[] { scene, name });
        }

        private static Array FindGameObjectsInScene(
            object scene,
            string name)
        {
            return (Array)Type.GetType(
                ValidatorTypeName,
                throwOnError: true
            ).GetMethod(
                "FindGameObjectsInSceneForTests",
                BindingFlags.Public | BindingFlags.Static
            ).Invoke(null, new[] { scene, name });
        }

        private static void StripW7OwnedWiring(object scene)
        {
            Type.GetType(
                ValidatorTypeName,
                throwOnError: true
            ).GetMethod(
                "StripW7OwnedWiringForTests",
                BindingFlags.Public | BindingFlags.Static
            ).Invoke(null, new[] { scene });
        }

        private static int CountW7OwnedWiring(object scene)
        {
            return (int)Type.GetType(
                ValidatorTypeName,
                throwOnError: true
            ).GetMethod(
                "CountW7OwnedWiringForTests",
                BindingFlags.Public | BindingFlags.Static
            ).Invoke(null, new[] { scene });
        }

        private static void AssertEditorPublisherDoesNotReachBinding(
            object binding,
            object adapter,
            object collider)
        {
            int invocationCount = GetSubscriptionInvocationCount(binding);
            object colliderState = collider.GetType().GetProperty("enabled")
                .GetValue(collider);
            adapter.GetType().GetMethod("Enable").Invoke(adapter, null);
            adapter.GetType().GetMethod("Disable").Invoke(adapter, null);
            Assert.That(
                GetSubscriptionInvocationCount(binding),
                Is.EqualTo(invocationCount),
                "EditMode Configure must not attach runtime event handlers."
            );
            Assert.That(
                collider.GetType().GetProperty("enabled").GetValue(collider),
                Is.EqualTo(colliderState),
                "An EditMode publisher event reached the binding collider."
            );
        }

        private static int GetSubscriptionInvocationCount(object subscriber)
        {
            PropertyInfo property = subscriber.GetType().GetProperty(
                "SubscriptionDiagnostic",
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(
                property,
                Is.Not.Null,
                "UNITY_INCLUDE_TESTS diagnostic is unavailable."
            );
            object diagnostic = property.GetValue(subscriber);
            return (int)diagnostic.GetType().GetProperty("InvocationCount")
                .GetValue(diagnostic);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(bytes))
                    .Replace("-", string.Empty);
            }
        }

        private static Type EditorType(string fullName)
        {
            Type type = Type.GetType(fullName + ", UnityEditor", false) ??
                Type.GetType(fullName + ", UnityEditor.CoreModule", false);
            return type ?? throw new TypeLoadException(fullName);
        }

        private static void AssertDisabledInputBoundary(
            object coordinator,
            object binding,
            object collider)
        {
            object previousResult = coordinator.GetType()
                .GetProperty("LastResult").GetValue(coordinator);
            object adapter = binding.GetType().GetProperty("Adapter")
                .GetValue(binding);
            binding.GetType().GetProperty("enabled")
                .SetValue(binding, false);
            ReconfigureBinding(binding, adapter, collider);

            Assert.That(
                collider.GetType().GetProperty("enabled").GetValue(collider),
                Is.False
            );
            Assert.That(
                binding.GetType().GetMethod("AcceptInput")
                    .Invoke(binding, null),
                Is.Null
            );
            binding.GetType().GetMethod("Poke").Invoke(binding, null);
            Assert.That(
                coordinator.GetType().GetProperty("LastResult")
                    .GetValue(coordinator),
                Is.SameAs(previousResult),
                "A disabled UnityEvent seam must not reach the Core session."
            );

            AssertEditorPublisherDoesNotReachBinding(
                binding,
                adapter,
                collider
            );
            Assert.That(
                collider.GetType().GetProperty("enabled").GetValue(collider),
                Is.False,
                "EditMode publisher events must not reopen disabled input."
            );
        }

        private static void AssertDisabledConfigureBoundary(
            object binding,
            object adapter,
            object collider,
            object interactionBehaviour = null)
        {
            binding.GetType().GetProperty("enabled")
                .SetValue(binding, false);
            ReconfigureBinding(
                binding,
                adapter,
                collider,
                interactionBehaviour
            );

            Assert.That(
                collider.GetType().GetProperty("enabled").GetValue(collider),
                Is.False,
                "Configure must not reopen a disabled binding collider."
            );
            if (interactionBehaviour != null)
            {
                Assert.That(
                    interactionBehaviour.GetType().GetProperty("enabled")
                        .GetValue(interactionBehaviour),
                    Is.False,
                    "Configure must not reopen disabled interaction behaviour."
                );
            }
            Assert.That(
                binding.GetType().GetMethod("AcceptInput")
                    .Invoke(binding, null),
                Is.Null
            );

            adapter.GetType().GetMethod("Disable").Invoke(adapter, null);
            adapter.GetType().GetMethod("Enable").Invoke(adapter, null);
            Assert.That(
                collider.GetType().GetProperty("enabled").GetValue(collider),
                Is.False,
                "A disabled Configure must not subscribe to availability."
            );
            if (interactionBehaviour != null)
            {
                Assert.That(
                    interactionBehaviour.GetType().GetProperty("enabled")
                        .GetValue(interactionBehaviour),
                    Is.False
                );
            }

            binding.GetType().GetProperty("enabled")
                .SetValue(binding, true);
            ReconfigureBinding(
                binding,
                adapter,
                collider,
                interactionBehaviour
            );
            Assert.That(
                collider.GetType().GetProperty("enabled").GetValue(collider),
                Is.True,
                "An explicit active Configure must apply current availability."
            );
            if (interactionBehaviour != null)
            {
                Assert.That(
                    interactionBehaviour.GetType().GetProperty("enabled")
                        .GetValue(interactionBehaviour),
                    Is.True
                );
            }
            Assert.That(
                binding.GetType().GetMethod("AcceptInput")
                    .Invoke(binding, null),
                Is.Not.Null
            );
            AssertEditorPublisherDoesNotReachBinding(
                binding,
                adapter,
                collider
            );
        }

        private static void ReconfigureBinding(
            object binding,
            object adapter,
            object collider,
            object interactionBehaviour = null)
        {
            MethodInfo configure = binding.GetType().GetMethod("Configure");
            switch (binding.GetType().Name)
            {
                case "InteractionTargetBinding":
                    configure.Invoke(
                        binding,
                        new object[]
                        {
                            "box_stool",
                            adapter,
                            interactionBehaviour == null
                                ? null
                                : TypedArray(
                                    UnityType("Behaviour"),
                                    interactionBehaviour
                                ),
                            TypedArray(UnityPhysicsType("Collider"), collider)
                        }
                    );
                    break;
                case "InteractionDigitBinding":
                    configure.Invoke(binding, new[] { (object)1, adapter, collider });
                    break;
                default:
                    configure.Invoke(binding, new[] { adapter, collider });
                    break;
            }
        }

        private static object CreateKeypadBinding(
            object parent,
            string typeName,
            object adapter,
            out object collider,
            int? digit = null)
        {
            object button = CreateGameObject(typeName);
            SetParent(GetTransform(button), parent);
            collider = AddComponent(button, UnityPhysicsType("BoxCollider"));
            object binding = AddComponent(button, RuntimeType(typeName));
            MethodInfo configure = binding.GetType().GetMethod("Configure");
            if (digit.HasValue)
            {
                configure.Invoke(
                    binding,
                    new[] { (object)digit.Value, adapter, collider }
                );
            }
            else
            {
                configure.Invoke(binding, new[] { adapter, collider });
            }
            return binding;
        }

        private static object CreateChildPath(object parent, string path)
        {
            object current = parent;
            foreach (string segment in path.Split('/'))
            {
                object child = CreateGameObject(segment);
                object transform = GetTransform(child);
                SetParent(transform, current);
                current = transform;
            }
            return current;
        }

        private static object FindGameObject(string name)
        {
            object result = UnityType("GameObject").GetMethod(
                "Find",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null
            ).Invoke(null, new object[] { name });
            Assert.That(result, Is.Not.Null, $"Missing GameObject '{name}'.");
            return result;
        }

        private static object FindChild(object transform, string path)
        {
            object result = transform.GetType().GetMethod(
                "Find",
                new[] { typeof(string) }
            ).Invoke(transform, new object[] { path });
            Assert.That(result, Is.Not.Null, $"Missing child '{path}'.");
            return result;
        }

        private static object GetGameObject(object component)
        {
            return component.GetType().GetProperty("gameObject")
                .GetValue(component);
        }

        private static object GetComponent(object gameObject, Type type)
        {
            return gameObject.GetType().GetMethod(
                "GetComponent",
                new[] { typeof(Type) }
            ).Invoke(gameObject, new object[] { type });
        }

        private static object[] GetComponentsInChildren(
            object gameObject,
            Type type,
            bool includeInactive)
        {
            return ((Array)gameObject.GetType().GetMethod(
                "GetComponentsInChildren",
                new[] { typeof(Type), typeof(bool) }
            ).Invoke(
                gameObject,
                new object[] { type, includeInactive }
            )).Cast<object>().ToArray();
        }

        private static bool IsOculusGrabBehaviour(object component)
        {
            Type type = component?.GetType();
            string typeName = type?.Name ?? string.Empty;
            string typeNamespace = type?.Namespace ?? string.Empty;
            return typeNamespace.StartsWith(
                       "Oculus.Interaction",
                       StringComparison.Ordinal
                   ) &&
                   (typeName.IndexOf(
                        "Interactable",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0 ||
                    typeName.IndexOf(
                        "Grabbable",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0);
        }

        private static Array CreateSixAdapters(object parent)
        {
            Type adapterType = RuntimeType("InteractionPhaseAdapter");
            Array adapters = Array.CreateInstance(adapterType, 6);
            string[] names =
            {
                "PhaseOneInteractionAdapter",
                "PhaseTwoInteractionAdapter",
                "PhaseThreeInteractionAdapter",
                "PhaseFourInteractionAdapter",
                "PhaseFiveInteractionAdapter",
                "PhaseSixInteractionAdapter"
            };
            for (int index = 0; index < names.Length; index++)
            {
                object child = CreateGameObject(names[index]);
                SetParent(GetTransform(child), parent);
                adapters.SetValue(
                    AddComponent(child, RuntimeType(names[index])),
                    index
                );
            }
            return adapters;
        }

        private static object CreateVisual(
            object parent,
            string name)
        {
            object visual = CreateGameObject(name);
            object transform = GetTransform(visual);
            SetParent(transform, parent);
            return transform;
        }

        private static object CreateBoundTarget(
            object parent,
            object adapter,
            string targetId)
        {
            object target = CreateGameObject(targetId);
            SetParent(GetTransform(target), parent);
            object binding = AddComponent(
                target,
                RuntimeType("InteractionTargetBinding")
            );
            binding.GetType().GetMethod("Configure").Invoke(
                binding,
                new object[] { targetId, adapter, null, null }
            );
            return target;
        }

        private static object CreateBoundTargetWithCollider(
            object parent,
            object adapter,
            string targetId,
            out object binding,
            out object inputCollider)
        {
            object target = CreateGameObject(targetId);
            SetParent(GetTransform(target), parent);
            inputCollider = AddComponent(
                target,
                UnityPhysicsType("BoxCollider")
            );
            binding = AddComponent(
                target,
                RuntimeType("InteractionTargetBinding")
            );
            binding.GetType().GetMethod("Configure").Invoke(
                binding,
                new object[]
                {
                    targetId,
                    adapter,
                    null,
                    TypedArray(
                        UnityPhysicsType("Collider"),
                        inputCollider
                    )
                }
            );
            return target;
        }

        private static object CreateTextMesh(object parent, string name)
        {
            object textObject = CreateGameObject(name);
            SetParent(GetTransform(textObject), parent);
            return AddComponent(
                textObject,
                Type.GetType(
                    "UnityEngine.TextMesh, UnityEngine.TextRenderingModule",
                    throwOnError: true
                )
            );
        }

        private static void DispatchHintResult(object presenter, object result)
        {
            MethodInfo handler = presenter.GetType().GetMethod(
                "HandleResult",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(handler, Is.Not.Null);
            handler.Invoke(presenter, new[] { result });
        }

        private static bool IsComponentGameObjectActive(object component)
        {
            object gameObject = component.GetType().GetProperty("gameObject")
                .GetValue(component);
            return (bool)gameObject.GetType().GetProperty("activeSelf")
                .GetValue(gameObject);
        }

        private static Array TypedArray(Type elementType, params object[] values)
        {
            Array result = Array.CreateInstance(elementType, values.Length);
            for (int index = 0; index < values.Length; index++)
            {
                result.SetValue(values[index], index);
            }
            return result;
        }

        private static Array CreateTargetStateBindings(
            object[] targets,
            string[] targetIds)
        {
            Type bindingType = RuntimeType(
                "DeterministicTargetStateBinding"
            );
            Array bindings = Array.CreateInstance(
                bindingType,
                targets.Length
            );
            MethodInfo configure = bindingType.GetMethod("Configure");
            for (int index = 0; index < targets.Length; index++)
            {
                object binding = Activator.CreateInstance(bindingType);
                configure.Invoke(
                    binding,
                    new object[]
                    {
                        targetIds[index],
                        targets[index],
                        CreateVector3(-10f, 0f, 0f)
                    }
                );
                bindings.SetValue(binding, index);
            }
            return bindings;
        }

        private static object ConfigurePresentation(
            object root,
            object coordinator,
            Array cabinetBindings)
        {
            Type presentationType = RuntimeType(
                "InteractionDeterministicPresentation"
            );
            object presentation = AddComponent(root, presentationType);
            Type hingeType = RuntimeType("DeterministicHingeBinding");
            Type stateType = RuntimeType(
                "DeterministicTargetStateBinding"
            );
            Type keyType = RuntimeType("PlannedKeyReleaseBinding");
            Array noStates = Array.CreateInstance(stateType, 0);
            Array noKeys = Array.CreateInstance(keyType, 0);
            presentationType.GetMethod("Configure").Invoke(
                presentation,
                new object[]
                {
                    coordinator,
                    Activator.CreateInstance(hingeType),
                    Activator.CreateInstance(hingeType),
                    Activator.CreateInstance(hingeType),
                    Activator.CreateInstance(hingeType),
                    Activator.CreateInstance(hingeType),
                    noStates,
                    cabinetBindings,
                    noStates,
                    noKeys
                }
            );
            return presentation;
        }

        private static object CreateRunPlan()
        {
            string[] sentenceIds =
                { "001", "004", "013", "016", "025", "026" };
            Type contentType = CoreType("InstructionContentReference");
            Type phaseType = CoreType("RunPhasePlan");
            Type variantCatalogType = CoreType("TaskVariantCatalog");
            Array phases = Array.CreateInstance(phaseType, sentenceIds.Length);
            MethodInfo forSentence = variantCatalogType.GetMethod(
                "ForSentence",
                BindingFlags.Public | BindingFlags.Static
            );
            for (int index = 0; index < sentenceIds.Length; index++)
            {
                string sentenceId = sentenceIds[index];
                object content = Activator.CreateInstance(
                    contentType,
                    new object[]
                    {
                        index + 1,
                        sentenceId,
                        "wang",
                        "take_" + sentenceId,
                        new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero),
                        0,
                        "content/" + sentenceId + ".mp4",
                        new string('a', 64)
                    }
                );
                object variant = forSentence.Invoke(
                    null,
                    new object[] { sentenceId }
                );
                phases.SetValue(
                    Activator.CreateInstance(
                        phaseType,
                        new[] { (object)(index + 1), content, variant }
                    ),
                    index
                );
            }

            Type assistanceConditionType = CoreType(
                "AssistanceCondition"
            );
            object assignment = Activator.CreateInstance(
                CoreType("AssistanceAssignment"),
                new[]
                {
                    Enum.Parse(assistanceConditionType, "SignOnly"),
                    (object)0,
                    (object)0,
                    Enum.Parse(
                        CoreType("AssistanceAssignmentMode"),
                        "RandomizedBlock"
                    )
                }
            );
            object password = Activator.CreateInstance(
                CoreType("SafePassword"),
                new object[] { new[] { 1, 2, 3, 4 } }
            );
            object chestOrder = Activator.CreateInstance(
                CoreType("ChestButtonOrder"),
                new object[]
                {
                    new[] { "blue", "red", "yellow", "green" }
                }
            );
            return Activator.CreateInstance(
                CoreType("RunPlan"),
                new object[]
                {
                    "pilot-20260825",
                    "P001",
                    "run_w7_phase5_visual_reset",
                    "app_w7_phase5_visual_reset",
                    new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero),
                    "1.0.0-test",
                    "335befa",
                    12345,
                    assignment,
                    password,
                    chestOrder,
                    phases
                }
            );
        }

        private static void AdvanceToPhaseFive(object coordinator)
        {
            SynchronizePhase(coordinator, 1);
            AcceptTarget(coordinator, 1, "box_stool");
            for (int digit = 1; digit <= 4; digit++)
            {
                AcceptInput(
                    coordinator,
                    1,
                    CoreType("PhaseInput").GetMethod("Digit").Invoke(
                        null,
                        new object[] { digit }
                    )
                );
            }
            AcceptInput(
                coordinator,
                1,
                CoreType("PhaseInput").GetMethod("Submit").Invoke(null, null)
            );
            SynchronizePhase(coordinator, 2);
            AcceptInput(
                coordinator,
                2,
                CoreType("PhaseInput").GetMethod("Pair").Invoke(
                    null,
                    new object[] { "coin_dragon", "plate_dragon" }
                )
            );
            SynchronizePhase(coordinator, 3);
            AcceptTarget(coordinator, 3, "picture_frame_a");
            SynchronizePhase(coordinator, 4);
            AcceptTarget(coordinator, 4, "key_a");
            SynchronizePhase(coordinator, 5);
        }

        private static void SynchronizePhase(
            object coordinator,
            int phaseId,
            bool giveUpAvailable = false)
        {
            object snapshot = CreatePhaseSnapshot(
                phaseId,
                giveUpAvailable
            );
            coordinator.GetType().GetMethod("Synchronize").Invoke(
                coordinator,
                new[] { snapshot }
            );
        }

        private static object GiveUp(object coordinator, int phaseId)
        {
            object snapshot = CreatePhaseSnapshot(
                phaseId,
                giveUpAvailable: true
            );
            return coordinator.GetType().GetMethod("GiveUpCurrentPhase")
                .Invoke(coordinator, new[] { snapshot });
        }

        private static object CreatePhaseSnapshot(
            int phaseId,
            bool giveUpAvailable)
        {
            Type snapshotType = CoreType("PhaseExecutionSnapshot");
            Type phaseStateType = CoreType("PhaseState");
            object state = Enum.Parse(
                phaseStateType,
                giveUpAvailable ? "Active" : "FirstPlayback"
            );
            return Activator.CreateInstance(
                snapshotType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object[]
                {
                    phaseId,
                    state,
                    null,
                    giveUpAvailable,
                    giveUpAvailable,
                    false,
                    0,
                    0,
                    TimeSpan.Zero
                },
                culture: null
            );
        }

        private static object CreateTerminalPhaseSnapshot(int phaseId)
        {
            Type snapshotType = CoreType("PhaseExecutionSnapshot");
            Type phaseStateType = CoreType("PhaseState");
            return Activator.CreateInstance(
                snapshotType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object[]
                {
                    phaseId,
                    Enum.Parse(phaseStateType, "Completed"),
                    null,
                    true,
                    true,
                    false,
                    0,
                    0,
                    TimeSpan.Zero
                },
                culture: null
            );
        }

        private static object AcceptTarget(
            object coordinator,
            int phaseId,
            string targetId)
        {
            object input = CoreType("PhaseInput").GetMethod("Target").Invoke(
                null,
                new object[] { targetId }
            );
            return AcceptInput(coordinator, phaseId, input);
        }

        private static object AcceptInput(
            object coordinator,
            int phaseId,
            object input)
        {
            return coordinator.GetType().GetMethod("AcceptInput").Invoke(
                coordinator,
                new[] { (object)phaseId, input }
            );
        }

        private static object CreateGameObject(string name)
        {
            return Activator.CreateInstance(
                UnityType("GameObject"),
                new object[] { name }
            );
        }

        private static object AddComponent(object gameObject, Type type)
        {
            return gameObject.GetType().GetMethod(
                "AddComponent",
                new[] { typeof(Type) }
            ).Invoke(gameObject, new object[] { type });
        }

        private static object GetTransform(object gameObject)
        {
            return gameObject.GetType().GetProperty("transform")
                .GetValue(gameObject);
        }

        private static void SetParent(object transform, object parent)
        {
            Type transformType = UnityType("Transform");
            transformType.GetMethod(
                "SetParent",
                new[] { transformType, typeof(bool) }
            ).Invoke(transform, new[] { parent, (object)false });
        }

        private static void SetLocalEulerAngles(object transform, object euler)
        {
            transform.GetType().GetProperty("localEulerAngles")
                .SetValue(transform, euler);
        }

        private static void SetLocalPosition(object transform, object position)
        {
            transform.GetType().GetProperty("localPosition")
                .SetValue(transform, position);
        }

        private static object CreateVector3(float x, float y, float z)
        {
            return Activator.CreateInstance(
                UnityType("Vector3"),
                new object[] { x, y, z }
            );
        }

        private static object CreateColor(float r, float g, float b, float a)
        {
            return Activator.CreateInstance(
                UnityType("Color"),
                new object[] { r, g, b, a }
            );
        }

        private static object ReadRendererPropertyBlockColor(object renderer)
        {
            Type blockType = UnityType("MaterialPropertyBlock");
            object block = Activator.CreateInstance(blockType);
            renderer.GetType().GetMethod(
                "GetPropertyBlock",
                new[] { blockType }
            ).Invoke(renderer, new[] { block });
            return blockType.GetMethod(
                "GetColor",
                new[] { typeof(string) }
            ).Invoke(block, new object[] { "_BaseColor" });
        }

        private static void AssertColor(
            object color,
            float red,
            float green,
            float blue)
        {
            Type colorType = color.GetType();
            Assert.That(
                Convert.ToSingle(colorType.GetField("r").GetValue(color)),
                Is.EqualTo(red).Within(0.001f)
            );
            Assert.That(
                Convert.ToSingle(colorType.GetField("g").GetValue(color)),
                Is.EqualTo(green).Within(0.001f)
            );
            Assert.That(
                Convert.ToSingle(colorType.GetField("b").GetValue(color)),
                Is.EqualTo(blue).Within(0.001f)
            );
        }

        private static float ReadVector3Component(
            object vector,
            string component)
        {
            return Convert.ToSingle(
                vector.GetType().GetField(component).GetValue(vector)
            );
        }

        private static void AssertVector3(
            object vector,
            float x,
            float y,
            float z,
            string message)
        {
            Assert.That(
                Convert.ToSingle(vector.GetType().GetField("x")
                    .GetValue(vector)),
                Is.EqualTo(x).Within(0.0001f),
                message
            );
            Assert.That(
                Convert.ToSingle(vector.GetType().GetField("y")
                    .GetValue(vector)),
                Is.EqualTo(y).Within(0.0001f),
                message
            );
            Assert.That(
                Convert.ToSingle(vector.GetType().GetField("z")
                    .GetValue(vector)),
                Is.EqualTo(z).Within(0.0001f),
                message
            );
        }

        private static float RotationAngleFromIdentity(object transform)
        {
            Type quaternionType = UnityType("Quaternion");
            object identity = quaternionType.GetProperty(
                "identity",
                BindingFlags.Public | BindingFlags.Static
            ).GetValue(null);
            object rotation = transform.GetType()
                .GetProperty("localRotation")
                .GetValue(transform);
            object angle = quaternionType.GetMethod(
                "Angle",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { quaternionType, quaternionType },
                modifiers: null
            ).Invoke(null, new[] { identity, rotation });
            return Convert.ToSingle(angle);
        }

        private static void DestroyImmediate(object target)
        {
            Type objectType = UnityType("Object");
            objectType.GetMethod(
                "DestroyImmediate",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { objectType },
                modifiers: null
            ).Invoke(null, new[] { target });
        }

        private static Type UnityType(string typeName)
        {
            return Type.GetType(
                "UnityEngine." + typeName + ", UnityEngine.CoreModule",
                throwOnError: true
            );
        }

        private static Type UnityPhysicsType(string typeName)
        {
            return Type.GetType(
                "UnityEngine." + typeName + ", UnityEngine.PhysicsModule",
                throwOnError: true
            );
        }

        private static Type UnityAudioType(string typeName)
        {
            return Type.GetType(
                "UnityEngine." + typeName + ", UnityEngine.AudioModule",
                throwOnError: true
            );
        }

        private static Type RuntimeType(string typeName)
        {
            return Type.GetType(
                AdapterNamespace + typeName + ", " + RuntimeAssembly,
                throwOnError: true
            );
        }

        private static Type PresentationType(string typeName)
        {
            return Type.GetType(
                "SignVR.Interaction.Presentation." + typeName +
                ", " + RuntimeAssembly,
                throwOnError: true
            );
        }

        private static Type CoreType(string typeName)
        {
            return Type.GetType(
                "SignVR.Interaction.Core." + typeName +
                ", SignVR.Interaction.Core",
                throwOnError: true
            );
        }

        private static object GetRunPlanTaskVariant(
            object runPlan,
            int zeroBasedPhaseIndex)
        {
            object phases = runPlan.GetType().GetProperty("Phases")
                .GetValue(runPlan);
            object phase = phases.GetType().GetProperty("Item")
                .GetValue(phases, new object[] { zeroBasedPhaseIndex });
            return phase.GetType().GetProperty("TaskVariant")
                .GetValue(phase);
        }

        private static void SubscribePublicResultProduced(
            object coordinator,
            PublicResultEventCounter counter)
        {
            Type resultType = CoreType("ValidationResult");
            MethodInfo openHandler = typeof(PublicResultEventCounter).GetMethod(
                nameof(PublicResultEventCounter.Observe),
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.That(openHandler, Is.Not.Null);
            MethodInfo closedHandler = openHandler.MakeGenericMethod(resultType);
            Type delegateType = typeof(Action<>).MakeGenericType(resultType);
            Delegate handler = Delegate.CreateDelegate(
                delegateType,
                counter,
                closedHandler
            );
            EventInfo resultProduced = coordinator.GetType().GetEvent(
                "ResultProduced",
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.That(resultProduced, Is.Not.Null);
            resultProduced.AddEventHandler(coordinator, handler);
        }

        private sealed class PublicResultEventCounter
        {
            public int Count { get; private set; }

            public void Observe<T>(T result)
            {
                Count++;
            }
        }

        private static void AssertPublicInstanceMethod(
            Type type,
            string methodName)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.That(
                method,
                Is.Not.Null,
                $"{type.Name}.{methodName} is missing."
            );
        }
    }
}
