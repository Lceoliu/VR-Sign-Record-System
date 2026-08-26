import {
  Activity,
  Camera,
  CheckCircle2,
  Clock3,
  Database,
  Download,
  Headset,
  RefreshCw,
  ShieldAlert,
  UploadCloud,
  XCircle,
} from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api } from './api'
import { QuestPreviewPanel } from './components/QuestPreviewPanel'
import { VideoPanel } from './components/VideoPanel'
import {
  bindCameraPresenceRetirement,
  cameraReadinessForCurrentStream,
  captureDirective,
  createCameraReadinessHeartbeat,
  isTerminalInteractionRun,
  isValidParticipantId,
  nextPageSessionHeartbeatGeneration,
  participantIdsMatch,
  reconcilePolledInteractionRun,
  transitionCameraHeartbeatGate,
  transitionWebcamRecovery,
  webcamRecoveryFilename,
  type CameraHeartbeatGateAction,
  type CameraHeartbeatGateState,
  type WebcamRecoveryAction,
  type WebcamRecoveryState,
} from './interactionCapture'
import type {
  InteractionArtifactType,
  InteractionReadiness,
  InteractionRunSnapshot,
} from './types'
import './interaction-study.css'

type CaptureStatus =
  | 'idle'
  | 'waiting-camera'
  | 'scheduled'
  | 'recording'
  | 'stopping'
  | 'uploading'
  | 'uploaded'
  | 'missed'
  | 'error'

const ARTIFACT_LABELS: Record<InteractionArtifactType, string> = {
  events: '事件',
  poses: '参与者姿态',
  objects: '物体状态',
  summary: '运行摘要',
  webcam: '摄像头视频',
}

const CAPTURE_LABELS: Record<CaptureStatus, string> = {
  idle: '等待 Run Plan',
  'waiting-camera': '等待摄像头就绪',
  scheduled: '已排程',
  recording: '正在录制整轮视频',
  stopping: '正在结束录制',
  uploading: '正在上传 webcam.webm',
  uploaded: '摄像头视频已落盘',
  missed: '本机没有生成摄像头视频',
  error: '摄像头录制失败',
}

function preferredMimeType(): string {
  return ['video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm']
    .find((type) => MediaRecorder.isTypeSupported(type)) ?? ''
}

function runStateLabel(run: InteractionRunSnapshot | null): string {
  if (!run) return '等待 Quest 提交不可变 Run Plan'
  return {
    Scheduled: '已排程',
    Running: '运行中',
    Completed: '已完成',
    Aborted: '已中止',
  }[run.state]
}

function readinessTone(ready: boolean): 'ready' | 'blocked' {
  return ready ? 'ready' : 'blocked'
}

