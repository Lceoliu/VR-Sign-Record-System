# SignVR Interaction Experiment

This context covers the sign-guided interaction test in which a hearing participant follows previously recorded sign-language instructions through one complete six-phase run.

## Language

**Instruction Signer**:
The signer whose recorded sign-language performance is presented to a participant as task guidance.
_Avoid_: Teacher, avatar operator

**Participant**:
A hearing person who follows the Instruction Signer and performs the interaction tasks.
_Avoid_: Recorder, player, subject

**Recording Take**:
One captured performance from the Instruction Signer in the Recorder workflow.
_Avoid_: Pose Take, participant recording

**Instruction Clip**:
A replayable instruction resolved from one completed Recording Take for a specific Instruction Signer and Task Variant. For the first delivery, resolution selects the latest completed take for that signer and sentence identifier.
_Avoid_: Video, participant recording

**Instruction Source Selection**:
The two-part choice recorded for each Instruction Phase: first a sentence identifier is selected from that phase's variant range, then an Instruction Signer is selected for that sentence. The first delivery fixes one signer across all six phases; later runs may select a signer independently per phase.
_Avoid_: Take selection, global signer

**Run Content Manifest**:
The resolved per-phase record of sentence identifier, Instruction Signer, exact Recording Take, and source artifact used by one Interaction Run.
_Avoid_: Folder scan, current file

**Interaction Run**:
One continuous execution in which a Participant completes all six Instruction Phases.
_Avoid_: Take, session, single task

**Instruction Phase**:
One of the six participant-visible instruction units in an Interaction Run.
_Avoid_: Development phase, component

**Interaction Step**:
One concrete manipulation within an Instruction Phase, such as entering a password, opening a lid, or pressing a button.
_Avoid_: Phase, task variant

**Task Variant**:
One resolved target combination for an Instruction Phase, such as a specific box, coin-and-plate pair, button set, or breaker order.
_Avoid_: Seed, clip

**Run Plan**:
The immutable plan created when the Participant presses Start, containing the new Run ID, random seed, Assistance Condition, six independently sampled Task Variants, per-phase Instruction Sources, exact Recording Takes, and generated passwords. The first study build fixes Wang as Instruction Signer while selecting one sentence from each phase's full variant range. Aborting, replaying, or resetting does not rewrite this plan.
_Avoid_: Mutable scene state, latest selection

**Assistance Condition**:
The presentation condition assigned once per Interaction Run and held constant across all six phases. The allowed conditions are `TextAndPointing` (Instruction Bubble plus fingertip ray and target highlight), `TextOnly` (Instruction Bubble without pointing assistance), and `SignOnly` (neither assistance). `PointingOnly` is intentionally not an allowed condition.
_Avoid_: Guidance toggle, per-phase condition

**Assistance Assignment Block**:
A shuffled three-Run allocation containing each Assistance Condition exactly once. The next block is reshuffled after all three entries are consumed. A slot is consumed when Start creates its Run Plan and is not returned after an abort. Block state is intentionally session-local and resets whenever the Interaction application launches.
_Avoid_: Independent coin flip, persisted study schedule

**Pointing Assistance**:
The ghost signer's fingertip ray and the corresponding target highlight, shown together for the current instruction as soon as the inferred fingertip ray hits an eligible target. There is no dwell-before-show threshold; after the ray stops hitting, both visuals may remain for an approximately 150 ms grace period to suppress flicker.
_Avoid_: Participant hand ray, recorded cue timeline

**Interaction Feedback**:
The immediate visible or audible acknowledgement that a Participant's manipulation was received. Pilot implementations may simplify physical motion, but their validation, ordering, reset, and phase-transition semantics remain authoritative.
_Avoid_: Physics fidelity, visual polish

**Instruction Bubble**:
The existing rounded prompt surface, Chinese font, visual styling, and canonical sentence text already used during the 31-sentence recording workflow. Recorder keeps it in the HMD's upper-left field of view; Interaction reuses its presentation above the active ghost signer's head without rewriting the 31 sentence strings.
_Avoid_: New subtitle system, screen-space caption

**Delayed Transcript**:
The canonical Chinese instruction text revealed in the Instruction Bubble only after the first sign-language playback finishes and a short delay elapses, representing a completed sign-to-text result.
_Avoid_: Live subtitle, Guidance mode

**Phase Replay Allowance**:
The single optional replay available after the first instruction playback completes. Using it is logged; the replay control remains unavailable before completion and cannot be used a second time in the same phase.
_Avoid_: Automatic loop, unlimited replay

**Stuck Phase**:
An Instruction Phase that the Participant gives up after the one permitted replay. It is retained as an unsuccessful phase result before the Interaction Run advances to the next phase.
_Avoid_: Aborted Run, technical failure

**Aborted Run**:
An Interaction Run ended by the Participant or operator for safety, discomfort, tracking failure, or another whole-run reason before all six phases complete. Its Run Plan, partial capture, replay usage, failure point, and abort reason are retained before the system returns to the pre-start state.
_Avoid_: Deleted run, successful run

**Engineering Pilot Run**:
An Interaction Run used to establish system stability and data completeness before participant data collection; its result is not treated as paper evidence.
_Avoid_: Formal test, study sample

**Study Run**:
An Interaction Run whose pseudonymous participant data is intentionally retained for analysis after the Engineering Pilot gate passes.
_Avoid_: Demo, synthetic sample

**Experiment Capture**:
The time-aligned record of participant motion, interaction events, and scene state produced during an Interaction Run.
_Avoid_: Video, Recording Take

**Webcam Capture**:
The PC-connected camera video recorded by the Host from the synchronized start of an Interaction Run until completion or abort. It documents the participant test and is distinct from the later high-quality Unity Showcase recording.
_Avoid_: Quest preview, Showcase Replay

**Interaction Host**:
The PC service that receives the Quest-authored Run Plan and Experiment Capture, synchronizes and stores Webcam Capture, and reports readiness. It does not choose Task Variants, Assistance Condition, passwords, Instruction Signers, or Recording Takes.
_Avoid_: Run planner, Recording Take service

**Showcase Replay**:
A presentation-oriented replay derived from an Experiment Capture and enhanced with improved cameras and visual effects.
_Avoid_: Study Run, raw result
