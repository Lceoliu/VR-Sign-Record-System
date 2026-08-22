import { Bone } from 'lucide-react'
import { useEffect, useMemo, useRef, useState, type RefObject } from 'react'

const CONNECTIONS: ReadonlyArray<readonly [number, number]> = [
  [1, 2], [2, 3], [3, 4], [4, 5], [5, 6], [6, 7],
  [5, 8], [8, 9], [9, 10], [10, 11], [11, 12],
  [5, 13], [13, 14], [14, 15], [15, 16], [16, 17],
  [12, 18], [18, 19], [18, 20], [20, 21], [21, 22], [22, 23],
  [18, 24], [24, 25], [25, 26], [26, 27], [27, 28],
  [18, 29], [29, 30], [30, 31], [31, 32], [32, 33],
  [18, 34], [34, 35], [35, 36], [36, 37], [37, 38],
  [18, 39], [39, 40], [40, 41], [41, 42], [42, 43],
  [17, 44], [44, 45], [44, 46], [46, 47], [47, 48], [48, 49],
  [44, 50], [50, 51], [51, 52], [52, 53], [53, 54],
  [44, 55], [55, 56], [56, 57], [57, 58], [58, 59],
  [44, 60], [60, 61], [61, 62], [62, 63], [63, 64],
  [44, 65], [65, 66], [66, 67], [67, 68], [68, 69],
]

interface Position {
  x: number
  y: number
}

interface RecordedPoseFrame {
  recording_time: number
  pose_valid: boolean
  joint_count: number
  positions: Position[]
  hand_capture?: {
    left?: { tracked?: boolean }
    right?: { tracked?: boolean }
  }
}

interface ReviewPosePanelProps {
  poseUrl: string | null
  videoRef: RefObject<HTMLVideoElement | null>
}

export function ReviewPosePanel({ poseUrl, videoRef }: ReviewPosePanelProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [poseState, setPoseState] = useState<{
    url: string | null
    frames: RecordedPoseFrame[]
    error: string | null
  }>({ url: null, frames: [], error: null })
  const frames = useMemo(
    () => poseState.url === poseUrl ? poseState.frames : [],
    [poseState.frames, poseState.url, poseUrl],
  )
  const error = poseState.url === poseUrl ? poseState.error : null

  useEffect(() => {
    const controller = new AbortController()
    if (!poseUrl) return () => controller.abort()

    fetch(poseUrl, { signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error(`${response.status} ${response.statusText}`)
        return response.text()
      })
      .then((body) => {
        if (controller.signal.aborted) return
        const parsed = body
          .split(/\r?\n/)
          .filter(Boolean)
          .map((line) => JSON.parse(line) as RecordedPoseFrame)
        setPoseState({ url: poseUrl, frames: parsed, error: null })
      })
      .catch((reason: unknown) => {
        if (controller.signal.aborted) return
        setPoseState({
          url: poseUrl,
          frames: [],
          error: reason instanceof Error ? reason.message : 'Pose 读取失败',
        })
      })

    return () => controller.abort()
  }, [poseUrl])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas || frames.length === 0) return
    const context = canvas.getContext('2d')
    if (!context) return

    let animation = 0
    const render = () => {
      animation = requestAnimationFrame(render)
      const ratio = window.devicePixelRatio || 1
      const width = canvas.clientWidth
      const height = canvas.clientHeight
      if (canvas.width !== Math.round(width * ratio) || canvas.height !== Math.round(height * ratio)) {
        canvas.width = Math.round(width * ratio)
        canvas.height = Math.round(height * ratio)
      }
      context.setTransform(ratio, 0, 0, ratio, 0, 0)
      context.clearRect(0, 0, width, height)

      const time = videoRef.current?.currentTime ?? 0
      const frame = frames[findFrameIndex(frames, time)]
      const jointLimit = Math.min(frame.joint_count, frame.positions.length, 70)
      if (!frame.pose_valid || jointLimit === 0) return

      const valid = (index: number) => {
        if (index >= 18 && index <= 43) return frame.hand_capture?.left?.tracked !== false
        if (index >= 44 && index <= 69) return frame.hand_capture?.right?.tracked !== false
        return true
      }
      const points = frame.positions.slice(1, jointLimit).filter((_, index) => valid(index + 1))
      if (points.length === 0) return

      const xs = points.map((point) => point.x)
      const ys = points.map((point) => point.y)
      const minX = Math.min(...xs)
      const maxX = Math.max(...xs)
      const minY = Math.min(...ys)
      const maxY = Math.max(...ys)
      const centerX = (minX + maxX) / 2
      const centerY = (minY + maxY) / 2
      const scale = Math.min(
        (width * 0.82) / Math.max(maxX - minX, 0.7),
        (height * 0.82) / Math.max(maxY - minY, 0.9),
      )
      const project = (index: number): [number, number] => [
        width / 2 + (frame.positions[index].x - centerX) * scale,
        height / 2 - (frame.positions[index].y - centerY) * scale,
      ]

      context.lineWidth = 2.5
      context.lineCap = 'round'
      for (const [from, to] of CONNECTIONS) {
        if (from >= jointLimit || to >= jointLimit) continue
        const linked = valid(from) && valid(to)
        const [x1, y1] = project(from)
        const [x2, y2] = project(to)
        context.strokeStyle = linked ? '#6fc9b0' : '#59625e'
        context.globalAlpha = linked ? 1 : 0.45
        context.beginPath()
        context.moveTo(x1, y1)
        context.lineTo(x2, y2)
        context.stroke()
      }

      context.globalAlpha = 1
      for (let index = 1; index < jointLimit; index += 1) {
        const [x, y] = project(index)
        context.fillStyle = valid(index) ? '#e8efec' : '#59625e'
        context.beginPath()
        context.arc(x, y, valid(index) ? 2.6 : 1.8, 0, Math.PI * 2)
        context.fill()
      }
    }

    animation = requestAnimationFrame(render)
    return () => cancelAnimationFrame(animation)
  }, [frames, videoRef])

  return (
    <section className="review-pose-panel">
      <header>
        <strong>Pose</strong>
        <span>{frames.length > 0 ? `${frames.length} 帧` : error ? '读取失败' : '读取中'}</span>
      </header>
      <div className="review-pose-canvas-wrap">
        <canvas ref={canvasRef} />
        {(frames.length === 0 || error) && (
          <div className="review-pose-placeholder">
            <Bone size={28} strokeWidth={1.5} />
            <span>{error ?? (poseUrl ? '正在读取 Pose' : '缺少 Pose 文件')}</span>
          </div>
        )}
      </div>
    </section>
  )
}

function findFrameIndex(frames: RecordedPoseFrame[], time: number): number {
  let low = 0
  let high = frames.length - 1
  while (low < high) {
    const middle = Math.ceil((low + high) / 2)
    if (frames[middle].recording_time <= time) low = middle
    else high = middle - 1
  }
  return low
}
