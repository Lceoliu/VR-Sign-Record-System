import { useEffect, useRef, useState } from 'react'
import { connectReconnectingWebSocket } from '../reconnectingWebSocket'
import { SkeletonPanel } from './SkeletonPanel'
import { VideoPanel } from './VideoPanel'

interface QuestPreviewPanelProps {
  deviceId: string | null
  active: boolean
}

export function QuestPreviewPanel({ deviceId, active }: QuestPreviewPanelProps) {
  const [imageUrl, setImageUrl] = useState<string | null>(null)
  const [connected, setConnected] = useState(false)
  const [stale, setStale] = useState(true)
  const currentUrlRef = useRef<string | null>(null)
  const lastFrameAtRef = useRef(0)

  useEffect(() => {
    if (!deviceId) return

    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
    const disconnect = connectReconnectingWebSocket({
      url: `${protocol}//${window.location.host}/ws/preview/${encodeURIComponent(deviceId)}`,
      binaryType: 'blob',
      onOpen: () => setConnected(true),
      onClose: () => setConnected(false),
      onMessage: (event) => {
        const nextUrl = URL.createObjectURL(event.data as Blob)
        if (currentUrlRef.current) URL.revokeObjectURL(currentUrlRef.current)
        currentUrlRef.current = nextUrl
        lastFrameAtRef.current = Date.now()
        setStale(false)
        setImageUrl(nextUrl)
      },
    })

    return () => {
      disconnect()
      if (currentUrlRef.current) URL.revokeObjectURL(currentUrlRef.current)
      currentUrlRef.current = null
      lastFrameAtRef.current = 0
      setImageUrl(null)
    }
  }, [deviceId])

  useEffect(() => {
    const timer = window.setInterval(() => {
      setStale(lastFrameAtRef.current === 0 || Date.now() - lastFrameAtRef.current > 4000)
    }, 1000)
    return () => window.clearInterval(timer)
  }, [])

  return (
    <div className="quest-monitor-stack">
      <SkeletonPanel title="Quest 实时动作" deviceId={deviceId} active={active} />
      <VideoPanel
        kind="quest"
        title="Quest 场景画面"
        meta={!connected ? '未连接' : stale ? '画面已中断' : '480 × 270 · 3 FPS'}
        imageUrl={stale ? null : imageUrl}
        active={active}
      />
    </div>
  )
}
