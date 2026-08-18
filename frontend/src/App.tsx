import {
  Activity,
  BellRing,
  Camera,
  Check,
  ChevronRight,
  Circle,
  Eye,
  EyeOff,
  FileJson,
  FileText,
  Headset,
  Import,
  Play,
  RefreshCw,
  RotateCcw,
  Square,
  UploadCloud,
} from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api } from './api'
import { SkeletonPanel } from './components/SkeletonPanel'
import { StatusStrip } from './components/StatusStrip'
import { VideoPanel } from './components/VideoPanel'
import type { DeviceInfo, HostState, RecordingStatus } from './types'

const HOLD_DURATION_MS = 1200

function formatTimer(status: RecordingStatus, startedAt: number | null, now: number): string {
  if (!startedAt) return '00:00.0'
  const delta = status === 'countdown' ? Math.max(0, startedAt - now) : Math.max(0, now - startedAt)
  const minutes = Math.floor(delta / 60000)
  const seconds = Math.floor((delta % 60000) / 1000)
  const tenths = Math.floor((delta % 1000) / 100)
  return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}.${tenths}`
}

function preferredMimeType(): string {
  return ['video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm'].find((type) => MediaRecorder.isTypeSupported(type)) ?? ''
}

export default function App() {
  const [state, setState] = useState<HostState | null>(null)
  const [devices, setDevices] = useState<DeviceInfo[]>([])
  const [cameras, setCameras] = useState<MediaDeviceInfo[]>([])
  const [cameraId, setCameraId] = useState('')
  const [cameraReady, setCameraReady] = useState(false)
  const [now, setNow] = useState(0)
  const [holdProgress, setHoldProgress] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)

  const videoRef = useRef<HTMLVideoElement>(null)
  const mediaStreamRef = useRef<MediaStream | null>(null)
  const recorderRef = useRef<MediaRecorder | null>(null)
  const chunksRef = useRef<Blob[]>([])
  const cameraStartTimerRef = useRef<number | null>(null)
  const activeTakeRef = useRef<{ takeId: string; sessionId: string; sentenceId: string } | null>(null)
  const keyDownAtRef = useRef<number | null>(null)
  const holdTimerRef = useRef<number | null>(null)
  const longPressTriggeredRef = useRef(false)

  const selectedDevice = useMemo(
    () => devices.find((device) => device.device_id === state?.selected_device_id) ?? devices.find((device) => device.selected),
    [devices, state?.selected_device_id],
  )
  const selectedDeviceId = selectedDevice?.device_id ?? null
  const currentSentence = state?.sentences[state.current_sentence_index]
  const isRecording = state?.recording_status === 'recording' || state?.recording_status === 'countdown'

  const refresh = useCallback(async () => {
    const [nextState, nextDevices] = await Promise.all([api.state(), api.devices()])
    setState(nextState)
    setDevices(nextDevices)
  }, [])

  useEffect(() => {
    queueMicrotask(() => refresh().catch((reason: Error) => setError(reason.message)))
    const timer = window.setInterval(() => setNow(Date.now()), 100)
    return () => window.clearInterval(timer)
  }, [refresh])

  useEffect(() => {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
    const socket = new WebSocket(`${protocol}//${window.location.host}/ws/events`)
    socket.onmessage = (event) => {
      const message = JSON.parse(event.data) as { type: string; payload: HostState }
      if (['state_changed', 'take_uploaded', 'camera_uploaded'].includes(message.type)) {
        setState(message.payload)
      }
      if (['device_updated', 'device_selected', 'command_ack'].includes(message.type)) {
        api.devices().then(setDevices).catch(() => undefined)
      }
    }
    return () => socket.close()
  }, [])

  const openCamera = useCallback(async (deviceId?: string) => {
    mediaStreamRef.current?.getTracks().forEach((track) => track.stop())
    const stream = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: {
        deviceId: deviceId ? { exact: deviceId } : undefined,
        width: { ideal: 1280 },
        height: { ideal: 720 },
      },
    })
    mediaStreamRef.current = stream
    if (videoRef.current) videoRef.current.srcObject = stream
    const available = await navigator.mediaDevices.enumerateDevices()
    const videoDevices = available.filter((device) => device.kind === 'videoinput')
    setCameras(videoDevices)
    const selectedTrack = stream.getVideoTracks()[0]
    const resolvedId = selectedTrack.getSettings().deviceId
    if (resolvedId) setCameraId(resolvedId)
    setCameraReady(true)
  }, [])

  useEffect(() => {
    queueMicrotask(() => openCamera().catch((reason: Error) => {
      setCameraReady(false)
      setError(`外置相机：${reason.message}`)
    }))
    return () => mediaStreamRef.current?.getTracks().forEach((track) => track.stop())
  }, [openCamera])

  const uploadCameraTake = useCallback(async (blob: Blob) => {
    const context = activeTakeRef.current
    if (!context || blob.size === 0) return
    setUploading(true)
    try {
      await api.uploadCamera(context.takeId, context.sessionId, context.sentenceId, blob)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '相机视频上传失败')
    } finally {
      setUploading(false)
      activeTakeRef.current = null
    }
  }, [refresh])

  const beginCameraRecording = useCallback((responseState: HostState, startAt: number) => {
    const stream = mediaStreamRef.current
    const take = responseState.current_take
    const sentence = responseState.sentences[responseState.current_sentence_index]
    if (!stream || !take || !sentence) return
    activeTakeRef.current = {
      takeId: take.take_id,
      sessionId: responseState.session_id,
      sentenceId: sentence.sentence_id,
    }
    chunksRef.current = []
    const mimeType = preferredMimeType()
    const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined)
    recorder.ondataavailable = (event) => {
      if (event.data.size > 0) chunksRef.current.push(event.data)
    }
    recorder.onstop = () => {
      const blob = new Blob(chunksRef.current, { type: recorder.mimeType || 'video/webm' })
      void uploadCameraTake(blob)
    }
    recorderRef.current = recorder
    cameraStartTimerRef.current = window.setTimeout(() => recorder.start(1000), Math.max(0, startAt - Date.now()))
  }, [uploadCameraTake])

  const stopCameraRecording = useCallback(() => {
    if (cameraStartTimerRef.current !== null) {
      window.clearTimeout(cameraStartTimerRef.current)
      cameraStartTimerRef.current = null
    }
    if (recorderRef.current?.state === 'recording') recorderRef.current.stop()
  }, [])

  const startRecording = useCallback(async () => {
    setError(null)
    try {
      const response = await api.start()
      setState(response.state)
      if (response.start_at_unix_ms) beginCameraRecording(response.state, response.start_at_unix_ms)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法开始录制')
    }
  }, [beginCameraRecording])

  const stopRecording = useCallback(async () => {
    setError(null)
    stopCameraRecording()
    try {
      const response = await api.stop()
      setState(response.state)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法结束录制')
    }
  }, [stopCameraRecording])

  const resetRecording = useCallback(async () => {
    setError(null)
    stopCameraRecording()
    try {
      const response = await api.reset()
      setState(response.state)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法重新录制')
    }
  }, [stopCameraRecording])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.code !== 'Space' || event.repeat || ['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON'].includes((event.target as HTMLElement).tagName)) return
      event.preventDefault()
      keyDownAtRef.current = performance.now()
      longPressTriggeredRef.current = false
      // The teacher cannot hear the pedal, so every press is echoed into the
      // headset immediately, before any state change is decided.
      api.pedal('down')
      holdTimerRef.current = window.setInterval(() => {
        const elapsed = performance.now() - (keyDownAtRef.current ?? performance.now())
        const progress = Math.min(1, elapsed / HOLD_DURATION_MS)
        setHoldProgress(progress)
        // Throttled to ~10 Hz: enough for a smooth ring, light enough for UDP.
        if (Math.floor(elapsed / 100) !== Math.floor((elapsed - 30) / 100)) {
          api.pedal('hold', progress)
        }
        if (progress >= 1 && !longPressTriggeredRef.current) {
          longPressTriggeredRef.current = true
          void resetRecording()
        }
      }, 30)
    }
    const onKeyUp = (event: KeyboardEvent) => {
      if (event.code !== 'Space' || keyDownAtRef.current === null) return
      event.preventDefault()
      if (holdTimerRef.current !== null) window.clearInterval(holdTimerRef.current)
      api.pedal('up')
      if (!longPressTriggeredRef.current) {
        if (isRecording) void stopRecording()
        else void startRecording()
      }
      keyDownAtRef.current = null
      holdTimerRef.current = null
      setHoldProgress(0)
    }
    window.addEventListener('keydown', onKeyDown)
    window.addEventListener('keyup', onKeyUp)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('keyup', onKeyUp)
      if (holdTimerRef.current !== null) window.clearInterval(holdTimerRef.current)
    }
  }, [isRecording, resetRecording, startRecording, stopRecording])

  const importSentenceFile = async (file: File) => {
    const text = await file.text()
    const values = text.split(/\r?\n/).map((line) => line.trim()).filter(Boolean)
    try {
      setState(await api.importSentences(values))
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '句子导入失败')
    }
  }

  const selectQuest = async (deviceId: string) => {
    try {
      await api.selectDevice(deviceId)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Quest 连接失败')
    }
  }

  if (!state) {
    return (
      <main className="loading-screen">
        <Headset size={34} />
        <span>正在连接本地服务…</span>
        {error && <strong>{error}</strong>}
      </main>
    )
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand"><Headset size={25} /><h1>VR 手语录制台</h1></div>
        <div className="topbar-meta">
          <span className="online"><i />本地服务在线</span>
          <span>会话 {state.session_id}</span>
          <strong>第 {state.current_sentence_index + 1} / {state.sentences.length} 句</strong>
        </div>
      </header>

      <div className="workspace">
        <aside className="left-rail">
          <div className="rail-title"><h2>设备</h2><span>{devices.length + (cameraReady ? 1 : 0)} 台在线</span></div>
          <section className="device-section">
            <div className="section-heading"><h3>Quest 设备</h3><button className="text-button" onClick={() => api.scan().catch((reason: Error) => setError(reason.message))}><RefreshCw size={14} />重新扫描</button></div>
            <div className="device-list">
              {devices.length === 0 && <div className="empty-row"><Headset size={20} /><span>等待 Quest 广播</span></div>}
              {devices.map((device) => (
                <button key={device.device_id} className={`device-row ${device.device_id === selectedDevice?.device_id ? 'selected' : ''}`} onClick={() => void selectQuest(device.device_id)}>
                  <Headset size={21} />
                  <span><strong>{device.name}</strong><small>{device.ip} · {device.state === 'recording' ? '录制中' : device.paired ? '已连接' : '可用'}</small></span>
                  {device.device_id === selectedDevice?.device_id ? <Check size={18} /> : <ChevronRight size={17} />}
                </button>
              ))}
            </div>
          </section>

          <section className="device-section camera-selector">
            <div className="section-heading"><h3>外置相机</h3></div>
            <label>
              <Camera size={19} />
              <select value={cameraId} onChange={(event) => { setCameraId(event.target.value); void openCamera(event.target.value) }}>
                {cameras.length === 0 && <option value="">未检测到相机</option>}
                {cameras.map((camera, index) => <option key={camera.deviceId} value={camera.deviceId}>{camera.label || `USB Camera ${index + 1}`}</option>)}
              </select>
            </label>
          </section>

          <label className="import-control">
            <Import size={18} />
            <span>导入句子文本</span>
            <input type="file" accept=".txt,text/plain" onChange={(event) => { const file = event.target.files?.[0]; if (file) void importSentenceFile(file) }} />
          </label>

          <section className="device-section">
            <div className="section-heading"><h3>追踪边界提示</h3></div>
            <button
              className={`guidance-toggle ${state.guidance_enabled ? 'on' : 'off'}`}
              onClick={() => void api.setGuidance(!state.guidance_enabled).catch((reason: Error) => setError(reason.message))}
            >
              {state.guidance_enabled ? <Eye size={17} /> : <EyeOff size={17} />}
              <span>{state.guidance_enabled ? '提示已开启' : '提示已关闭'}</span>
            </button>
            <p className="guidance-hint">对照录制时关闭，实验组录制时开启；两种情况都会记录追踪质量。</p>
          </section>
        </aside>

        <main className="main-stage">
          <section className="prompt-block">
            <div className="section-heading"><h2>当前句子</h2><span>#{String(state.current_sentence_index + 1).padStart(3, '0')}</span></div>
            <p>{currentSentence?.text}</p>
          </section>
          <div className="video-grid">
            <VideoPanel kind="camera" title="外置相机" meta={cameraReady ? '1280 × 720' : '未就绪'} videoRef={videoRef} active={state.recording_status === 'recording'} />
            <SkeletonPanel title="Quest 实时动作" deviceId={selectedDeviceId} active={state.recording_status === 'recording'} />
          </div>
          {state.help_requested && (
            <div className="help-banner" role="alert">
              <BellRing size={20} />
              <span>老师在头显内呼叫帮助</span>
              <button onClick={() => void api.acknowledgeHelp().catch(() => undefined)}>我已处理</button>
            </div>
          )}
          <StatusStrip status={state.recording_status} displayTime={formatTimer(state.recording_status, state.started_at_unix_ms, now)} />
          {error && <div className="error-banner" role="alert">{error}<button onClick={() => setError(null)}>关闭</button></div>}
        </main>

        <aside className="right-rail">
          <section>
            <div className="section-heading"><h2>句子队列</h2><span>{state.sentences.length} 句</span></div>
            <ol className="sentence-list">
              {state.sentences.map((sentence) => (
                <li key={sentence.sentence_id} className={sentence.status}>
                  <span className="sentence-index">{String(sentence.index + 1).padStart(3, '0')}</span>
                  <p>{sentence.text}</p>
                  {sentence.status === 'completed' ? <Check size={17} /> : sentence.status === 'current' ? <Circle size={10} fill="currentColor" /> : null}
                </li>
              ))}
            </ol>
          </section>
          <section className="take-section">
            <div className="section-heading"><h2>当前句 Take</h2><span>{uploading ? '上传中' : `${state.takes.length} 个`}</span></div>
            <div className="take-list">
              {state.takes.length === 0 && <div className="empty-take"><UploadCloud size={23} /><span>录制后在这里查看文件</span></div>}
              {state.takes.map((take) => (
                <article key={take.take_id}>
                  <header><strong>Take {String(take.take_index).padStart(3, '0')}</strong><span>{take.status === 'recording' ? '录制中' : '候选'}</span></header>
                  <div><FileText size={15} /><span>pose.jsonl</span><i className={take.pose_file ? 'received' : ''}>{take.pose_file ? '已接收' : '等待'}</i></div>
                  <div><FileJson size={15} /><span>meta.json</span><i className={take.meta_file ? 'received' : ''}>{take.meta_file ? '已接收' : '等待'}</i></div>
                  <div><Camera size={15} /><span>camera.webm</span><i className={take.video_file ? 'received' : ''}>{take.video_file ? '已接收' : '等待'}</i></div>
                  {take.quality && (
                    <div className={`take-quality ${take.quality.clean_ratio >= 0.9 ? 'good' : take.quality.clean_ratio >= 0.75 ? 'fair' : 'poor'}`}>
                      <Activity size={15} />
                      <span>追踪质量</span>
                      <i>{Math.round(take.quality.clean_ratio * 100)}%{take.quality.guidance_enabled ? '' : ' · 无提示'}</i>
                    </div>
                  )}
                </article>
              ))}
            </div>
          </section>
        </aside>
      </div>

      <footer className="action-bar">
        <div className="actions">
          <button className="action-start" disabled={state.recording_status !== 'ready'} onClick={() => void startRecording()}><Play size={20} fill="currentColor" />开始录制</button>
          <button className="action-stop" disabled={!isRecording} onClick={() => void stopRecording()}><Square size={18} fill="currentColor" />结束录制</button>
          <button className="action-reset" onClick={() => void resetRecording()}><RotateCcw size={20} />重新录制</button>
        </div>
        <div className="pedal-hint">
          <div className="keycap">SPACE</div>
          <span>短按开始 / 结束，长按重新录制</span>
          {holdProgress > 0 && <div className="hold-ring" style={{ '--progress': `${holdProgress * 360}deg` } as React.CSSProperties}><i /></div>}
        </div>
      </footer>
    </div>
  )
}
