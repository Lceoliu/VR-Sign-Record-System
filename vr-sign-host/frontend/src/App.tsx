import {
  Activity,
  ArrowLeft,
  ArrowRight,
  BellRing,
  Camera,
  Check,
  ChevronRight,
  Circle,
  Eye,
  EyeOff,
  FileJson,
  FileText,
  FolderOpen,
  Headset,
  ListFilter,
  Play,
  Plus,
  Pencil,
  RefreshCw,
  RotateCcw,
  Search,
  SkipForward,
  Square,
  UploadCloud,
} from 'lucide-react'
import { useCallback, useDeferredValue, useEffect, useMemo, useRef, useState } from 'react'
import { api } from './api'
import {
  appendCaptureChunk,
  createCaptureId,
  listPendingCaptures,
  loadCaptureBlob,
  removeCapture,
  saveCaptureContext,
  type CaptureContext,
} from './captureStore'
import { QuestPreviewPanel } from './components/QuestPreviewPanel'
import { StatusStrip } from './components/StatusStrip'
import { VideoPanel } from './components/VideoPanel'
import { connectReconnectingWebSocket } from './reconnectingWebSocket'
import type { DeviceInfo, HostState, RecordingStatus, RoundInfo } from './types'

const HOLD_DURATION_MS = 1200
const CATEGORY_LABELS: Record<string, string> = {
  social: '社交',
  collaborate: '协作',
  spatial: '空间',
  question: '问答',
  stress: '辨析',
  temporary: '临时',
}

function isEditableTarget(target: EventTarget | null): boolean {
  return target instanceof HTMLElement
    && (target.isContentEditable || ['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON'].includes(target.tagName))
}

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

interface ActiveCameraCapture {
  context: CaptureContext
  recorder: MediaRecorder
  chunks: Blob[]
  chunkSequence: number
  persistence: Promise<void>
  finalized: Promise<void>
  resolveFinalized: () => void
}

