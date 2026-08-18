import type { CommandResponse, DeviceInfo, HostState } from './types'

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
  scan: () => request<{ status: string }>('/api/devices/scan', { method: 'POST' }),
  selectDevice: (deviceId: string) =>
    request(`/api/devices/${encodeURIComponent(deviceId)}/select`, { method: 'POST' }),
  importSentences: (sentences: string[]) =>
    request<HostState>('/api/sentences', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sentences }),
    }),
  // Fire-and-forget: a dropped pedal receipt must never block the pedal itself.
  pedal: (phase: 'down' | 'hold' | 'up', progress = 0) => {
    void fetch(`/api/pedal/${phase}?progress=${progress.toFixed(3)}`, { method: 'POST' }).catch(
      () => undefined,
    )
  },
  setGuidance: (enabled: boolean) =>
    request<{ guidance_enabled: boolean }>(`/api/guidance/${enabled}`, { method: 'POST' }),
  acknowledgeHelp: () => request<{ help_requested: boolean }>('/api/help/true', { method: 'POST' }),
  start: () => request<CommandResponse>('/api/recording/start', { method: 'POST' }),
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

