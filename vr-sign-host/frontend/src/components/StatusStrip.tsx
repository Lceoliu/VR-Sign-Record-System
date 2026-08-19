import { CircleDot, RotateCcw } from 'lucide-react'
import type { RecordingStatus } from '../types'

interface StatusStripProps {
  status: RecordingStatus
  displayTime: string
}

const labels: Record<RecordingStatus, string> = {
  ready: '待机',
  countdown: '准备录制',
  recording: '正在录制',
  stopping: '正在保存',
}

export function StatusStrip({ status, displayTime }: StatusStripProps) {
  return (
    <section className={`status-strip status-${status}`}>
      <div className="status-copy">
        {status === 'stopping' ? <RotateCcw size={20} /> : <CircleDot size={20} />}
        <div>
          <span>录制状态</span>
          <strong>{labels[status]}</strong>
        </div>
      </div>
      <time>{displayTime}</time>
    </section>
  )
}