export default function App() {
  const [state, setState] = useState<HostState | null>(null)
  const [devices, setDevices] = useState<DeviceInfo[]>([])
  const [recordingBatches, setRecordingBatches] = useState<string[]>([])
  const [recordingRounds, setRecordingRounds] = useState<RoundInfo[]>([])
  const [recordingRoot, setRecordingRoot] = useState('data/recordings')
  const [batchId, setBatchId] = useState('')
  const [openedBatchId, setOpenedBatchId] = useState('')
  const [sentenceFilter, setSentenceFilter] = useState<'all' | 'pending' | 'completed'>('all')
  const [sentenceQuery, setSentenceQuery] = useState('')
  const [jumpValue, setJumpValue] = useState('1')
  const [cameras, setCameras] = useState<MediaDeviceInfo[]>([])
  const [cameraId, setCameraId] = useState('')
  const [cameraReady, setCameraReady] = useState(false)
  const [now, setNow] = useState(0)
  const [holdProgress, setHoldProgress] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const [recoveringCapture, setRecoveringCapture] = useState(false)
  const [captureActive, setCaptureActive] = useState(false)
  const [sentenceEditor, setSentenceEditor] = useState<{
    mode: 'edit' | 'add'
    index: number
    text: string
  } | null>(null)

  const videoRef = useRef<HTMLVideoElement>(null)
  const mediaStreamRef = useRef<MediaStream | null>(null)
  const recorderRef = useRef<MediaRecorder | null>(null)
  const cameraStartTimerRef = useRef<number | null>(null)
  const activeCaptureRef = useRef<ActiveCameraCapture | null>(null)
  const stopCameraRecordingRef = useRef<() => Promise<void>>(async () => undefined)
  const recoveryStartedRef = useRef(false)
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
  const currentBatchId = state?.batch_id ?? null
  const currentRoundId = state?.round_id ?? null
  const activeRoundId = currentBatchId === openedBatchId ? currentRoundId : null
  const contextReady = Boolean(openedBatchId && activeRoundId)
  const missingStandardRoundId = !recordingRounds.some((round) => round.round_id === 'round_001')
    ? 'round_001'
    : !recordingRounds.some((round) => round.round_id === 'round_002')
      ? 'round_002'
      : null
  const captureBusy = uploading || recoveringCapture || captureActive
  const captureReady = contextReady && Boolean(selectedDeviceId) && cameraReady && !captureBusy
  const startBlockedReason = !contextReady
    ? '请先打开录制批次并选择轮次'
    : !selectedDeviceId
      ? '请先连接 Quest'
      : !cameraReady
        ? '请先连接外置相机'
        : recoveringCapture
          ? '正在恢复上一次未完成的相机视频'
          : uploading
            ? '上一条相机视频仍在上传'
            : captureActive
              ? '外置相机正在录制'
              : undefined
  const completedCount = state?.sentences.filter((sentence) => sentence.completed).length ?? 0
  const deferredSentenceQuery = useDeferredValue(sentenceQuery.trim().toLocaleLowerCase())
  const visibleSentences = useMemo(() => {
    if (!state) return []
    return state.sentences.filter((sentence) => {
      if (sentenceFilter === 'pending' && sentence.completed) return false
      if (sentenceFilter === 'completed' && !sentence.completed) return false
      if (!deferredSentenceQuery) return true
      const number = String(sentence.index + 1).padStart(3, '0')
      return sentence.text.toLocaleLowerCase().includes(deferredSentenceQuery)
        || number.includes(deferredSentenceQuery)
        || (CATEGORY_LABELS[sentence.category] ?? sentence.category).includes(deferredSentenceQuery)
    })
  }, [deferredSentenceQuery, sentenceFilter, state])

  const commitState = useCallback((nextState: HostState) => {
    setState(nextState)
    setJumpValue(String(nextState.current_sentence_index + 1))
  }, [])

  const refreshRoundList = useCallback(async (targetBatchId: string) => {
    const response = await api.recordingRounds(targetBatchId)
    setRecordingRounds(response.rounds)
    return response
  }, [])

  const refresh = useCallback(async () => {
    const [nextState, nextDevices, batches] = await Promise.all([
      api.state(),
      api.devices(),
      api.recordingBatches(),
    ])
    commitState(nextState)
    setDevices(nextDevices)
    setRecordingBatches(batches.batches)
    setRecordingRoot(batches.root)
    if (nextState.batch_id) {
      setBatchId(nextState.batch_id)
      setOpenedBatchId(nextState.batch_id)
      await refreshRoundList(nextState.batch_id)
    }
  }, [commitState, refreshRoundList])

  useEffect(() => {
    queueMicrotask(() => refresh().catch((reason: Error) => setError(reason.message)))
    const timer = window.setInterval(() => setNow(Date.now()), 100)
    return () => window.clearInterval(timer)
  }, [refresh])

  useEffect(() => {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
    return connectReconnectingWebSocket({
      url: `${protocol}//${window.location.host}/ws/events`,
      onMessage: (event) => {
        const message = JSON.parse(event.data) as {
          type: string
          payload: (HostState & { accepted?: boolean }) | {
            accepted?: boolean
            message?: string
            state?: HostState
          }
        }
        if (['state_changed', 'take_uploaded', 'camera_uploaded'].includes(message.type)) {
          const nextState = message.payload as HostState
          commitState(nextState)
          if (nextState.batch_id) {
            setBatchId(nextState.batch_id)
            setOpenedBatchId(nextState.batch_id)
            void refreshRoundList(nextState.batch_id).catch(() => undefined)
          }
        }
        if (['device_updated', 'device_selected', 'command_ack'].includes(message.type)) {
          api.devices().then(setDevices).catch(() => undefined)
        }
        if (
          message.type === 'device_selected' ||
          (message.type === 'command_ack' && message.payload.accepted)
        ) {
          setError(null)
        }
        if (['recording_start_failed', 'recording_interrupted'].includes(message.type)) {
          const payload = message.payload as { message?: string; state?: HostState }
          if (payload.state) commitState(payload.state)
          setError(payload.message ?? 'Quest 已中止本次录制')
          void stopCameraRecordingRef.current()
        }
      },
    })
  }, [commitState, refreshRoundList])

  useEffect(() => {
    document.querySelector(`[data-sentence-index="${state?.current_sentence_index ?? 0}"]`)
      ?.scrollIntoView({ block: 'nearest' })
  }, [deferredSentenceQuery, sentenceFilter, state?.current_sentence_index])

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

  const uploadCameraTake = useCallback(async (blob: Blob, context: CaptureContext) => {
    if (blob.size === 0) return
    setUploading(true)
    try {
      await api.uploadCamera(
        context.takeId,
        context.sessionId,
        context.sentenceId,
        blob,
        context.startedAtUnixMs,
        context.stoppedAtUnixMs,
        context.firstChunkAtUnixMs,
      )
      await removeCapture(context.captureId)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '相机视频上传失败')
    } finally {
      setUploading(false)
    }
  }, [refresh])

  const beginCameraRecording = useCallback((responseState: HostState, startAt: number) => {
    const stream = mediaStreamRef.current
    const take = responseState.current_take
    const sentence = responseState.sentences[responseState.current_sentence_index]
    if (!stream) throw new Error('外置相机视频流已经断开')
    if (!take || !sentence) throw new Error('后端没有返回当前 Take')
    if (activeCaptureRef.current !== null) throw new Error('上一条相机录制尚未结束')
    const mimeType = preferredMimeType()
    const context: CaptureContext = {
      captureId: createCaptureId(responseState.session_id, sentence.sentence_id, take.take_id),
      takeId: take.take_id,
      sessionId: responseState.session_id,
      sentenceId: sentence.sentence_id,
      mimeType: mimeType || 'video/webm',
      scheduledAtUnixMs: startAt,
      startedAtUnixMs: 0,
      firstChunkAtUnixMs: 0,
      stoppedAtUnixMs: 0,
    }
    const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined)
    let resolveFinalized: () => void = () => {}
    const finalized = new Promise<void>((resolve) => {
      resolveFinalized = resolve
    })
    const reportCacheError = (reason: unknown) => {
      setError(reason instanceof Error ? `相机缓存失败：${reason.message}` : '相机缓存失败')
    }
    const capture: ActiveCameraCapture = {
      context,
      recorder,
      chunks: [],
      chunkSequence: 0,
      persistence: saveCaptureContext(context).catch(reportCacheError),
      finalized,
      resolveFinalized,
    }
    activeCaptureRef.current = capture
    recorderRef.current = recorder
    setCaptureActive(true)
    recorder.onstart = () => {
      context.startedAtUnixMs = Date.now()
      capture.persistence = capture.persistence
        .then(() => saveCaptureContext(context))
        .catch(reportCacheError)
    }
    recorder.ondataavailable = (event) => {
      if (event.data.size === 0) return
      if (context.firstChunkAtUnixMs === 0) context.firstChunkAtUnixMs = Date.now()
      capture.chunks.push(event.data)
      const sequence = capture.chunkSequence
      capture.chunkSequence += 1
      capture.persistence = capture.persistence
        .then(() => saveCaptureContext(context))
        .then(() => appendCaptureChunk(context.captureId, sequence, event.data))
        .catch(reportCacheError)
    }
    recorder.onerror = (event) => {
      setError(`相机录制失败：${event.error.message}`)
      void api.abort().catch((reason: Error) => {
        setError(`相机录制失败，且 Quest 中止失败：${reason.message}`)
      })
    }
    recorder.onstop = () => {
      void (async () => {
        try {
          context.stoppedAtUnixMs = Date.now()
          capture.persistence = capture.persistence
            .then(() => saveCaptureContext(context))
            .catch(reportCacheError)
          const blob = new Blob(capture.chunks, { type: recorder.mimeType || context.mimeType })
          await capture.persistence
          await uploadCameraTake(blob, context)
        } catch (reason) {
          setError(reason instanceof Error ? reason.message : '无法保存相机录制缓存')
        } finally {
          if (activeCaptureRef.current === capture) activeCaptureRef.current = null
          if (recorderRef.current === recorder) recorderRef.current = null
          setCaptureActive(false)
          capture.resolveFinalized()
        }
      })()
    }
    cameraStartTimerRef.current = window.setTimeout(() => {
      cameraStartTimerRef.current = null
      if (activeCaptureRef.current !== capture || recorder.state !== 'inactive') return
      try {
        recorder.start(1000)
      } catch (reason) {
        activeCaptureRef.current = null
        if (recorderRef.current === recorder) recorderRef.current = null
        setCaptureActive(false)
        capture.resolveFinalized()
        void removeCapture(context.captureId).catch(reportCacheError)
        const message = reason instanceof Error
          ? `外置相机无法开始录制：${reason.message}`
          : '外置相机无法开始录制'
        setError(message)
        void api.abort().catch((abortReason: Error) => {
          setError(`${message}；Quest 中止失败：${abortReason.message}`)
        })
      }
    }, Math.max(0, startAt - Date.now()))
  }, [uploadCameraTake])

  const stopCameraRecording = useCallback(async () => {
    const capture = activeCaptureRef.current
    if (!capture) return
    if (cameraStartTimerRef.current !== null) {
      window.clearTimeout(cameraStartTimerRef.current)
      cameraStartTimerRef.current = null
    }
    if (capture.recorder.state !== 'inactive') {
      capture.recorder.stop()
      await capture.finalized
      return
    }
    activeCaptureRef.current = null
    if (recorderRef.current === capture.recorder) recorderRef.current = null
    setCaptureActive(false)
    await capture.persistence
    await removeCapture(capture.context.captureId)
    capture.resolveFinalized()
  }, [])

  useEffect(() => {
    stopCameraRecordingRef.current = stopCameraRecording
  }, [stopCameraRecording])

  const recoverPendingCameraTakes = useCallback(async () => {
    if (recoveryStartedRef.current) return
    recoveryStartedRef.current = true
    setRecoveringCapture(true)
    try {
      const pending = await listPendingCaptures()
      if (pending.length === 0) return
      const serverState = await api.state()
      if (['countdown', 'recording', 'stopping'].includes(serverState.recording_status)) {
        await api.abort()
      }
      for (const context of pending) {
        const blob = await loadCaptureBlob(context)
        if (blob.size === 0 || context.startedAtUnixMs === 0) {
          await removeCapture(context.captureId)
          continue
        }
        if (context.firstChunkAtUnixMs === 0) context.firstChunkAtUnixMs = context.startedAtUnixMs
        if (context.stoppedAtUnixMs === 0) context.stoppedAtUnixMs = Date.now()
        await uploadCameraTake(blob, context)
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法恢复上一次相机录制')
    } finally {
      setRecoveringCapture(false)
    }
  }, [uploadCameraTake])

  useEffect(() => {
    queueMicrotask(() => void recoverPendingCameraTakes())
  }, [recoverPendingCameraTakes])

  useEffect(() => {
    const sendHeartbeat = () => {
      void api.operatorHeartbeat().catch(() => undefined)
    }
    sendHeartbeat()
    const heartbeatTimer = window.setInterval(sendHeartbeat, 2000)
    const handlePageHide = () => {
      if (activeCaptureRef.current?.recorder.state === 'recording') {
        activeCaptureRef.current.recorder.requestData()
      }
      if (activeCaptureRef.current) void api.operatorDisconnect().catch(() => undefined)
    }
    window.addEventListener('pagehide', handlePageHide)
    return () => {
      window.clearInterval(heartbeatTimer)
      window.removeEventListener('pagehide', handlePageHide)
    }
  }, [])

  const startRecording = useCallback(async () => {
    setError(null)
    if (!contextReady || !currentBatchId || !currentRoundId) {
      setError('请先打开录制批次并选择轮次')
      return
    }
    if (!selectedDeviceId) {
      setError('请先连接 Quest')
      return
    }
    if (!cameraReady || captureBusy) {
      setError(startBlockedReason ?? '请先连接外置相机')
      return
    }
    try {
      const response = await api.start(currentBatchId, currentRoundId)
      commitState(response.state)
      setRecordingBatches((current) => current.includes(currentBatchId) ? current : [...current, currentBatchId])
      if (!['countdown', 'recording'].includes(response.state.recording_status)) {
        throw new Error('Quest 未能启动 Pose 录制，请确认身体追踪已经就绪')
      }
      try {
        if (!response.start_at_unix_ms || !response.state.current_take) {
          throw new Error('Quest 未返回有效的录制开始时间')
        }
        beginCameraRecording(response.state, response.start_at_unix_ms)
      } catch (reason) {
        try {
          await api.abort()
        } catch (abortReason) {
          const cameraDetail = reason instanceof Error ? reason.message : '未知错误'
          const abortDetail = abortReason instanceof Error ? abortReason.message : '未知错误'
          throw new Error(
            `相机启动失败（${cameraDetail}），且 Quest 中止失败：${abortDetail}`,
            { cause: abortReason },
          )
        }
        throw reason
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法开始录制')
    }
  }, [beginCameraRecording, cameraReady, captureBusy, commitState, contextReady, currentBatchId, currentRoundId, selectedDeviceId, startBlockedReason])

  const stopRecording = useCallback(async () => {
    setError(null)
    try {
      const response = await api.stop()
      commitState(response.state)
      await stopCameraRecording()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法结束录制')
    }
  }, [commitState, stopCameraRecording])

  const resetRecording = useCallback(async () => {
    setError(null)
    try {
      const response = await api.reset()
      commitState(response.state)
      await stopCameraRecording()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法重新录制')
    }
  }, [commitState, stopCameraRecording])

  const openBatch = async () => {
    const targetBatchId = batchId.trim()
    if (!targetBatchId) {
      setError('请输入录制批次目录')
      return
    }
    try {
      await refreshRoundList(targetBatchId)
      setBatchId(targetBatchId)
      setOpenedBatchId(targetBatchId)
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法打开录制批次')
    }
  }

  const createNextRound = async () => {
    if (!openedBatchId || !missingStandardRoundId) return
    try {
      const nextState = await api.createRound(openedBatchId, missingStandardRoundId)
      commitState(nextState)
      setRecordingBatches((current) => current.includes(openedBatchId) ? current : [...current, openedBatchId])
      await refreshRoundList(openedBatchId)
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法创建录制轮次')
    }
  }

  const saveSentenceEditor = async () => {
    if (!sentenceEditor || !sentenceEditor.text.trim()) return
    try {
      const nextState = sentenceEditor.mode === 'edit'
        ? await api.updateSentence(sentenceEditor.index, sentenceEditor.text)
        : await api.addSentence(sentenceEditor.index, sentenceEditor.text)
      commitState(nextState)
      setSentenceEditor(null)
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法保存句子')
    }
  }

  const activateRound = async (roundId: string) => {
    if (!openedBatchId || !roundId) return
    try {
      commitState(await api.selectRound(openedBatchId, roundId))
      await refreshRoundList(openedBatchId)
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法切换录制轮次')
    }
  }

  const selectSentence = async (sentenceIndex: number) => {
    if (!contextReady || state?.recording_status !== 'ready') return
    try {
      commitState(await api.selectSentence(sentenceIndex))
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法切换句子')
    }
  }

  const jumpToSentence = () => {
    const sentenceNumber = Number.parseInt(jumpValue, 10)
    if (!state || !Number.isInteger(sentenceNumber) || sentenceNumber < 1 || sentenceNumber > state.sentences.length) {
      setError(`请输入 1–${state?.sentences.length ?? 300} 的句子编号`)
      return
    }
    void selectSentence(sentenceNumber - 1)
  }

  const selectNextIncomplete = () => {
    if (!state) return
    const afterCurrent = state.sentences.find(
      (sentence) => !sentence.completed && sentence.index > state.current_sentence_index,
    )
    const target = afterCurrent ?? state.sentences.find((sentence) => !sentence.completed)
    if (!target) {
      setError('当前轮次的 300 句已经全部完成')
      return
    }
    void selectSentence(target.index)
  }

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.code !== 'Space' || event.repeat) return
      if (sentenceEditor && isEditableTarget(event.target)) return
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
  }, [isRecording, resetRecording, sentenceEditor, startRecording, stopRecording])

  const selectQuest = async (deviceId: string) => {
    try {
      await api.selectDevice(deviceId)
      await refresh()
      setError(null)
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
          <span>工作站 {state.station_id}</span>
          <span>{state.batch_id && state.round_id ? `${state.batch_id} / ${state.round_id}` : '尚未选择录制轮次'}</span>
          {state.signing_mode && <span className={`mode-chip ${state.signing_mode}`}>{state.signing_mode === 'rough' ? '粗打' : '精打'}</span>}
          <strong>{completedCount} 已完成 · 第 {state.current_sentence_index + 1} / {state.sentences.length} 句</strong>
        </div>
      </header>

      <div className="workspace">
        <aside className="left-rail">
          <div className="rail-title"><h2>录制准备</h2><span>{devices.length + (cameraReady ? 1 : 0)} 台在线</span></div>
          <section className="device-section recording-target">
            <div className="section-heading"><h3>录制位置</h3><span>{contextReady ? '已打开' : '必选'}</span></div>
            <div className="batch-picker">
              <label>
                <FolderOpen size={18} />
                <input
                  aria-label="录制批次目录"
                  list="recording-batches"
                  placeholder="输入新批次或选择已有批次"
                  value={batchId}
                  disabled={state.recording_status !== 'ready'}
                  onChange={(event) => { setBatchId(event.target.value); setError(null) }}
                  onKeyDown={(event) => { if (event.key === 'Enter') void openBatch() }}
                  onBlur={(event) => setBatchId(event.currentTarget.value.trim())}
                />
              </label>
              <button disabled={state.recording_status !== 'ready' || !batchId.trim()} onClick={() => void openBatch()}>打开</button>
            </div>
            <datalist id="recording-batches">
              {recordingBatches.map((batch) => <option key={batch} value={batch} />)}
            </datalist>
            <div className="round-picker">
              <select
                aria-label="录制轮次"
                value={activeRoundId ?? ''}
                disabled={!openedBatchId || state.recording_status !== 'ready'}
                onChange={(event) => void activateRound(event.target.value)}
              >
                <option value="">{recordingRounds.length ? '选择轮次' : '尚无轮次'}</option>
                {recordingRounds.map((round) => (
                  <option key={round.round_id} value={round.round_id}>
                    {round.signing_mode === 'rough' ? '粗打' : round.signing_mode === 'precise' ? '精打' : round.round_id} · {round.completed_sentences}/{round.total_sentences}
                  </option>
                ))}
              </select>
              <button disabled={!openedBatchId || !missingStandardRoundId || state.recording_status !== 'ready'} onClick={() => void createNextRound()}>
                <Plus size={15} />{missingStandardRoundId ? '初始化粗打 / 精打' : '粗打 / 精打已就绪'}
              </button>
            </div>
            <p>{openedBatchId ? `${recordingRoot}/${openedBatchId}${activeRoundId ? `/${activeRoundId}` : ''}` : '开始录制前必须打开批次并选择轮次'}</p>
          </section>
          <section className="device-section">
            <div className="section-heading"><h3>Quest 设备</h3><button className="text-button" onClick={() => api.scan().catch((reason: Error) => setError(reason.message))}><RefreshCw size={14} />重新扫描</button></div>
            <div className="device-list">
              {devices.length === 0 && <div className="empty-row"><Headset size={20} /><span>等待 Quest 广播</span></div>}
              {devices.map((device) => {
                const ownedByOther = Boolean(
                  device.paired_station_id && device.paired_station_id !== state.station_id,
                )
                return (
                  <button
                    key={device.device_id}
                    className={`device-row ${device.device_id === selectedDevice?.device_id ? 'selected' : ''}`}
                    disabled={ownedByOther}
                    onClick={() => void selectQuest(device.device_id)}
                  >
                    <Headset size={21} />
                    <span>
                      <strong>{device.name} · {device.device_id.slice(-6).toUpperCase()}</strong>
                      <small>
                        {device.ip} · {ownedByOther
                          ? `已绑定 ${device.paired_station_id}`
                          : device.state === 'recording'
                          ? '录制中'
                          : device.paired ? '已连接' : '可用'}
                      </small>
                    </span>
                    {device.device_id === selectedDevice?.device_id ? <Check size={18} /> : <ChevronRight size={17} />}
                  </button>
                )
              })}
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
          {state.mode_switch_notice && (
            <div className="mode-switch-banner" role="alert">
              <strong>{state.mode_switch_notice}</strong>
              {state.signing_mode && <span>当前进入{state.signing_mode === 'rough' ? '粗打' : '精打'} · 第 {state.current_sentence_index + 1} 句</span>}
            </div>
          )}
          <section className="prompt-block">
            <div className="section-heading">
              <h2>当前句子</h2>
              <div className="prompt-actions">
                <span>{CATEGORY_LABELS[currentSentence?.category ?? ''] ?? currentSentence?.category} · #{String(state.current_sentence_index + 1).padStart(3, '0')}</span>
                <button disabled={!contextReady || state.recording_status !== 'ready'} onClick={() => setSentenceEditor({ mode: 'edit', index: state.current_sentence_index, text: currentSentence?.text ?? '' })}><Pencil size={14} />修改</button>
                <button disabled={!contextReady || state.recording_status !== 'ready'} onClick={() => setSentenceEditor({ mode: 'add', index: state.current_sentence_index, text: '' })}><Plus size={14} />在后面临时加一句</button>
              </div>
            </div>
            <p>{currentSentence?.text}</p>
          </section>
          <div className="video-grid">
            <VideoPanel kind="camera" title="外置相机" meta={cameraReady ? '1280 × 720' : '未就绪'} videoRef={videoRef} active={state.recording_status === 'recording'} />
            <QuestPreviewPanel key={selectedDeviceId ?? 'no-device'} deviceId={selectedDeviceId} active={state.recording_status === 'recording'} />
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
          <section className="sentence-section">
            <div className="section-heading"><h2>句子进度</h2><span>{completedCount} / {state.sentences.length}</span></div>
            <div className="progress-track" aria-label={`已完成 ${completedCount} / ${state.sentences.length}`}>
              <i style={{ width: `${completedCount / state.sentences.length * 100}%` }} />
            </div>
            <div className="sentence-jump">
              <label>
                <span>从第</span>
                <input
                  aria-label="跳转句子编号"
                  type="number"
                  min="1"
                  max={state.sentences.length}
                  value={jumpValue}
                  disabled={!contextReady || state.recording_status !== 'ready'}
                  onChange={(event) => setJumpValue(event.target.value)}
                  onKeyDown={(event) => { if (event.key === 'Enter') jumpToSentence() }}
                />
                <span>句开始</span>
              </label>
              <button disabled={!contextReady || state.recording_status !== 'ready'} onClick={jumpToSentence}>跳转</button>
            </div>
            <div className="sentence-nav-actions">
              <button disabled={!contextReady || state.recording_status !== 'ready' || state.current_sentence_index === 0} onClick={() => void selectSentence(state.current_sentence_index - 1)}><ArrowLeft size={15} />上一句</button>
              <button disabled={!contextReady || state.recording_status !== 'ready' || state.current_sentence_index === state.sentences.length - 1} onClick={() => void selectSentence(state.current_sentence_index + 1)}>下一句<ArrowRight size={15} /></button>
              <button disabled={!contextReady || state.recording_status !== 'ready'} onClick={selectNextIncomplete}><SkipForward size={15} />下一条未完成</button>
            </div>
            <label className="sentence-search">
              <Search size={16} />
              <input aria-label="搜索句子" value={sentenceQuery} placeholder="搜索编号、分类或文本" onChange={(event) => setSentenceQuery(event.target.value)} />
            </label>
            <div className="sentence-filters" role="group" aria-label="句子完成状态筛选">
              <ListFilter size={15} />
              <button className={sentenceFilter === 'all' ? 'active' : ''} onClick={() => setSentenceFilter('all')}>全部</button>
              <button className={sentenceFilter === 'pending' ? 'active' : ''} onClick={() => setSentenceFilter('pending')}>未完成</button>
              <button className={sentenceFilter === 'completed' ? 'active' : ''} onClick={() => setSentenceFilter('completed')}>已完成</button>
            </div>
            <ol className="sentence-list">
              {visibleSentences.map((sentence) => (
                <li key={sentence.sentence_id} data-sentence-index={sentence.index} className={`${sentence.status} ${sentence.completed ? 'completed' : ''}`}>
                  <button disabled={!contextReady || state.recording_status !== 'ready'} onClick={() => void selectSentence(sentence.index)}>
                    <span className="sentence-index">{String(sentence.index + 1).padStart(3, '0')}</span>
                    <span className="sentence-copy"><p>{sentence.text}</p><small>{CATEGORY_LABELS[sentence.category] ?? sentence.category}{sentence.take_count ? ` · ${sentence.take_count} Take` : ''}</small></span>
                    {sentence.completed ? <Check size={17} /> : sentence.status === 'current' ? <Circle size={10} fill="currentColor" /> : null}
                  </button>
                </li>
              ))}
            </ol>
            {visibleSentences.length === 0 && <div className="empty-sentence">没有符合条件的句子</div>}
          </section>
          <section className="take-section">
            <div className="section-heading"><h2>当前句 Take</h2><span>{uploading ? '上传中' : `${state.takes.length} 个`}</span></div>
            <div className="take-list">
              {state.takes.length === 0 && <div className="empty-take"><UploadCloud size={23} /><span>录制后在这里查看文件</span></div>}
              {state.takes.map((take) => (
                <article key={take.take_id}>
                  <header><strong>Take {String(take.take_index).padStart(3, '0')}</strong><span>{take.status === 'recording' ? '录制中' : take.status === 'complete' ? '完整' : '候选'}</span></header>
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
          <button className="action-start" disabled={state.recording_status !== 'ready' || !captureReady} title={startBlockedReason} onClick={() => void startRecording()}><Play size={20} fill="currentColor" />开始录制</button>
          <button className="action-stop" disabled={!isRecording} onClick={() => void stopRecording()}><Square size={18} fill="currentColor" />结束录制</button>
          <button className="action-reset" disabled={!contextReady} onClick={() => void resetRecording()}><RotateCcw size={20} />重新录制</button>
        </div>
        <div className="pedal-hint">
          <div className="keycap">SPACE</div>
          <span>短按开始 / 结束，长按重新录制</span>
          {holdProgress > 0 && <div className="hold-ring" style={{ '--progress': `${holdProgress * 360}deg` } as React.CSSProperties}><i /></div>}
        </div>
      </footer>
      {sentenceEditor && (
        <div className="sentence-editor-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setSentenceEditor(null) }}>
          <section className="sentence-editor" role="dialog" aria-modal="true" aria-label={sentenceEditor.mode === 'edit' ? '修改句子' : '临时添加句子'}>
            <h2>{sentenceEditor.mode === 'edit' ? `修改第 ${sentenceEditor.index + 1} 句` : `在第 ${sentenceEditor.index + 1} 句后添加`}</h2>
            <textarea autoFocus value={sentenceEditor.text} onChange={(event) => setSentenceEditor({ ...sentenceEditor, text: event.target.value })} onKeyDown={(event) => { if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) void saveSentenceEditor() }} />
            <div><button onClick={() => setSentenceEditor(null)}>取消</button><button className="primary" disabled={!sentenceEditor.text.trim()} onClick={() => void saveSentenceEditor()}>保存 Ctrl+Enter</button></div>
          </section>
        </div>
      )}
    </div>
  )
}
