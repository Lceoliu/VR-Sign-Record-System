import type {
  CommandResponse,
  DeviceInfo,
  HostState,
  RecordingBatchesResponse,
  RecordingRoundsResponse,
} from './types'

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init)
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as { detail?: string } | null
    throw new Error(body?.detail ?? `${response.status} ${response.statusText}`)
  }
  return response.json() as Promise<T>
}

export const api = {
  state: () => request<HostState>('/api/state'),
  devices: () => request<DeviceInfo[]>('/api/devices'),
  recordingBatches: () => request<RecordingBatchesResponse>('/api/recording/batches'),
  recordingRounds: (batchId: string) =>
    request<RecordingRoundsResponse>(`/api/recording/batches/${encodeURIComponent(batchId)}/rounds`),
  createRound: (batchId: string, roundId: string) =>
    request<HostState>(`/api/recording/batches/${encodeURIComponent(batchId)}/rounds`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ round_id: roundId }),
    }),
  selectRound: (batchId: string, roundId: string) =>
    request<HostState>(
      `/api/recording/batches/${encodeURIComponent(batchId)}/rounds/${encodeURIComponent(roundId)}/select`,
      { method: 'POST' },
    ),
  selectSentence: (sentenceIndex: number) =>
    request<HostState>('/api/recording/current-sentence', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sentence_index: sentenceIndex }),
    }),
  scan: () => request<{ status: string }>('/api/devices/scan', { method: 'POST' }),
  selectDevice: (deviceId: string) =>
    request(`/api/devices/${encodeURIComponent(deviceId)}/select`, { method: 'POST' }),
  // Fire-and-forget: a dropped pedal receipt must never block the pedal itself.
  pedal: (phase: 'down' | 'hold' | 'up', progress = 0) => {
    void fetch(`/api/pedal/${phase}?progress=${progress.toFixed(3)}`, { method: 'POST' }).catch(
      () => undefined,
    )
  },
  setGuidance: (enabled: boolean) =>
    request<{ guidance_enabled: boolean }>(`/api/guidance/${enabled}`, { method: 'POST' }),
  acknowledgeHelp: () => request<{ help_requested: boolean }>('/api/help/true', { method: 'POST' }),
  start: (batchId: string, roundId: string) =>
    request<CommandResponse>('/api/recording/start', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ batch_id: batchId, round_id: roundId }),
    }),
  stop: () => request<CommandResponse>('/api/recording/stop', { method: 'POST' }),
  reset: () => request<CommandResponse>('/api/recording/reset', { method: 'POST' }),
  uploadCamera: async (
    takeId: string,
    sessionId: string,
    sentenceId: string,
    blob: Blob,
  ) => {
    const body = new FormData()
    body.append('session_id', sessionId)
    body.append('sentence_id', sentenceId)
    body.append('video_file', blob, `${takeId}.camera.webm`)
    return request<{ video_file: string }>(`/api/takes/${encodeURIComponent(takeId)}/camera-upload`, {
      method: 'POST',
      body,
    })
  },
}
