import { useEffect, useRef, useState } from 'react'
import { SkeletonPanel } from './SkeletonPanel'
import { VideoPanel } from './VideoPanel'

interface QuestPreviewPanelProps {
  deviceId: string | null
  active: boolean
}

export function QuestPreviewPanel({ deviceId, active }: QuestPreviewPanelProps) {
  const [imageUrl, setImageUrl] = useState<string | null>(null)
  const currentUrlRef = useRef<string | null>(null)

  useEffect(() => {
    if (!deviceId) return

    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
    const socket = new WebSocket(`${protocol}//${window.location.host}/ws/preview/${encodeURIComponent(deviceId)}`)
    socket.binaryType = 'blob'
    socket.onmessage = (event) => {
      const nextUrl = URL.createObjectURL(event.data as Blob)
      if (currentUrlRef.current) URL.revokeObjectURL(currentUrlRef.current)
      currentUrlRef.current = nextUrl
      setImageUrl(nextUrl)
    }

    return () => {
      socket.close()
      if (currentUrlRef.current) URL.revokeObjectURL(currentUrlRef.current)
      currentUrlRef.current = null
    }
  }, [deviceId])

  if (!imageUrl) {
    return <SkeletonPanel title="Quest 实时动作" deviceId={deviceId} active={active} />
  }

  return (
    <VideoPanel
      kind="quest"
      title="Quest 实时画面"
      meta="640 × 360 · 8 FPS"
      imageUrl={imageUrl}
      active={active}
    />
  )
}
