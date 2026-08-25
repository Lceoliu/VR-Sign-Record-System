import { describe, expect, it } from 'vitest'
import {
  cameraReadinessForCurrentStream,
  captureDirective,
  participantIdsMatch,
  reconcilePolledInteractionRun,
  transitionWebcamRecovery,
  webcamRecoveryFilename,
  type CameraReadinessStream,
} from './interactionCapture'
import type { InteractionRunSnapshot } from './types'

const run: InteractionRunSnapshot = {
  schema_version: 1,
  batch_id: 'pilot-20260826',
  participant_id: 'P001',
  run_id: 'run_alpha',
  state: 'Scheduled',
  start_at_utc: '2026-08-26T10:15:32Z',
  missing_artifacts: ['events', 'poses', 'objects', 'summary', 'webcam'],
  acknowledged: false,
  terminal_utc: null,
  abort_reason: null,
}

describe('captureDirective', () => {
  it('waits for camera without inventing a successful recording state', () => {
    expect(captureDirective(run, false, null, false, Date.parse(run.start_at_utc) - 500)).toEqual({
      action: 'wait-for-camera',
    })
  })

  it('schedules against the Quest-authored run start time', () => {
    expect(captureDirective(run, true, null, false, Date.parse(run.start_at_utc) - 750)).toEqual({
      action: 'schedule',
      delayMs: 750,
    })
  })

  it('reports a late immediate start explicitly', () => {
    expect(captureDirective(run, true, null, false, Date.parse(run.start_at_utc) + 250)).toEqual({
      action: 'start',
      lateByMs: 250,
    })
  })

  it('stops only the capture bound to a terminal run', () => {
    const completed = { ...run, state: 'Completed' as const }
    const aborted = { ...run, state: 'Aborted' as const }
    expect(captureDirective(completed, true, run.run_id, true, Date.now())).toEqual({ action: 'stop' })
    expect(captureDirective(aborted, true, run.run_id, true, Date.now())).toEqual({ action: 'stop' })
    expect(captureDirective(completed, true, 'run_other', true, Date.now())).toEqual({ action: 'none' })
  })
})

describe('participantIdsMatch', () => {
  it('requires an exact non-empty pseudonymous identifier', () => {
    expect(participantIdsMatch(' P001 ', 'P001')).toBe(true)
    expect(participantIdsMatch('', 'P001')).toBe(false)
    expect(participantIdsMatch('P001', 'P002')).toBe(false)
  })
})

function cameraStream(
  active: boolean,
  trackStates: MediaStreamTrackState[],
): CameraReadinessStream {
  return {
    active,
    getVideoTracks: () => trackStates.map((readyState) => ({ readyState })),
  }
}

describe('cameraReadinessForCurrentStream', () => {
  it('requires an active stream with at least one live video track', () => {
    const live = cameraStream(true, ['live'])
    const ended = cameraStream(true, ['ended'])
    const noTrack = cameraStream(true, [])
    const inactive = cameraStream(false, ['live'])

    expect(cameraReadinessForCurrentStream(live, live)).toBe(true)
    expect(cameraReadinessForCurrentStream(ended, ended)).toBe(false)
    expect(cameraReadinessForCurrentStream(noTrack, noTrack)).toBe(false)
    expect(cameraReadinessForCurrentStream(inactive, inactive)).toBe(false)
  })

  it('ignores an ended callback from a stream that has already been replaced', () => {
    const oldStream = cameraStream(false, ['ended'])
    const currentStream = cameraStream(true, ['live'])

    expect(cameraReadinessForCurrentStream(currentStream, oldStream)).toBeNull()
    expect(cameraReadinessForCurrentStream(currentStream, currentStream)).toBe(true)
  })
})

