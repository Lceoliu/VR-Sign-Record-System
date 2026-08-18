import { Bone } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import type { FrameMessage, PoseMessage, SkeletonMessage } from '../types'

const BONE_COLOR = '#6fc9b0'
const JOINT_COLOR = '#e8efec'
const INVALID_COLOR = '#59625e'
const MIN_FRAMING_HEIGHT = 0.9
const FRAMING_SMOOTHING = 0.12

interface SkeletonPanelProps {
  title: string
  deviceId: string | null
  active: boolean
}

interface Framing {
  centerX: number
  centerY: number
  height: number
}

/** Draws the Quest body skeleton as a front-facing orthographic projection. */
export function SkeletonPanel({ title, deviceId, active }: SkeletonPanelProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const topologyRef = useRef<SkeletonMessage | null>(null)
  const frameRef = useRef<FrameMessage | null>(null)
  const framingRef = useRef<Framing | null>(null)
  const frameTimesRef = useRef<number[]>([])
  const [fps, setFps] = useState(0)
  const [tracked, setTracked] = useState(0)
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    if (!deviceId) return
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
    const socket = new WebSocket(`${protocol}//${window.location.host}/ws/pose/${encodeURIComponent(deviceId)}`)
    socket.onopen = () => setConnected(true)
    socket.onclose = () => setConnected(false)
    socket.onmessage = (event) => {
      const message = JSON.parse(event.data) as PoseMessage
      if (message.type === 'skeleton') topologyRef.current = message
      if (message.type === 'frame') {
        frameRef.current = message
        const now = performance.now()
        frameTimesRef.current = [...frameTimesRef.current, now].filter((time) => now - time < 1000)
      }
    }
    return () => {
      socket.close()
      topologyRef.current = null
      frameRef.current = null
      framingRef.current = null
    }
  }, [deviceId])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const context = canvas.getContext('2d')
    if (!context) return

    let animation = 0
    const render = () => {
      animation = requestAnimationFrame(render)
      const ratio = window.devicePixelRatio || 1
      const width = canvas.clientWidth
      const height = canvas.clientHeight
      if (canvas.width !== width * ratio || canvas.height !== height * ratio) {
        canvas.width = width * ratio
        canvas.height = height * ratio
      }
      context.setTransform(ratio, 0, 0, ratio, 0, 0)
      context.clearRect(0, 0, width, height)

      const frame = frameRef.current
      if (!frame) return

      const framing = updateFraming(frame, framingRef.current)
      if (!framing) return
      framingRef.current = framing

      // Fit the smoothed body box into the panel, keeping a margin on all sides.
      const scale = (height * 0.86) / framing.height
      const project = (index: number): [number, number] => [
        width / 2 + (frame.positions[index * 3] - framing.centerX) * scale,
        height / 2 - (frame.positions[index * 3 + 1] - framing.centerY) * scale,
      ]

      const parents = topologyRef.current?.parent_indices
      if (parents) {
        context.lineWidth = 2.5
        context.lineCap = 'round'
        for (let joint = 0; joint < Math.min(parents.length, frame.joint_count); joint += 1) {
          const parent = parents[joint]
          if (parent < 0 || parent >= frame.joint_count) continue
          const linked = frame.valid[joint] && frame.valid[parent]
          context.strokeStyle = linked ? BONE_COLOR : INVALID_COLOR
          context.globalAlpha = linked ? 1 : 0.4
          const [x1, y1] = project(joint)
          const [x2, y2] = project(parent)
          context.beginPath()
          context.moveTo(x1, y1)
          context.lineTo(x2, y2)
          context.stroke()
        }
      }

      context.globalAlpha = 1
      for (let joint = 0; joint < frame.joint_count; joint += 1) {
        const [x, y] = project(joint)
        context.fillStyle = frame.valid[joint] ? JOINT_COLOR : INVALID_COLOR
        context.beginPath()
        context.arc(x, y, frame.valid[joint] ? 2.6 : 1.8, 0, Math.PI * 2)
        context.fill()
      }
    }

    animation = requestAnimationFrame(render)
    return () => cancelAnimationFrame(animation)
  }, [])

  useEffect(() => {
    const timer = window.setInterval(() => {
      const now = performance.now()
      frameTimesRef.current = frameTimesRef.current.filter((time) => now - time < 1000)
      setFps(frameTimesRef.current.length)
      const frame = frameRef.current
      setTracked(frame ? frame.valid.filter(Boolean).length : 0)
    }, 500)
    return () => window.clearInterval(timer)
  }, [])

  const hasSignal = fps > 0
  return (
    <section className="video-panel">
      <header className="section-heading video-heading">
        <h2>{title}</h2>
        <span>{hasSignal ? `${tracked} 关节 · ${fps} FPS` : connected ? '等待动作数据' : '未连接'}</span>
      </header>
      <div className={`video-frame ${active ? 'is-active' : ''}`}>
        <canvas ref={canvasRef} className="skeleton-canvas" />
        {!hasSignal && (
          <div className="video-placeholder">
            <Bone size={28} strokeWidth={1.5} />
            <span>{deviceId ? '等待 Quest 身体追踪' : '请先选择 Quest 设备'}</span>
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

/** Follows the tracked body with a smoothed box so the view does not jitter. */
function updateFraming(frame: FrameMessage, previous: Framing | null): Framing | null {
  let minX = Infinity
  let maxX = -Infinity
  let minY = Infinity
  let maxY = -Infinity
  for (let joint = 0; joint < frame.joint_count; joint += 1) {
    if (!frame.valid[joint]) continue
    const x = frame.positions[joint * 3]
    const y = frame.positions[joint * 3 + 1]
    minX = Math.min(minX, x)
    maxX = Math.max(maxX, x)
    minY = Math.min(minY, y)
    maxY = Math.max(maxY, y)
  }
  if (minX === Infinity) return previous

  const target: Framing = {
    centerX: (minX + maxX) / 2,
    centerY: (minY + maxY) / 2,
    height: Math.max(maxY - minY, MIN_FRAMING_HEIGHT),
  }
  if (!previous) return target
  return {
    centerX: previous.centerX + (target.centerX - previous.centerX) * FRAMING_SMOOTHING,
    centerY: previous.centerY + (target.centerY - previous.centerY) * FRAMING_SMOOTHING,
    height: previous.height + (target.height - previous.height) * FRAMING_SMOOTHING,
  }
}
