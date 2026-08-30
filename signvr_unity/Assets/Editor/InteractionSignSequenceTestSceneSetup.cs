using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using SignVR.Interaction.CaptureHost;
using SignVR.Interaction.Core;
using SignVR.Interaction.Orchestration;
using SignVR.Interaction.PhaseAdapters;
using SignVR.Interaction.Presentation;
using SignVR.Interaction;
using SignVR.Streaming;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SignVR.Editor.Interaction
{
    public static class InteractionSignSequenceTestSceneSetup
    {
        public const string ScenePath =
            "Assets/Scenes/InteractionSignSequenceTest.unity";
        private const string ContentManifestRelativePath =
            "InstructionContent/instruction-content-manifest.json";
        private const float SeatedMoveStepDistance = 0.2f;
        private static readonly Vector3 RightWindowSpawnEyePosition =
            new(0.821f, 1.38f, -4.85f);
        // Sentence 001 is authored in the fixed room world frame. Keep the
        // signer and target objects in that frame, but aim the initial view at
        // the first signer's captured root so it is centered on entry.
        private static readonly Vector3 FirstSignerViewTarget =
            new(-3.44257f, 0f, -3.62814f);

        [MenuItem("SignVR/Tests/Configure 31-Sign Sequence Scene")]
        public static void ConfigureScene()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single
            );
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "The sign-sequence test scene could not be opened."
                );
            }

            PrepareExistingFramePasswordButtonsForW7(scene);

            // The sequence scene is a production copy of InteractionLab. Run
            // the same idempotent W7 wiring here so movable coins, planned
            // keys, breaker proxies, and obsolete floating lock/keypad
            // objects cannot drift from the scene used for the APK.
            W7InteractionPhaseAdaptersSetup
                .SetupAndValidateLoadedSceneForAutomation(scene);

            InstructionPresentationController presentation = FindOne<
                InstructionPresentationController>();
            InteractionPhaseCoordinator phaseCoordinator = FindOne<
                InteractionPhaseCoordinator>();
            InteractionInstructionControls copiedControls = FindOne<
                InteractionInstructionControls>();
            VRPlayerRig player = FindOne<VRPlayerRig>();
            InteractionSeatedRigMover seatedMover = FindOne<
                InteractionSeatedRigMover>();
            InteractionStudyCaptureBinding captureBinding = FindOne<
                InteractionStudyCaptureBinding>();
            GhostPointingDetector ghostPointingDetector = FindOne<
                GhostPointingDetector>();

            ConfigureSeatedRig(player, seatedMover, copiedControls);
            ConfigureFirstPersonRecording(player);
            ConfigurePlayerPointing(
                player,
                captureBinding,
                ghostPointingDetector
            );

            foreach (InteractionStudyFlowController flow in
                FindAll<InteractionStudyFlowController>())
            {
                flow.enabled = true;
                EditorUtility.SetDirty(flow);
            }
            copiedControls.enabled = true;
            copiedControls.Configure(presentation);
            copiedControls.ConfigureCommandRouting(requireSink: true);
            EditorUtility.SetDirty(copiedControls);
            RestoreProductionControlNames(copiedControls.transform);
            foreach (InteractionStudyFlowControls controls in
                FindAll<InteractionStudyFlowControls>())
            {
                ConfigureProductionResultUi(
                    controls,
                    copiedControls,
                    player.Head
                );
                EnableProductionStudyUi(controls);
            }

            Transform existing = copiedControls.transform.Find(
                "SignSequenceTestHarness"
            );
            if (existing != null)
            {
                foreach (InteractionSignSequenceTestController controller in
                    existing.GetComponents<InteractionSignSequenceTestController>())
                {
                    controller.enabled = false;
                    EditorUtility.SetDirty(controller);
                }
                foreach (InteractionSignSequenceTestControls controls in
                    existing.GetComponents<InteractionSignSequenceTestControls>())
                {
                    controls.enabled = false;
                    EditorUtility.SetDirty(controls);
                }
                existing.gameObject.SetActive(false);
                EditorUtility.SetDirty(existing.gameObject);
            }
            Transform legacyStatus = copiedControls.transform.Find(
                "InstructionControlCanvas/SequenceStatus"
            );
            if (legacyStatus != null)
            {
                legacyStatus.gameObject.SetActive(false);
                EditorUtility.SetDirty(legacyStatus.gameObject);
            }
            ConfigureFrameChestRecording(
                scene,
                copiedControls,
                presentation,
                phaseCoordinator,
                player
            );

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Configured InteractionSignSequenceTest in formal six-phase " +
                "mode: one randomized sentence per phase, in phase order."
            );
        }

        [MenuItem("SignVR/Tests/Repair 31-Sign Sequence Entry UI")]
        public static void RepairEntryUi()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive
            );
            try
            {
                InteractionStudyFlowControls[] controls =
                    FindSceneComponents<InteractionStudyFlowControls>(scene);
                if (controls.Length != 1)
                {
                    throw new InvalidOperationException(
                        "The sign-sequence scene must contain exactly one " +
                        "InteractionStudyFlowControls."
                    );
                }

                EnableProductionStudyUi(controls[0]);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        public static void ValidateSceneForAutomation()
        {
            ValidateInstructionContentForAutomation();

            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive
            );
            try
            {
                InteractionStudyFlowController[] flows =
                    FindSceneComponents<InteractionStudyFlowController>(scene);
                InteractionStudyFlowControls[] flowControls =
                    FindSceneComponents<InteractionStudyFlowControls>(scene);
                if (flows.Length != 1 || !flows[0].enabled)
                {
                    throw new InvalidOperationException(
                        "The sign-sequence scene must contain one enabled " +
                        "InteractionStudyFlowController."
                    );
                }
                if (flowControls.Length != 1 || !flowControls[0].enabled)
                {
                    throw new InvalidOperationException(
                        "The sign-sequence scene must contain one enabled " +
                        "InteractionStudyFlowControls."
                    );
                }

                var serializedControls = new SerializedObject(
                    flowControls[0]
                );
                SerializedProperty preStartRootProperty = serializedControls
                    .FindProperty("preStartRoot");
                GameObject preStartRoot = preStartRootProperty
                    ?.objectReferenceValue as GameObject;
                if (preStartRoot == null || !preStartRoot.activeSelf)
                {
                    throw new InvalidOperationException(
                        "The production study pre-start UI must be active " +
                        "when the formal test scene opens."
                    );
                }
                SerializedProperty resultRootProperty = serializedControls
                    .FindProperty("resultRoot");
                GameObject resultRoot = resultRootProperty
                    ?.objectReferenceValue as GameObject;
                if (resultRoot == null || resultRoot.activeSelf)
                {
                    throw new InvalidOperationException(
                        "The formal test scene requires a hidden result page " +
                        "that can be shown when the run completes."
                    );
                }

                if (FindSceneComponents<InteractionSignSequenceTestController>(scene)
                        .Any(item => item.isActiveAndEnabled) ||
                    FindSceneComponents<InteractionSignSequenceTestControls>(scene)
                        .Any(item => item.isActiveAndEnabled))
                {
                    throw new InvalidOperationException(
                        "The one-sentence development harness must be inactive " +
                        "in formal test mode."
                    );
                }

                VRPlayerRig player = FindSceneComponents<VRPlayerRig>(scene)
                    .SingleOrDefault();
                InteractionSeatedRigMover seatedMover =
                    FindSceneComponents<InteractionSeatedRigMover>(scene)
                        .SingleOrDefault();
                if (player == null || seatedMover == null ||
                    seatedMover.Hmd != player.Head ||
                    Mathf.Abs(seatedMover.StepDistance - SeatedMoveStepDistance) >
                        0.0001f)
                {
                    throw new InvalidOperationException(
                        "The sign-sequence scene must configure the seated " +
                        "mover for the player HMD at 0.2m per step."
                    );
                }

                InteractionInstructionControls instructionControls =
                    FindSceneComponents<InteractionInstructionControls>(scene)
                        .Single();
                if (!instructionControls.enabled ||
                    instructionControls.MoveButton == null ||
                    instructionControls.MenuToggleButton == null)
                {
                    throw new InvalidOperationException(
                        "Formal mode requires enabled Move 20cm and menu " +
                        "toggle controls."
                    );
                }

                if (FindLegacyMoveCanvases(scene)
                    .Any(value => value.gameObject.activeSelf))
                {
                    throw new InvalidOperationException(
                        "The legacy duplicate seated-movement canvas must be removed."
                    );
                }

                SpectatorViewStreamer firstPersonRecorder =
                    FindSceneComponents<SpectatorViewStreamer>(scene)
                        .SingleOrDefault();
                if (firstPersonRecorder == null ||
                    firstPersonRecorder.transform != player.transform ||
                    firstPersonRecorder.ViewMode != SpectatorViewMode.HeadsetPov ||
                    firstPersonRecorder.ConfiguredSourceCamera !=
                        player.Head.GetComponent<Camera>() ||
                    firstPersonRecorder.CaptureFrameRate != 30 ||
                    !firstPersonRecorder.KeepsCaptureHorizonLevel ||
                    firstPersonRecorder.StreamsAutomatically ||
                    !firstPersonRecorder.RecordsLocally)
                {
                    throw new InvalidOperationException(
                        "The sign-sequence scene must prepare dedicated " +
                        "player-view recording at 30 FPS."
                    );
                }
                if (firstPersonRecorder.StartsLocalRecordingWithStream)
                {
                    throw new InvalidOperationException(
                        "Local recording must start only for the frame/chest " +
                        "segment, not when network streaming starts."
                    );
                }

                PlayerPointingRayDetector playerPointingDetector =
                    FindSceneComponents<PlayerPointingRayDetector>(scene)
                        .SingleOrDefault();
                if (playerPointingDetector == null ||
                    playerPointingDetector.transform != player.transform ||
                    playerPointingDetector.enabled ||
                    playerPointingDetector.TargetBindings.Count == 0 ||
                    playerPointingDetector.TargetBindings.Count !=
                        FindSceneComponents<GhostPointingDetector>(scene)
                            .Single()
                            .TargetBindings.Count)
                {
                    throw new InvalidOperationException(
                        "The sign-sequence scene must keep the player " +
                        "pointing detector disabled while retaining the " +
                        "full target binding set."
                    );
                }
                OVRHand[] playerHands = player.GetComponentsInChildren<
                    OVRHand>(true);
                if (player.gameObject.layer == 31 ||
                    playerHands.Length != 2 ||
                    playerHands.Any(hand => hand.gameObject.layer == 31))
                {
                    throw new InvalidOperationException(
                        "The recording exclusion layer must not hide the " +
                        "VR player or either tracked hand."
                    );
                }

                FrameChestRecordingSequenceController[] recordingSequences =
                    FindSceneComponents<FrameChestRecordingSequenceController>(
                        scene
                    );
                InteractionTargetBinding[] physicalFrames =
                    FindSceneComponents<InteractionTargetBinding>(scene)
                        .Where(item => item.TargetId.StartsWith(
                            "picture_frame_",
                            StringComparison.Ordinal
                        ))
                        .ToArray();
                if (recordingSequences.Length != 1 ||
                    !recordingSequences[0].isActiveAndEnabled ||
                    physicalFrames.Length != 3 ||
                    physicalFrames.Any(item =>
                        item.GetComponent<BoxCollider>() == null ||
                        item.GetComponent<Rigidbody>() == null ||
                        item.GetComponent<VRGrabEventForwarder>() == null))
                {
                    throw new InvalidOperationException(
                        "The formal frame/chest take requires one live " +
                        "sequence controller and three physical grab frames."
                    );
                }
                FrameChestPasswordButton[] passwordButtons =
                    FindSceneComponents<FrameChestPasswordButton>(scene);
                if (passwordButtons.Length != 4 ||
                    passwordButtons.Any(item =>
                        item.GetComponent<BoxCollider>() == null ||
                        item.GetComponent<Rigidbody>() == null ||
                        item.GetComponent<InteractionTriggerRelay>() == null))
                {
                    throw new InvalidOperationException(
                        "The chest password panel requires four physical " +
                        "hand-triggered buttons."
                    );
                }
                if (FindSceneComponents<Transform>(scene).Any(item =>
                    item.name.StartsWith(
                        "W7Target_picture_frame_",
                        StringComparison.Ordinal
                    )))
                {
                    throw new InvalidOperationException(
                        "Legacy transparent picture-frame proxies must not " +
                        "overlap the physical frames."
                    );
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static void ValidateInstructionContentForAutomation()
        {
            string manifestPath = Path.Combine(
                Application.streamingAssetsPath,
                ContentManifestRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar
                )
            );
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException(
                    "The 31-sign test content manifest is missing: " +
                    manifestPath
                );
            }

            InstructionContentCatalog catalog =
                InteractionInstructionContentManifestReader.Read(
                    File.ReadAllBytes(manifestPath)
                );
            if (catalog.Entries.Count != PhaseSentenceRanges.TotalSentenceCount)
            {
                throw new InvalidOperationException(
                    "The sign-sequence test manifest must contain exactly 31 " +
                    "sentences."
                );
            }

            string contentRoot = Path.GetDirectoryName(manifestPath);
            foreach (InstructionContentReference entry in catalog.Entries)
            {
                string artifactPath = Path.Combine(
                    contentRoot,
                    entry.ArtifactPath.Replace(
                        '/',
                        Path.DirectorySeparatorChar
                    )
                );
                if (!File.Exists(artifactPath))
                {
                    throw new InvalidOperationException(
                        $"The action file for sentence {entry.SentenceId} is " +
                        "missing: " + artifactPath
                    );
                }
            }

            Debug.Log(
                "Validated all 31 staged sign-sequence instruction entries."
            );
        }

        private static T FindOne<T>() where T : Component
        {
            T[] values = FindAll<T>();
            if (values.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one {typeof(T).Name}, found " +
                    values.Length + "."
                );
            }
            return values[0];
        }

        private static T[] FindAll<T>() where T : Component
        {
            return UnityEngine.Object.FindObjectsByType<T>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
                .Where(value => value.gameObject.scene.IsValid())
                .ToArray();
        }

        private static T[] FindSceneComponents<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
        }

        private static void ConfigureSeatedRig(
            VRPlayerRig player,
            InteractionSeatedRigMover seatedMover,
            InteractionInstructionControls copiedControls)
        {
            if (player.Head == null || player.SpawnPoint == null)
            {
                throw new InvalidOperationException(
                    "The sign-sequence scene requires player HMD and spawn " +
                    "point references."
                );
            }

            seatedMover.Configure(player.Head, SeatedMoveStepDistance);
            copiedControls.ConfigureHmd(player.Head);
            copiedControls.ConfigureSeatedMovement(seatedMover);
            SerializedObject controlsSerialized =
                new(copiedControls);
            controlsSerialized.FindProperty("seatedRigMover")
                .objectReferenceValue = seatedMover;
            controlsSerialized.ApplyModifiedPropertiesWithoutUndo();

            foreach (Transform legacyCanvas in FindLegacyMoveCanvases(
                         player.gameObject.scene
                     ))
            {
                legacyCanvas.gameObject.SetActive(false);
                EditorUtility.SetDirty(legacyCanvas.gameObject);
            }

            Transform signerAnchor = FindSceneComponents<Transform>(
                    player.gameObject.scene
                )
                .FirstOrDefault(value =>
                    value.name == "InstructionSignerAnchor");
            if (signerAnchor == null)
            {
                throw new InvalidOperationException(
                    "InstructionSignerAnchor is missing from the scene."
                );
            }

            Transform spawnPoint = player.SpawnPoint;
            Undo.RecordObject(spawnPoint, "Configure sign-sequence spawn point");
            Quaternion spawnRotation = LookAtPlanar(
                RightWindowSpawnEyePosition,
                FirstSignerViewTarget
            );
            spawnPoint.SetPositionAndRotation(
                RightWindowSpawnEyePosition,
                spawnRotation
            );
            spawnPoint.localEulerAngles = spawnRotation.eulerAngles;
            EditorUtility.SetDirty(spawnPoint);
            EditorUtility.SetDirty(seatedMover);
            EditorUtility.SetDirty(copiedControls);
            EditorUtility.SetDirty(player);
        }

        private static void ConfigureFrameChestRecording(
            Scene scene,
            InteractionInstructionControls copiedControls,
            InstructionPresentationController presentation,
            InteractionPhaseCoordinator phaseCoordinator,
            VRPlayerRig player)
        {
            const int recordingHiddenLayer = 31;
            EnsureLayerName(recordingHiddenLayer, "RecordingHidden");

            SetLayerRecursively(player.transform, 0);
            Transform menu = copiedControls.transform.Find(
                "InstructionControlCanvas"
            );
            SetLayerRecursively(menu, recordingHiddenLayer);

            SpectatorViewStreamer recorder = player.GetComponent<
                SpectatorViewStreamer>();
            recorder.ConfigureLocalRecordingStartup(false);
            recorder.ConfigureAutomaticStreaming(false);
            recorder.ConfigureCaptureExclusion(1 << recordingHiddenLayer);
            recorder.ConfigureRecordingStability(
                smoothPose: true,
                keepHorizonLevel: true
            );

            PlayerPointingRayDetector detector = player.GetComponent<
                PlayerPointingRayDetector>();
            detector.ConfigureCaptureExcludedLayer(recordingHiddenLayer);
            detector.enabled = false;

            InteractionDeterministicPresentation deterministic = FindOne<
                InteractionDeterministicPresentation>();
            Transform proxyRoot = FindSceneComponents<Transform>(scene)
                .Single(item => item.name == "W7InteractionProxies");
            Transform chest = FindSceneComponents<Transform>(scene)
                .Single(item => item.name == "chest");

            GameObject panel = EnsureFramePasswordPanel(
                proxyRoot,
                chest,
                out FrameChestPasswordButton[] buttons
            );
            InteractionTargetBinding[] frames = FindSceneComponents<
                InteractionTargetBinding>(scene)
                .Where(item => item.TargetId.StartsWith(
                    "picture_frame_",
                    StringComparison.Ordinal
                ))
                .OrderBy(item => item.TargetId, StringComparer.Ordinal)
                .ToArray();
            TMP_Text[] wallPasswordDisplays = EnsureFrameWallPasswords(
                proxyRoot,
                frames
            );
            FrameChestRecordingSequenceController sequence =
                GetOrAddSingle<FrameChestRecordingSequenceController>(
                    copiedControls.gameObject
                );
            sequence.Configure(
                presentation,
                phaseCoordinator,
                FindOne<InteractionStudyFlowController>(),
                recorder,
                deterministic,
                frames,
                buttons,
                wallPasswordDisplays,
                panel
            );
            for (int index = 0; index < buttons.Length; index++)
            {
                buttons[index].Configure(
                    buttons[index].ButtonId,
                    sequence,
                    buttons[index].transform.Find("Visual"),
                    buttons[index].GetComponent<Collider>()
                );
                InteractionTriggerRelay relay = buttons[index].GetComponent<
                    InteractionTriggerRelay>();
                relay.Configure(
                    buttons[index],
                    phaseCoordinator.AllowedInteractorRoots.ToArray()
                );
                Transform pad = panel.transform.Find(
                    "LargePad_" + buttons[index].ButtonId
                );
                InteractionTriggerRelay padRelay = pad != null
                    ? pad.GetComponent<InteractionTriggerRelay>()
                    : null;
                if (padRelay != null)
                {
                    padRelay.Configure(
                        buttons[index],
                        phaseCoordinator.AllowedInteractorRoots.ToArray()
                    );
                }
            }
            EditorUtility.SetDirty(sequence);
            EditorUtility.SetDirty(recorder);
            EditorUtility.SetDirty(detector);
            EditorUtility.SetDirty(panel);
        }

        private static GameObject EnsureFramePasswordPanel(
            Transform proxyRoot,
            Transform chest,
            out FrameChestPasswordButton[] buttons)
        {
            Transform existing = proxyRoot.Find("FrameChestPasswordPanel");
            GameObject panel = existing != null
                ? existing.gameObject
                : new GameObject("FrameChestPasswordPanel");
            if (existing == null)
            {
                panel.transform.SetParent(proxyRoot, false);
            }
            panel.transform.SetPositionAndRotation(
                chest.position + Vector3.left * 0.48f +
                    Vector3.up * 0.15f + Vector3.forward * 0.18f,
                Quaternion.identity
            );
            panel.transform.localScale = Vector3.one;
            panel.SetActive(false);

            Transform board = panel.transform.Find("Board");
            GameObject boardObject = board != null
                ? board.gameObject
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            boardObject.name = "Board";
            if (board == null)
            {
                boardObject.transform.SetParent(panel.transform, false);
            }
            boardObject.transform.localPosition = Vector3.zero;
            boardObject.transform.localRotation = Quaternion.identity;
            boardObject.transform.localScale = new Vector3(0.52f, 0.5f, 0.035f);
            Collider boardCollider = boardObject.GetComponent<Collider>();
            if (boardCollider != null)
            {
                UnityEngine.Object.DestroyImmediate(boardCollider);
            }

            string[] ids = { "blue", "red", "yellow", "green" };
            Color[] colors =
            {
                new(0.08f, 0.28f, 0.92f, 1f),
                new(0.88f, 0.08f, 0.08f, 1f),
                new(0.95f, 0.72f, 0.04f, 1f),
                new(0.08f, 0.68f, 0.2f, 1f)
            };
            var result = new FrameChestPasswordButton[ids.Length];
            for (int index = 0; index < ids.Length; index++)
            {
                Transform buttonTransform = panel.transform.Find(
                    "Button_" + ids[index]
                );
                if (buttonTransform != null &&
                    buttonTransform.GetComponent<BoxCollider>() == null)
                {
                    Undo.DestroyObjectImmediate(buttonTransform.gameObject);
                    buttonTransform = null;
                }
                GameObject button = buttonTransform != null
                    ? buttonTransform.gameObject
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                if (buttonTransform == null)
                {
                    button.name = "Button_" + ids[index];
                    button.transform.SetParent(panel.transform, false);
                }
                button.transform.localPosition = new Vector3(
                    (index - 1.5f) * 0.115f,
                    0.08f,
                    0.035f
                );
                button.transform.localRotation = Quaternion.identity;
                button.transform.localScale = Vector3.one;
                Renderer buttonRenderer = button.GetComponent<Renderer>();
                if (buttonRenderer != null)
                {
                    buttonRenderer.enabled = false;
                }
                BoxCollider collider = button.GetComponent<BoxCollider>();
                if (collider == null)
                {
                    collider = Undo.AddComponent<BoxCollider>(button);
                }
                if (collider == null)
                {
                    throw new InvalidOperationException(
                        "Could not create password-button collider for " +
                        ids[index] + "."
                    );
                }
                collider.isTrigger = true;
                collider.size = new Vector3(0.085f, 0.12f, 0.045f);
                Rigidbody body = button.GetComponent<Rigidbody>();
                if (body == null)
                {
                    body = Undo.AddComponent<Rigidbody>(button);
                }
                if (body == null)
                {
                    throw new InvalidOperationException(
                        "Could not create password-button rigidbody for " +
                        ids[index] + "."
                    );
                }
                body.isKinematic = false;
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                Transform visual = button.transform.Find("Visual");
                GameObject visualObject = visual != null
                    ? visual.gameObject
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                visualObject.name = "Visual";
                if (visual == null)
                {
                    visualObject.transform.SetParent(button.transform, false);
                }
                visualObject.transform.localPosition = Vector3.zero;
                visualObject.transform.localScale = new Vector3(0.075f, 0.1f, 0.035f);
                Collider visualCollider = visualObject.GetComponent<Collider>();
                if (visualCollider != null)
                {
                    UnityEngine.Object.DestroyImmediate(visualCollider);
                }
                TextMeshPro label = button.transform.Find("Label")?.GetComponent<
                    TextMeshPro>();
                if (label == null)
                {
                    GameObject labelObject = new GameObject(
                        "Label",
                        typeof(TextMeshPro)
                    );
                    labelObject.transform.SetParent(button.transform, false);
                    label = labelObject.GetComponent<TextMeshPro>();
                }
                label.text = index.ToString();
                label.fontSize = 0.18f;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                label.transform.localPosition = new Vector3(0f, 0f, 0.021f);
                label.transform.localRotation = Quaternion.identity;
                label.transform.localScale = Vector3.one;

                FrameChestPasswordButton component = button.GetComponent<
                    FrameChestPasswordButton>();
                if (component == null)
                {
                    component = Undo.AddComponent<FrameChestPasswordButton>(
                        button
                    );
                }
                component.Configure(ids[index], null, visualObject.transform, collider);
                component.ConfigureColor(colors[index]);
                InteractionTriggerRelay relay = button.GetComponent<
                    InteractionTriggerRelay>();
                if (relay == null)
                {
                    relay = Undo.AddComponent<InteractionTriggerRelay>(button);
                }

                Transform padTransform = panel.transform.Find(
                    "LargePad_" + ids[index]
                );
                GameObject pad = padTransform != null
                    ? padTransform.gameObject
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                if (padTransform == null)
                {
                    pad.name = "LargePad_" + ids[index];
                    pad.transform.SetParent(panel.transform, false);
                }
                pad.transform.localPosition = new Vector3(
                    (index - 1.5f) * 0.115f,
                    -0.13f,
                    0.055f
                );
                pad.transform.localRotation = Quaternion.identity;
                pad.transform.localScale = new Vector3(0.1f, 0.16f, 0.065f);
                BoxCollider padCollider = pad.GetComponent<BoxCollider>();
                if (padCollider == null)
                {
                    padCollider = Undo.AddComponent<BoxCollider>(pad);
                }
                padCollider.isTrigger = true;
                Rigidbody padBody = pad.GetComponent<Rigidbody>();
                if (padBody == null)
                {
                    padBody = Undo.AddComponent<Rigidbody>(pad);
                }
                padBody.isKinematic = false;
                padBody.useGravity = false;
                padBody.constraints = RigidbodyConstraints.FreezeAll;
                padBody.collisionDetectionMode =
                    CollisionDetectionMode.ContinuousSpeculative;
                Renderer padRenderer = pad.GetComponent<Renderer>();
                if (padRenderer != null)
                {
                    Material sharedMaterial = visualObject
                        .GetComponent<Renderer>()?.sharedMaterial;
                    if (sharedMaterial != null)
                    {
                        padRenderer.sharedMaterial = sharedMaterial;
                    }
                }
                InteractionTriggerRelay padRelay = pad.GetComponent<
                    InteractionTriggerRelay>();
                if (padRelay == null)
                {
                    padRelay = Undo.AddComponent<InteractionTriggerRelay>(pad);
                }

                Transform padLabelTransform = panel.transform.Find(
                    "LargePadLabel_" + ids[index]
                );
                GameObject padLabelObject = padLabelTransform != null
                    ? padLabelTransform.gameObject
                    : new GameObject(
                        "LargePadLabel_" + ids[index],
                        typeof(TextMeshPro)
                    );
                if (padLabelTransform == null)
                {
                    padLabelObject.transform.SetParent(panel.transform, false);
                }
                TextMeshPro padLabel = padLabelObject.GetComponent<TextMeshPro>();
                padLabel.text = index.ToString();
                padLabel.fontSize = 0.18f;
                padLabel.fontStyle = FontStyles.Bold;
                padLabel.alignment = TextAlignmentOptions.Center;
                padLabel.color = Color.white;
                padLabel.transform.localPosition = new Vector3(
                    (index - 1.5f) * 0.115f,
                    -0.13f,
                    0.091f
                );
                padLabel.transform.localRotation = Quaternion.identity;
                padLabel.transform.localScale = Vector3.one;
                component.ConfigureAuxiliaryRenderers(padRenderer);
                component.ConfigureColor(colors[index]);
                result[index] = component;
                EditorUtility.SetDirty(button);
                EditorUtility.SetDirty(pad);
                EditorUtility.SetDirty(padLabelObject);
                EditorUtility.SetDirty(component);
                EditorUtility.SetDirty(relay);
                EditorUtility.SetDirty(padRelay);
            }

            buttons = result;
            return panel;
        }

        private static void PrepareExistingFramePasswordButtonsForW7(
            Scene scene)
        {
            foreach (FrameChestPasswordButton button in
                FindSceneComponents<FrameChestPasswordButton>(scene))
            {
                Rigidbody body = button.GetComponent<Rigidbody>();
                if (body == null)
                {
                    continue;
                }
                body.useGravity = false;
                body.isKinematic = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.detectCollisions = true;
                body.collisionDetectionMode =
                    CollisionDetectionMode.ContinuousSpeculative;
                EditorUtility.SetDirty(body);
            }
        }

        private static TMP_Text[] EnsureFrameWallPasswords(
            Transform proxyRoot,
            IReadOnlyList<InteractionTargetBinding> frames)
        {
            var result = new TMP_Text[frames.Count];
            for (int index = 0; index < frames.Count; index++)
            {
                InteractionTargetBinding frame = frames[index];
                if (frame == null)
                {
                    continue;
                }
                string name = "FramePasswordWall_" + frame.TargetId;
                Transform existing = proxyRoot.Find(name);
                GameObject displayObject = existing != null
                    ? existing.gameObject
                    : new GameObject(name, typeof(TextMeshPro));
                if (existing == null)
                {
                    displayObject.transform.SetParent(proxyRoot, false);
                }
                Bounds bounds = GetWorldBounds(frame.transform);
                TextMeshPro password = displayObject.GetComponent<TextMeshPro>();
                password.text = "0   1   2   3";
                password.fontSize = 0.32f;
                password.fontStyle = FontStyles.Bold;
                password.alignment = TextAlignmentOptions.Center;
                password.color = Color.white;
                password.transform.SetPositionAndRotation(
                    new Vector3(
                        bounds.center.x,
                        bounds.center.y,
                        bounds.max.z + 0.025f
                    ),
                    Quaternion.Euler(0f, 180f, 0f)
                );
                password.transform.localScale = Vector3.one;
                password.gameObject.SetActive(false);
                result[index] = password;
                EditorUtility.SetDirty(displayObject);
            }
            return result;
        }

        private static Bounds GetWorldBounds(Transform root)
        {
            Renderer[] renderers = root == null
                ? Array.Empty<Renderer>()
                : root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root != null ? root.position : Vector3.zero, Vector3.one);
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return bounds;
        }

        private static void EnsureLayerName(int layer, string name)
        {
            UnityEngine.Object tagManager = AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/TagManager.asset"
            )[0];
            SerializedObject serialized = new SerializedObject(tagManager);
            SerializedProperty layers = serialized.FindProperty("layers");
            if (layers != null && layer < layers.arraySize)
            {
                layers.GetArrayElementAtIndex(layer).stringValue = name;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null)
            {
                return;
            }
            root.gameObject.layer = layer;
            foreach (Transform child in root)
            {
                SetLayerRecursively(child, layer);
            }
        }

        private static void ConfigureFirstPersonRecording(VRPlayerRig player)
        {
            Camera headsetCamera = player.Head != null
                ? player.Head.GetComponent<Camera>()
                : null;
            if (headsetCamera == null)
            {
                throw new InvalidOperationException(
                    "The sign-sequence player HMD requires a Camera for " +
                    "first-person recording."
                );
            }

            SpectatorViewStreamer recorder =
                GetOrAddSingle<SpectatorViewStreamer>(player.gameObject);
            recorder.Configure(
                SpectatorViewMode.HeadsetPov,
                "255.255.255.255",
                SpectatorViewStreamer.DefaultPort,
                headsetCamera,
                null,
                SpectatorViewStreamer.DefaultRecordingWidth,
                SpectatorViewStreamer.DefaultRecordingHeight,
                SpectatorViewStreamer.DefaultRecordingFrameRate,
                SpectatorViewStreamer.DefaultRecordingJpegQuality
            );
            recorder.ConfigureLocalRecording(
                enabled: true,
                folderName: "FirstPersonVideos",
                segmentMinutes: 10
            );
            EditorUtility.SetDirty(recorder);
        }

        private static void ConfigurePlayerPointing(
            VRPlayerRig player,
            InteractionStudyCaptureBinding captureBinding,
            GhostPointingDetector ghostPointingDetector)
        {
            if (captureBinding == null || ghostPointingDetector == null)
            {
                throw new InvalidOperationException(
                    "The sign-sequence scene requires the live hand capture " +
                    "binding and ghost pointing detector before player " +
                    "pointing can be configured."
                );
            }

            InteractionTargetHighlightVisual highlight =
                GetOrAddSingle<InteractionTargetHighlightVisual>(
                    player.gameObject
                );
            PlayerPointingRayDetector detector =
                GetOrAddSingle<PlayerPointingRayDetector>(player.gameObject);
            detector.Configure(
                player,
                captureBinding.LeftSkeleton as OVRSkeleton,
                captureBinding.RightSkeleton as OVRSkeleton,
                ghostPointingDetector.TargetBindings.ToArray(),
                highlight
            );
            EditorUtility.SetDirty(detector);
            EditorUtility.SetDirty(highlight);
        }

        private static Quaternion LookAtPlanar(Vector3 from, Vector3 target)
        {
            Vector3 direction = Vector3.ProjectOnPlane(target - from, Vector3.up);
            return direction.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : Quaternion.identity;
        }

        private static Transform[] FindLegacyMoveCanvases(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(value => value.name == "InteractionSeatedMoveCanvas")
                .ToArray();
        }

        private static void EnableProductionStudyUi(
            InteractionStudyFlowControls controls)
        {
            controls.enabled = true;

            var serializedControls = new SerializedObject(controls);
            SerializedProperty preStartRootProperty = serializedControls
                .FindProperty("preStartRoot");
            GameObject preStartRoot = preStartRootProperty
                ?.objectReferenceValue as GameObject;
            if (preStartRoot == null)
            {
                throw new InvalidOperationException(
                    "InteractionStudyFlowControls.preStartRoot is missing."
                );
            }

            preStartRoot.SetActive(true);
            EditorUtility.SetDirty(preStartRoot);
            EditorUtility.SetDirty(controls);
        }

        private static void ConfigureProductionResultUi(
            InteractionStudyFlowControls controls,
            InteractionInstructionControls instructionControls,
            Transform hmd)
        {
            if (controls == null || instructionControls == null || hmd == null)
            {
                throw new InvalidOperationException(
                    "Formal result UI requires flow controls, instruction " +
                    "controls, and the player HMD."
                );
            }
            GameObject resultSurface = W8LocalUnityIntegrationSetup
                .EnsureResultSurfaceForAutomation(
                    controls.transform,
                    hmd
                );
            TMP_Text outcome = resultSurface.transform.Find("Outcome")
                ?.GetComponent<TMP_Text>();
            TMP_Text saveStatus = resultSurface.transform.Find("SaveStatus")
                ?.GetComponent<TMP_Text>();
            UnityEngine.UI.Button acknowledge = resultSurface.transform
                .Find("Acknowledge")?.GetComponent<UnityEngine.UI.Button>();
            if (outcome == null || saveStatus == null || acknowledge == null)
            {
                throw new InvalidOperationException(
                    "The generated formal result page is incomplete."
                );
            }
            controls.Configure(
                controls.FlowController,
                instructionControls,
                controls.PreStartRoot,
                controls.StartButton,
                controls.StatusLabel,
                controls.ProgressLabel,
                resultSurface,
                outcome,
                saveStatus,
                acknowledge
            );
            EditorUtility.SetDirty(resultSurface);
            EditorUtility.SetDirty(controls);
        }

        private static T GetOrAddSingle<T>(GameObject target)
            where T : Component
        {
            T[] existing = target.GetComponents<T>();
            T result = existing.FirstOrDefault() ?? target.AddComponent<T>();
            for (int index = 1; index < existing.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(existing[index]);
            }
            return result;
        }

        private static void RestoreProductionControlNames(Transform owner)
        {
            Transform canvas = owner.Find("InstructionControlCanvas");
            if (canvas == null)
            {
                throw new InvalidOperationException(
                    "InstructionControlCanvas is missing from the copied scene."
                );
            }

            RenameIfPresent(canvas, "Previous", "GiveUpPhase");
            RenameIfPresent(canvas, "Next", "AbortRun");
        }

        private static void RenameIfPresent(
            Transform parent,
            string currentName,
            string requiredName)
        {
            Transform required = parent.Find(requiredName);
            if (required != null)
            {
                return;
            }

            Transform current = parent.Find(currentName);
            if (current == null)
            {
                throw new InvalidOperationException(
                    $"Copied control '{currentName}' is missing."
                );
            }
            current.name = requiredName;
            EditorUtility.SetDirty(current.gameObject);
        }
    }
}