describe('reconcilePolledInteractionRun', () => {
  it('drives scheduled, running, and terminal capture without WebSocket events', () => {
    const running = { ...run, state: 'Running' as const }
    const completed = {
      ...run,
      state: 'Completed' as const,
      terminal_utc: '2026-08-26T10:20:00Z',
    }
    const scheduledPoll = reconcilePolledInteractionRun(
      { acceptedSequence: 0, run: null },
      {
        sequence: 1,
        readinessActiveRun: run,
        boundRunId: null,
        boundRunSnapshot: null,
      },
    )
    expect(captureDirective(
      scheduledPoll.run!,
      true,
      null,
      false,
      Date.parse(run.start_at_utc) - 500,
    )).toEqual({ action: 'schedule', delayMs: 500 })

    const runningPoll = reconcilePolledInteractionRun(
      scheduledPoll,
      {
        sequence: 2,
        readinessActiveRun: running,
        boundRunId: run.run_id,
        boundRunSnapshot: running,
      },
    )
    expect(captureDirective(runningPoll.run!, true, run.run_id, true, Date.now())).toEqual({
      action: 'none',
    })

    const terminalPoll = reconcilePolledInteractionRun(
      runningPoll,
      {
        sequence: 3,
        readinessActiveRun: null,
        boundRunId: run.run_id,
        boundRunSnapshot: completed,
      },
    )

    expect(terminalPoll).toEqual({ acceptedSequence: 3, run: completed })
    expect(captureDirective(terminalPoll.run!, true, run.run_id, true, Date.now())).toEqual({
      action: 'stop',
    })
  })

  it('does not let an older readiness response replace an accepted terminal state', () => {
    const completed = {
      ...run,
      state: 'Completed' as const,
      terminal_utc: '2026-08-26T10:20:00Z',
    }
    const current = { acceptedSequence: 8, run: completed }

    expect(reconcilePolledInteractionRun(
      current,
      {
        sequence: 7,
        readinessActiveRun: run,
        boundRunId: null,
        boundRunSnapshot: null,
      },
    )).toBe(current)
  })
})

describe('webcam recovery state', () => {
  it('retains the exact Blob after failure and blocks a concurrent retry', () => {
    const blob = new Blob(['webcam-a'], { type: 'video/webm' })
    const retained = transitionWebcamRecovery(new Map(), {
      type: 'retain',
      runId: 'run_recovery_a',
      blob,
    })
    const started = transitionWebcamRecovery(retained.state, {
      type: 'start-upload',
      runId: 'run_recovery_a',
    })
    const duplicate = transitionWebcamRecovery(started.state, {
      type: 'start-upload',
      runId: 'run_recovery_a',
    })
    const failed = transitionWebcamRecovery(started.state, {
      type: 'upload-failed',
      runId: 'run_recovery_a',
      blob,
      attempt: 1,
      error: 'Host still reports webcam missing',
    })

    expect(started.upload).toEqual({ runId: 'run_recovery_a', blob, attempt: 1 })
    expect(duplicate.upload).toBeNull()
    expect(duplicate.state).toBe(started.state)
    expect(failed.state.get('run_recovery_a')).toMatchObject({
      blob,
      status: 'failed',
      attempt: 1,
      error: 'Host still reports webcam missing',
    })
  })

  it('isolates Runs and releases only the Blob confirmed by its current attempt', () => {
    const blobA = new Blob(['webcam-a'], { type: 'video/webm' })
    const replacementA = new Blob(['must-not-replace-a'], { type: 'video/webm' })
    const blobB = new Blob(['webcam-b'], { type: 'video/webm' })
    let state = transitionWebcamRecovery(new Map(), {
      type: 'retain',
      runId: 'run_recovery_a',
      blob: blobA,
    }).state
    state = transitionWebcamRecovery(state, {
      type: 'retain',
      runId: 'run_recovery_b',
      blob: blobB,
    }).state
    state = transitionWebcamRecovery(state, {
      type: 'retain',
      runId: 'run_recovery_a',
      blob: replacementA,
    }).state

    const firstAttempt = transitionWebcamRecovery(state, {
      type: 'start-upload',
      runId: 'run_recovery_a',
    })
    const firstFailure = transitionWebcamRecovery(firstAttempt.state, {
      type: 'upload-failed',
      runId: 'run_recovery_a',
      blob: blobA,
      attempt: 1,
      error: 'offline',
    })
    const retry = transitionWebcamRecovery(firstFailure.state, {
      type: 'start-upload',
      runId: 'run_recovery_a',
    })
    const staleConfirmation = transitionWebcamRecovery(retry.state, {
      type: 'host-confirmed',
      runId: 'run_recovery_a',
      blob: blobA,
      attempt: 1,
    })
    const confirmed = transitionWebcamRecovery(staleConfirmation.state, {
      type: 'host-confirmed',
      runId: 'run_recovery_a',
      blob: blobA,
      attempt: 2,
    })

    expect(state.get('run_recovery_a')?.blob).toBe(blobA)
    expect(state.get('run_recovery_b')?.blob).toBe(blobB)
    expect(retry.upload).toEqual({ runId: 'run_recovery_a', blob: blobA, attempt: 2 })
    expect(staleConfirmation.state).toBe(retry.state)
    expect(confirmed.state.has('run_recovery_a')).toBe(false)
    expect(confirmed.state.get('run_recovery_b')?.blob).toBe(blobB)
    expect(webcamRecoveryFilename('run_recovery_b')).toBe('run_recovery_b.webcam.webm')
  })
})