export default function InteractionStudyApp() {
  const [readiness, setReadiness] = useState<InteractionReadiness | null>(null)
  const [backendOnline, setBackendOnline] = useState(false)
  const [currentRun, setCurrentRun] = useState<InteractionRunSnapshot | null>(null)
  const [expectedParticipantId, setExpectedParticipantId] = useState(
    () => window.localStorage.getItem('signvr-interaction-participant') ?? '',
  )
  const [cameras, setCameras] = useState<MediaDeviceInfo[]>([])
  const [cameraId, setCameraId] = useState('')
  const [cameraReady, setCameraReady] = useState(false)
  const [cameraHeartbeatReady, setCameraHeartbeatReady] = useState(false)
  const [captureStatus, setCaptureStatus] = useState<CaptureStatus>('idle')
  const [captureDetail, setCaptureDetail] = useState('尚未收到本轮同步起点')
  const [abortReason, setAbortReason] = useState('')
  const [abortPending, setAbortPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [now, setNow] = useState(0)
  const [webcamRecoveries, setWebcamRecoveries] = useState<WebcamRecoveryState>(
    () => new Map(),
  )

  const videoRef = useRef<HTMLVideoElement>(null)
  const mediaStreamRef = useRef<MediaStream | null>(null)
  const cameraRequestRef = useRef(0)
  const participantIdRef = useRef(expectedParticipantId)
  const [heartbeatGeneration] = useState(() => nextPageSessionHeartbeatGeneration())
  const heartbeatSequenceRef = useRef(0)
  const cameraHeartbeatGateRef = useRef<CameraHeartbeatGateState>({
    ready: false,
    pendingGeneration: null,
    pendingSequence: null,
  })
  const recorderRef = useRef<MediaRecorder | null>(null)
  const chunksRef = useRef<Blob[]>([])
  const captureRunIdRef = useRef<string | null>(null)
  const startTimerRef = useRef<number | null>(null)
  const currentRunRef = useRef<InteractionRunSnapshot | null>(null)
  const refreshIssuedSequenceRef = useRef(0)
  const refreshAcceptedSequenceRef = useRef(0)
  const webcamRecoveryRef = useRef<WebcamRecoveryState>(new Map())

  const applyCurrentRun = useCallback((run: InteractionRunSnapshot | null) => {
    currentRunRef.current = run
    setCurrentRun(run)
  }, [])

  const updateWebcamRecovery = useCallback((action: WebcamRecoveryAction) => {
    const transition = transitionWebcamRecovery(webcamRecoveryRef.current, action)
    if (transition.state !== webcamRecoveryRef.current) {
      webcamRecoveryRef.current = transition.state
      setWebcamRecoveries(transition.state)
    }
    return transition.upload
  }, [])

  const applyCameraHeartbeatGate = useCallback((action: CameraHeartbeatGateAction) => {
    const next = transitionCameraHeartbeatGate(cameraHeartbeatGateRef.current, action)
    cameraHeartbeatGateRef.current = next
    setCameraHeartbeatReady(next.ready)
    return next
  }, [])

  const refreshReadiness = useCallback(async () => {
    const sequence = ++refreshIssuedSequenceRef.current
    try {
      const next = await api.interactionReadiness()
      const boundRunId = captureRunIdRef.current
      const boundRunSnapshot = boundRunId
        ? await api.interactionRun(boundRunId)
        : null
      if (boundRunId !== captureRunIdRef.current) return
      const reconciled = reconcilePolledInteractionRun(
        {
          acceptedSequence: refreshAcceptedSequenceRef.current,
          run: currentRunRef.current,
        },
        {
          sequence,
          readinessActiveRun: next.active_run,
          boundRunId,
          boundRunSnapshot,
        },
      )
      if (reconciled.acceptedSequence !== sequence) return
      refreshAcceptedSequenceRef.current = sequence
      setReadiness(next)
      if (
        !next.camera_ready
        || !next.participant_ready
        || next.participant_id !== participantIdRef.current
      ) {
        applyCameraHeartbeatGate({ type: 'invalidate' })
      }
      applyCurrentRun(reconciled.run)
      setBackendOnline(true)
    } catch (reason) {
      if (sequence <= refreshAcceptedSequenceRef.current) return
      refreshAcceptedSequenceRef.current = sequence
      setBackendOnline(false)
      setError(reason instanceof Error ? reason.message : 'Interaction Host 无法连接')
    }
  }, [applyCameraHeartbeatGate, applyCurrentRun])

  useEffect(() => {
    queueMicrotask(() => void refreshReadiness())
    const refreshTimer = window.setInterval(() => void refreshReadiness(), 2000)
    const clockTimer = window.setInterval(() => setNow(Date.now()), 100)
    return () => {
      window.clearInterval(refreshTimer)
      window.clearInterval(clockTimer)
    }
  }, [refreshReadiness])

  useEffect(() => {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
    const socket = new WebSocket(`${protocol}//${window.location.host}/ws/events`)
    socket.onmessage = (event) => {
      const message = JSON.parse(event.data) as { type?: string; payload?: unknown }
      if (!message.type?.startsWith('interaction_') || !message.payload) return
      const payload = message.payload as InteractionRunSnapshot
      if (payload.schema_version !== 1 || !payload.run_id) return
      refreshAcceptedSequenceRef.current = refreshIssuedSequenceRef.current
      applyCurrentRun(payload)
      setReadiness((previous) => previous
        ? {
            ...previous,
            active_run: isTerminalInteractionRun(payload) ? null : payload,
          }
        : previous)
      if (message.type === 'interaction_upload_acknowledged') {
        setCaptureDetail('Host 已确认全部工件耐久落盘；Quest 可以删除本地副本')
      }
    }
    return () => socket.close()
  }, [applyCurrentRun])

  const reportCameraReadiness = useCallback((observedStream?: MediaStream | null) => {
    const currentStream = mediaStreamRef.current
    const ready = cameraReadinessForCurrentStream(
      currentStream,
      observedStream === undefined ? currentStream : observedStream,
    )
    if (ready === null) return
    setCameraReady(ready)
    const heartbeat = createCameraReadinessHeartbeat(
      ready,
      participantIdRef.current,
      heartbeatGeneration,
      ++heartbeatSequenceRef.current,
    )
    applyCameraHeartbeatGate({ type: 'sent', heartbeat })
    void api.updateInteractionCameraReadiness(heartbeat).then((echo) => {
      if (
        heartbeat.heartbeat_generation !== heartbeatGeneration
        || heartbeat.heartbeat_sequence !== heartbeatSequenceRef.current
      ) return
      applyCameraHeartbeatGate({ type: 'echo', heartbeat, echo })
      setReadiness((previous) => previous
        ? {
            ...previous,
            camera_fresh: echo.camera_fresh,
            camera_ready: echo.camera_ready,
            camera_last_seen_utc: echo.camera_last_seen_utc,
            participant_fresh: echo.participant_fresh,
            participant_ready: echo.participant_ready,
            participant_id: echo.participant_id,
            participant_last_seen_utc: echo.participant_last_seen_utc,
            ready: previous.storage_ready
              && previous.quest_ready
              && echo.camera_ready
              && echo.participant_ready,
          }
        : previous)
    }).catch(() => {
      if (heartbeat.heartbeat_sequence === heartbeatSequenceRef.current) {
        applyCameraHeartbeatGate({ type: 'failed', heartbeat })
      }
    })
  }, [applyCameraHeartbeatGate, heartbeatGeneration])

  useEffect(() => bindCameraPresenceRetirement(
    window,
    () => {
      const heartbeat = createCameraReadinessHeartbeat(
        false,
        '',
        heartbeatGeneration,
        ++heartbeatSequenceRef.current,
      )
      applyCameraHeartbeatGate({ type: 'sent', heartbeat })
      return heartbeat
    },
    (heartbeat) => api.retireInteractionCameraReadiness(heartbeat),
  ), [applyCameraHeartbeatGate, heartbeatGeneration])

  const openCamera = useCallback(async (deviceId?: string) => {
    const requestId = ++cameraRequestRef.current
    const preferredDeviceId = deviceId
      || window.localStorage.getItem('signvr-interaction-camera')
      || undefined
    const previousStream = mediaStreamRef.current
    mediaStreamRef.current = null
    if (videoRef.current?.srcObject === previousStream) videoRef.current.srcObject = null
    previousStream?.getTracks().forEach((track) => track.stop())
    reportCameraReadiness(null)

    let stream: MediaStream
    try {
      stream = await navigator.mediaDevices.getUserMedia({
        audio: false,
        video: {
          deviceId: preferredDeviceId ? { exact: preferredDeviceId } : undefined,
          width: { ideal: 1280 },
          height: { ideal: 720 },
        },
      })
    } catch (reason) {
      if (requestId !== cameraRequestRef.current) return
      reportCameraReadiness(null)
      setCameraId('')
      try {
        const available = await navigator.mediaDevices.enumerateDevices()
        if (requestId !== cameraRequestRef.current) return
        setCameras(available.filter((device) => device.kind === 'videoinput'))
      } catch {
        setCameras([])
      }
      setError(`外置摄像头：${reason instanceof Error ? reason.message : '无法打开'}`)
      return
    }

    if (requestId !== cameraRequestRef.current) {
      stream.getTracks().forEach((track) => track.stop())
      return
    }

    mediaStreamRef.current = stream
    if (videoRef.current) videoRef.current.srcObject = stream
    const reportThisStream = () => reportCameraReadiness(stream)
    stream.addEventListener('inactive', reportThisStream)
    stream.getVideoTracks().forEach((track) => {
      track.addEventListener('ended', reportThisStream)
    })
    const resolvedId = stream.getVideoTracks()[0]?.getSettings().deviceId
    if (resolvedId) {
      setCameraId(resolvedId)
      window.localStorage.setItem('signvr-interaction-camera', resolvedId)
    }
    reportThisStream()
    setError(null)

    try {
      const available = await navigator.mediaDevices.enumerateDevices()
      if (requestId !== cameraRequestRef.current || mediaStreamRef.current !== stream) return
      setCameras(available.filter((device) => device.kind === 'videoinput'))
    } catch (reason) {
      if (requestId === cameraRequestRef.current && mediaStreamRef.current === stream) {
        setError(`摄像头列表：${reason instanceof Error ? reason.message : '无法读取'}`)
      }
    }
  }, [reportCameraReadiness])

  useEffect(() => {
    queueMicrotask(() => void openCamera())
    return () => {
      cameraRequestRef.current += 1
      if (startTimerRef.current !== null) window.clearTimeout(startTimerRef.current)
      if (recorderRef.current?.state === 'recording') recorderRef.current.stop()
      const stream = mediaStreamRef.current
      mediaStreamRef.current = null
      stream?.getTracks().forEach((track) => track.stop())
    }
  }, [openCamera])

  useEffect(() => {
    const report = () => reportCameraReadiness()
    report()
    const heartbeat = window.setInterval(report, 2000)
    return () => window.clearInterval(heartbeat)
  }, [reportCameraReadiness])

  const uploadWebcam = useCallback(async (runId: string, capturedBlob?: Blob) => {
    if (capturedBlob) {
      updateWebcamRecovery({ type: 'retain', runId, blob: capturedBlob })
    }
    const upload = updateWebcamRecovery({ type: 'start-upload', runId })
    if (!upload) return
    const { blob, attempt } = upload
    const updateCapturePanel = (status: CaptureStatus, detail: string) => {
      const displayedRunId = currentRunRef.current?.run_id
      if (displayedRunId !== undefined && displayedRunId !== runId) return
      setCaptureStatus(status)
      setCaptureDetail(detail)
    }
    const confirmHostHasWebcam = () => {
      updateWebcamRecovery({ type: 'host-confirmed', runId, blob, attempt })
    }

    if (blob.size === 0) {
      const message = 'MediaRecorder 返回了空文件；该 Run 的空 Blob 仍保留在当前页面内存中'
      updateWebcamRecovery({ type: 'upload-failed', runId, blob, attempt, error: message })
      updateCapturePanel('error', message)
      return
    }
    updateCapturePanel(
      'uploading',
      `${(blob.size / 1024 / 1024).toFixed(1)} MiB，正在原子上传`,
    )
    try {
      const result = await api.uploadInteractionArtifact(runId, 'webcam', blob)
      confirmHostHasWebcam()
      updateCapturePanel(
        'uploaded',
        result.acknowledged
          ? 'webcam.webm 已落盘，全部工件 ACK 完成'
          : 'webcam.webm 已落盘，仍等待 Quest 工件',
      )
      await refreshReadiness()
    } catch (reason) {
      try {
        const ack = await api.interactionAck(runId)
        if (!ack.missing_artifacts.includes('webcam')) {
          confirmHostHasWebcam()
          updateCapturePanel('uploaded', 'Host 已有 webcam.webm；本次重试没有覆盖原文件')
          await refreshReadiness()
          return
        }
      } catch {
        // Preserve the original upload error below; ACK was only a recovery check.
      }
      const message = reason instanceof Error ? reason.message : 'webcam.webm 上传失败'
      updateWebcamRecovery({ type: 'upload-failed', runId, blob, attempt, error: message })
      updateCapturePanel(
        'error',
        '上传失败；Blob 仍保留在当前页面内存中，请重试上传或立即保存到本地',
      )
      setError(`${runId} webcam 上传失败：${message}`)
    }
  }, [refreshReadiness, updateWebcamRecovery])

  const beginCapture = useCallback((run: InteractionRunSnapshot, lateByMs = 0) => {
    const stream = mediaStreamRef.current
    if (!stream || cameraReadinessForCurrentStream(stream, stream) !== true) {
      captureRunIdRef.current = null
      setCaptureStatus('error')
      setCaptureDetail('同步起点到达时摄像头不可用；没有伪造录制成功状态')
      return
    }
    chunksRef.current = []
    try {
      const mimeType = preferredMimeType()
      const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined)
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) chunksRef.current.push(event.data)
      }
      recorder.onerror = () => {
        setCaptureStatus('error')
        setCaptureDetail('浏览器报告 MediaRecorder 错误；请保留 Quest 本地工件')
      }
      recorder.onstop = () => {
        recorderRef.current = null
        captureRunIdRef.current = null
        const blob = new Blob(chunksRef.current, {
          type: recorder.mimeType || 'video/webm',
        })
        void uploadWebcam(run.run_id, blob)
      }
      recorderRef.current = recorder
      captureRunIdRef.current = run.run_id
      recorder.start(1000)
      setCaptureStatus('recording')
      setCaptureDetail(
        lateByMs > 250
          ? `摄像头晚于同步起点 ${(lateByMs / 1000).toFixed(1)} 秒开始；该偏差已明确保留`
          : 'video-only WebM 已按 start_at 开始，等待 Quest complete/abort',
      )
    } catch (reason) {
      recorderRef.current = null
      captureRunIdRef.current = null
      setCaptureStatus('error')
      setCaptureDetail('浏览器无法创建 video-only WebM MediaRecorder')
      setError(reason instanceof Error ? reason.message : 'MediaRecorder 初始化失败')
    }
  }, [uploadWebcam])

  const stopCapture = useCallback((runId: string) => {
    if (captureRunIdRef.current !== runId) return
    if (startTimerRef.current !== null) {
      window.clearTimeout(startTimerRef.current)
      startTimerRef.current = null
    }
    const recorder = recorderRef.current
    if (recorder?.state === 'recording') {
      setCaptureStatus('stopping')
      setCaptureDetail('收到 complete/abort，正在封装整轮 WebM')
      recorder.stop()
      return
    }
    captureRunIdRef.current = null
    recorderRef.current = null
    setCaptureStatus('missed')
    setCaptureDetail('Run 在摄像头起录前结束；webcam 保持为缺失工件')
  }, [])

  useEffect(() => {
    if (!currentRun) return
    const directive = captureDirective(
      currentRun,
      cameraReady,
      captureRunIdRef.current,
      recorderRef.current?.state === 'recording',
      Date.now(),
    )
    if (directive.action === 'wait-for-camera') {
      setCaptureStatus('waiting-camera')
      setCaptureDetail('Run 已存在，但摄像头尚未就绪')
      return
    }
    if (directive.action === 'schedule') {
      captureRunIdRef.current = currentRun.run_id
      setCaptureStatus('scheduled')
      setCaptureDetail(`将在 ${new Date(currentRun.start_at_utc).toLocaleTimeString()} 同步起录`)
      startTimerRef.current = window.setTimeout(() => {
        startTimerRef.current = null
        beginCapture(currentRun)
      }, directive.delayMs)
      return
    }
    if (directive.action === 'start') {
      captureRunIdRef.current = currentRun.run_id
      beginCapture(currentRun, directive.lateByMs)
      return
    }
    if (directive.action === 'stop') stopCapture(currentRun.run_id)
  }, [beginCapture, cameraReady, currentRun, stopCapture])

  const participantInputValid = isValidParticipantId(expectedParticipantId)
  const participantMatch = participantIdsMatch(
    expectedParticipantId,
    currentRun?.participant_id ?? null,
  )
  const participantHeartbeatMatch = participantIdsMatch(
    expectedParticipantId,
    readiness?.participant_id ?? null,
  )
  const participantHeartbeatReady = Boolean(
    participantInputValid
    && cameraHeartbeatReady
    && readiness?.participant_fresh
    && readiness?.participant_ready
    && participantHeartbeatMatch,
  )
  const captureBusy = ['scheduled', 'recording', 'stopping', 'uploading'].includes(captureStatus)
  const hostReady = Boolean(
    backendOnline
    && readiness?.ready
    && cameraReady
    && cameraHeartbeatReady
    && participantInputValid,
  )
  const startAtMs = currentRun ? Date.parse(currentRun.start_at_utc) : Number.NaN
  const startsIn = now > 0 && Number.isFinite(startAtMs) && currentRun?.state === 'Scheduled'
    ? Math.max(0, startAtMs - now)
    : null
  const canAbort = currentRun !== null && !isTerminalInteractionRun(currentRun)
  const selectedQuestId = readiness?.quest_device_id ?? null
  const webcamRecoveryEntries = useMemo(
    () => Array.from(webcamRecoveries.values()),
    [webcamRecoveries],
  )

  const readinessItems = useMemo(() => [
    {
      label: 'Backend',
      ready: backendOnline && Boolean(readiness?.storage_ready),
      detail: readiness?.storage_ready
        ? 'Interaction 存储可写'
        : readiness?.storage_error ?? '服务或存储不可用',
      icon: Database,
    },
    {
      label: 'Quest',
      ready: Boolean(readiness?.quest_ready),
      detail: selectedQuestId ?? '未收到 fresh Quest HTTP heartbeat',
      icon: Headset,
    },
    {
      label: 'Camera',
      ready: cameraReady && cameraHeartbeatReady,
      detail: !cameraReady
        ? '浏览器未获得摄像头'
        : cameraHeartbeatReady ? 'Host 已接受 fresh camera heartbeat' : '等待 Host 回显',
      icon: Camera,
    },
    {
      label: 'Participant',
      ready: participantHeartbeatReady && (!currentRun || participantMatch),
      detail: currentRun
        ? participantMatch ? `已核对 ${currentRun.participant_id}` : '输入值与 Run Plan 不一致'
        : !participantInputValid
          ? '请输入合法匿名编号'
          : participantHeartbeatReady ? `Host 已接受 ${expectedParticipantId}` : '等待 Host 回显',
      icon: Activity,
    },
  ], [
    backendOnline,
    cameraHeartbeatReady,
    cameraReady,
    currentRun,
    expectedParticipantId,
    participantInputValid,
    participantHeartbeatReady,
    participantMatch,
    readiness?.quest_ready,
    readiness?.storage_error,
    readiness?.storage_ready,
    selectedQuestId,
  ])

  const abortCurrentRun = async () => {
    if (!currentRun || !canAbort) return
    const reason = abortReason.trim()
    if (!reason) {
      setError('中止整轮前必须填写原因；原因会与部分数据一起保留')
      return
    }
    setAbortPending(true)
    try {
      const snapshot = await api.abortInteractionRun(currentRun.run_id, reason)
      refreshAcceptedSequenceRef.current = refreshIssuedSequenceRef.current
      applyCurrentRun(snapshot)
      setAbortReason('')
      setError(null)
    } catch (reasonValue) {
      setError(reasonValue instanceof Error ? reasonValue.message : '无法中止 Interaction Run')
    } finally {
      setAbortPending(false)
    }
  }

  const saveWebcamLocally = useCallback((runId: string, blob: Blob) => {
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = webcamRecoveryFilename(runId)
    document.body.appendChild(link)
    link.click()
    link.remove()
    window.setTimeout(() => URL.revokeObjectURL(url), 1000)
  }, [])

  return (
    <div className="interaction-shell">
      <header className="interaction-topbar">
        <div className="interaction-brand">
          <Headset size={25} />
          <div><h1>SignVR Interaction Study Host</h1><span>Contract V1 · Host 不生成实验变量</span></div>
        </div>
        <div className="interaction-topmeta">
          <span className={hostReady ? 'host-ready' : 'host-blocked'}>
            <i />{hostReady ? '主机采集就绪' : '主机尚未全部就绪'}
          </span>
          <a href="/">返回 Recording Take</a>
        </div>
      </header>

      <main className="interaction-main">
        <section className="interaction-hero">
          <div>
            <span className="interaction-kicker">当前 Interaction Run</span>
            <h2>{currentRun?.run_id ?? '尚未收到 Run Plan'}</h2>
            <p>
              {currentRun
                ? `${currentRun.batch_id} / ${currentRun.participant_id}`
                : '请先核对匿名参与者编号；condition、密码、句子与 Take 均由 Quest 冻结。'}
            </p>
          </div>
          <div className={`run-state state-${currentRun?.state.toLowerCase() ?? 'waiting'}`}>
            <Clock3 size={20} />
            <span>{runStateLabel(currentRun)}</span>
            {startsIn !== null && <strong>{(startsIn / 1000).toFixed(1)} s</strong>}
          </div>
        </section>

        <section className="readiness-grid" aria-label="Interaction readiness">
          {readinessItems.map(({ label, ready, detail, icon: Icon }) => (
            <article key={label} className={`readiness-card ${readinessTone(ready)}`}>
              <Icon size={20} />
              <div><span>{label}</span><strong>{ready ? 'READY' : 'NOT READY'}</strong><small>{detail}</small></div>
              {ready ? <CheckCircle2 size={19} /> : <XCircle size={19} />}
            </article>
          ))}
        </section>

        <section className="interaction-controls">
          <label className="participant-control">
            <span>匿名参与者编号（通过 Host heartbeat 核对，不写入 Run Plan）</span>
            <input
              value={expectedParticipantId}
              placeholder="例如 P001"
              disabled={captureBusy}
              onChange={(event) => {
                const value = event.target.value
                participantIdRef.current = value
                setExpectedParticipantId(value)
                applyCameraHeartbeatGate({ type: 'invalidate' })
                window.localStorage.setItem('signvr-interaction-participant', value)
                reportCameraReadiness()
              }}
            />
          </label>
          <label className="study-camera-control">
            <span>摄像头</span>
            <select
              value={cameraId}
              disabled={captureBusy}
              onChange={(event) => void openCamera(event.target.value)}
            >
              {!cameraId && (
                <option value="" disabled>
                  {cameras.length > 0 ? '请选择摄像头' : '未检测到摄像头'}
                </option>
              )}
              {cameras.map((camera, index) => (
                <option value={camera.deviceId} key={camera.deviceId}>
                  {camera.label || `Camera ${index + 1}`}
                </option>
              ))}
            </select>
          </label>
          <button className="study-refresh" onClick={() => void refreshReadiness()}>
            <RefreshCw size={16} />刷新 readiness
          </button>
        </section>

        <section className="interaction-capture-grid">
          <VideoPanel
            kind="camera"
            title="Webcam Capture"
            meta="1280 × 720 · video-only WebM"
            videoRef={videoRef}
            active={captureStatus === 'recording' || captureStatus === 'stopping'}
          />
          <QuestPreviewPanel
            deviceId={selectedQuestId}
            active={currentRun?.state === 'Running'}
          />
        </section>

        <section className="study-status-grid">
          <article className={`capture-status capture-${captureStatus}`}>
            <header><UploadCloud size={20} /><span>Webcam 状态</span></header>
            <strong>{CAPTURE_LABELS[captureStatus]}</strong>
            <p>{captureDetail}</p>
          </article>
          <article className="artifact-status">
            <header><Database size={20} /><span>Host ACK / 缺失工件</span></header>
            {currentRun?.acknowledged ? (
              <div className="ack-complete"><CheckCircle2 size={20} />全部工件已耐久落盘</div>
            ) : (
              <div className="missing-artifacts">
                {(currentRun?.missing_artifacts ?? ['events', 'poses', 'objects', 'summary', 'webcam'])
                  .map((artifact) => <span key={artifact}>{ARTIFACT_LABELS[artifact]}</span>)}
              </div>
            )}
            <p>{readiness?.interaction_root ?? 'data/interaction-tests'}</p>
          </article>
        </section>

        {webcamRecoveryEntries.length > 0 && (
          <section className="webcam-recovery" aria-live="polite">
            <header>
              <ShieldAlert size={20} />
              <div>
                <strong>待恢复 Webcam（仅当前页面内存）</strong>
                <p>刷新、关闭或离开 Study 页面会永久丢失这些内存 Blob；请重试上传或立即保存到本地。</p>
              </div>
            </header>
            <div className="webcam-recovery-list">
              {webcamRecoveryEntries.map((entry) => (
                <article className="webcam-recovery-item" key={entry.runId}>
                  <div>
                    <code>{entry.runId}</code>
                    <span>
                      {(entry.blob.size / 1024 / 1024).toFixed(1)} MiB · {
                        entry.status === 'uploading' ? '正在上传' : '等待恢复'
                      }
                    </span>
                    {entry.error && <small>{entry.error}</small>}
                  </div>
                  <div className="webcam-recovery-actions">
                    <button
                      type="button"
                      disabled={entry.status === 'uploading'}
                      onClick={() => void uploadWebcam(entry.runId)}
                    >
                      <RefreshCw size={15} />
                      {entry.status === 'uploading' ? '正在上传…' : '重试上传'}
                    </button>
                    <button
                      type="button"
                      onClick={() => saveWebcamLocally(entry.runId, entry.blob)}
                    >
                      <Download size={15} />保存到本地
                    </button>
                  </div>
                </article>
              ))}
            </div>
          </section>
        )}

        <section className="abort-panel">
          <div><ShieldAlert size={20} /><span>整轮安全中止</span></div>
          <input
            value={abortReason}
            disabled={!canAbort || abortPending}
            placeholder="必填：安全、身体不适、追踪故障等原因"
            onChange={(event) => setAbortReason(event.target.value)}
          />
          <button disabled={!canAbort || abortPending} onClick={() => void abortCurrentRun()}>
            {abortPending ? '正在中止…' : 'Abort Run'}
          </button>
        </section>

        {currentRun?.state === 'Aborted' && currentRun.abort_reason && (
          <div className="study-notice"><ShieldAlert size={17} />中止原因：{currentRun.abort_reason}</div>
        )}
        {error && (
          <div className="study-error"><XCircle size={17} /><span>{error}</span><button onClick={() => setError(null)}>关闭</button></div>
        )}
      </main>
    </div>
  )
}
