import type { InteractionRunSnapshot } from './types'

export type CaptureDirective =
  | { action: 'none' }
  | { action: 'wait-for-camera' }
  | { action: 'schedule'; delayMs: number }
  | { action: 'start'; lateByMs: number }
  | { action: 'stop' }

export type CameraReadinessStream = {
  readonly active: boolean
  getVideoTracks: () => ReadonlyArray<Pick<MediaStreamTrack, 'readyState'>>
}

export type InteractionRunPollState = {
  acceptedSequence: number
  run: InteractionRunSnapshot | null
}

export type InteractionRunPollResponse = {
  sequence: number
  readinessActiveRun: InteractionRunSnapshot | null
  boundRunId: string | null
  boundRunSnapshot: InteractionRunSnapshot | null
}

export type WebcamRecoveryStatus = 'pending' | 'uploading' | 'failed'

export type WebcamRecoveryEntry = {
  runId: string
  blob: Blob
  status: WebcamRecoveryStatus
  attempt: number
  error: string | null
}

export type WebcamRecoveryState = ReadonlyMap<string, WebcamRecoveryEntry>

export type WebcamRecoveryUpload = {
  runId: string
  blob: Blob
  attempt: number
}

export type WebcamRecoveryAction =
  | { type: 'retain'; runId: string; blob: Blob }
  | { type: 'start-upload'; runId: string }
  | {
      type: 'upload-failed'
      runId: string
      blob: Blob
      attempt: number
      error: string
    }
  | {
      type: 'host-confirmed'
      runId: string
      blob: Blob
      attempt: number
    }

export type WebcamRecoveryTransition = {
  state: WebcamRecoveryState
  upload: WebcamRecoveryUpload | null
}

export function transitionWebcamRecovery(
  current: WebcamRecoveryState,
  action: WebcamRecoveryAction,
): WebcamRecoveryTransition {
  const unchanged = (): WebcamRecoveryTransition => ({ state: current, upload: null })
  const existing = current.get(action.runId)

  if (action.type === 'retain') {
    if (existing) return unchanged()
    const next = new Map(current)
    next.set(action.runId, {
      runId: action.runId,
      blob: action.blob,
      status: 'pending',
      attempt: 0,
      error: null,
    })
    return { state: next, upload: null }
  }

  if (!existing) return unchanged()
  if (action.type === 'start-upload') {
    if (existing.status === 'uploading') return unchanged()
    const started = {
      ...existing,
      status: 'uploading' as const,
      attempt: existing.attempt + 1,
      error: null,
    }
    const next = new Map(current)
    next.set(action.runId, started)
    return {
      state: next,
      upload: {
        runId: started.runId,
        blob: started.blob,
        attempt: started.attempt,
      },
    }
  }

  if (
    existing.blob !== action.blob
    || existing.attempt !== action.attempt
    || existing.status !== 'uploading'
  ) {
    return unchanged()
  }
  const next = new Map(current)
  if (action.type === 'host-confirmed') {
    next.delete(action.runId)
  } else {
    next.set(action.runId, {
      ...existing,
      status: 'failed',
      error: action.error,
    })
  }
  return { state: next, upload: null }
}

export function webcamRecoveryFilename(runId: string): string {
  return `${runId}.webcam.webm`
}

export function reconcilePolledInteractionRun(
  current: InteractionRunPollState,
  response: InteractionRunPollResponse,
): InteractionRunPollState {
  if (response.sequence <= current.acceptedSequence) return current
  if (response.boundRunId !== null) {
    if (response.boundRunSnapshot?.run_id !== response.boundRunId) return current
    return {
      acceptedSequence: response.sequence,
      run: response.boundRunSnapshot,
    }
  }
  return {
    acceptedSequence: response.sequence,
    run: response.readinessActiveRun,
  }
}

export function cameraReadinessForCurrentStream(
  currentStream: CameraReadinessStream | null,
  observedStream: CameraReadinessStream | null,
): boolean | null {
  if (observedStream !== currentStream) return null
  return Boolean(
    observedStream?.active
    && observedStream.getVideoTracks().some((track) => track.readyState === 'live'),
  )
}

export function isTerminalInteractionRun(run: InteractionRunSnapshot): boolean {
  return run.state === 'Completed' || run.state === 'Aborted'
}

export function participantIdsMatch(expected: string, actual: string | null): boolean {
  const normalized = expected.trim()
  return normalized.length > 0 && normalized === actual
}

export function captureDirective(
  run: InteractionRunSnapshot,
  cameraReady: boolean,
  boundRunId: string | null,
  recorderActive: boolean,
  nowMs: number,
): CaptureDirective {
  if (isTerminalInteractionRun(run)) {
    return boundRunId === run.run_id ? { action: 'stop' } : { action: 'none' }
  }
  if (boundRunId === run.run_id) return { action: 'none' }
  if (boundRunId !== null || recorderActive) return { action: 'none' }
  if (!cameraReady) return { action: 'wait-for-camera' }
  const startAtMs = Date.parse(run.start_at_utc)
  if (!Number.isFinite(startAtMs)) return { action: 'none' }
  if (startAtMs > nowMs) return { action: 'schedule', delayMs: startAtMs - nowMs }
  return { action: 'start', lateByMs: nowMs - startAtMs }
}
