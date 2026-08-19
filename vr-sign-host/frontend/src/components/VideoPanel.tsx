import { Camera, MonitorUp } from 'lucide-react'
import type { RefObject } from 'react'

interface VideoPanelProps {
  kind: 'camera' | 'quest'
  title: string
  meta?: string
  videoRef?: RefObject<HTMLVideoElement | null>
  imageUrl?: string | null
  active: boolean
}

export function VideoPanel({ kind, title, meta, videoRef, imageUrl, active }: VideoPanelProps) {
  const Icon = kind === 'camera' ? Camera : MonitorUp
  return (
    <section className="video-panel">
      <header className="section-heading video-heading">
        <h2>{title}</h2>
        {meta && <span>{meta}</span>}
      </header>
      <div className={`video-frame ${active ? 'is-active' : ''}`}>
        {kind === 'camera' ? (
          <video ref={videoRef} autoPlay muted playsInline />
        ) : imageUrl ? (
          <img src={imageUrl} alt="Quest 实时画面" />
        ) : (
          <div className="video-placeholder">
            <Icon size={28} strokeWidth={1.5} />
            <span>等待 Quest 画面</span>
          </div>
        )}
        {active && (
          <div className="recording-badge">
            <span /> REC
          </div>
        )}
      </div>
    </section>
  )
}

