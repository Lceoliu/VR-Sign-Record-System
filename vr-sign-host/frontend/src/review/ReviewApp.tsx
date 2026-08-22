import {
  AlertTriangle,
  ArrowLeft,
  ArrowRight,
  ChevronDown,
  ChevronUp,
  Film,
  LoaderCircle,
  Pause,
  Play,
  RotateCcw,
} from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { api } from '../api'
import type { ReviewItem, ReviewMeta } from '../types'
import { ReviewPosePanel } from './ReviewPosePanel'
import './review.css'

export function ReviewApp() {
  const videoRef = useRef<HTMLVideoElement>(null)
  const [datasets, setDatasets] = useState<string[]>([])
  const [dataset, setDataset] = useState('')
  const [items, setItems] = useState<ReviewItem[]>([])
  const [teacher, setTeacher] = useState('')
  const [roundId, setRoundId] = useState('')
  const [index, setIndex] = useState(0)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [isPlaying, setIsPlaying] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api.reviewDatasets()
      .then((response) => {
        setDatasets(response.datasets)
        const requested = new URLSearchParams(window.location.search).get('dataset')
        setDataset(requested && response.datasets.includes(requested) ? requested : (response.datasets[0] ?? ''))
        if (response.datasets.length === 0) setLoading(false)
      })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : '无法读取审核数据集'))
  }, [])

  useEffect(() => {
    if (!dataset) return
    api.reviewItems(dataset)
      .then((response) => {
        setItems(response.items)
        const teachers = [...new Set(response.items.map((item) => item.teacher))]
        setTeacher((current) => teachers.includes(current) ? current : (teachers[0] ?? ''))
        setRoundId('')
        setIndex(0)
        setError(null)
      })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : '无法读取审核项目'))
      .finally(() => setLoading(false))
  }, [dataset])

  const teacherOptions = useMemo(
    () => [...new Set(items.map((item) => item.teacher))],
    [items],
  )
  const roundOptions = useMemo(
    () => [...new Set(items.filter((item) => item.teacher === teacher).map((item) => item.round_id))],
    [items, teacher],
  )
  const activeRoundId = roundOptions.includes(roundId) ? roundId : (roundOptions[0] ?? '')

  const filteredItems = useMemo(
    () => items.filter((item) => item.teacher === teacher && item.round_id === activeRoundId),
    [activeRoundId, items, teacher],
  )
  const activeIndex = Math.min(index, Math.max(filteredItems.length - 1, 0))
  const current = filteredItems[activeIndex]
  const activeRoundIndex = roundOptions.indexOf(activeRoundId)

  const navigate = useCallback((delta: number) => {
    setIndex((currentIndex) => Math.min(Math.max(currentIndex + delta, 0), Math.max(filteredItems.length - 1, 0)))
  }, [filteredItems.length])

  const selectRound = useCallback((nextRoundId: string) => {
    if (!nextRoundId || nextRoundId === activeRoundId) return
    const targetItems = items.filter((item) => item.teacher === teacher && item.round_id === nextRoundId)
    const matchingIndex = current
      ? targetItems.findIndex((item) => item.sentence_id === current.sentence_id)
      : -1
    setRoundId(nextRoundId)
    setIndex(matchingIndex >= 0 ? matchingIndex : Math.min(activeIndex, Math.max(targetItems.length - 1, 0)))
  }, [activeIndex, activeRoundId, current, items, teacher])

  const changeRound = useCallback((delta: number) => {
    const nextIndex = Math.min(Math.max(activeRoundIndex + delta, 0), Math.max(roundOptions.length - 1, 0))
    selectRound(roundOptions[nextIndex] ?? '')
  }, [activeRoundIndex, roundOptions, selectRound])

  const togglePlayback = useCallback(() => {
    const video = videoRef.current
    if (!video) return
    if (video.paused || video.ended) {
      if (video.ended) video.currentTime = 0
      void video.play()
    } else {
      video.pause()
    }
  }, [])

  const replay = useCallback(() => {
    const video = videoRef.current
    if (!video) return
    video.currentTime = 0
    void video.play()
  }, [])

  const toggleIssue = useCallback(async (field: 'video_issue' | 'sentence_issue') => {
    if (!current || saving) return
    const updated = { ...current, [field]: !current[field] }
    setItems((all) => all.map((item) => item.id === updated.id ? updated : item))
    setSaving(true)
    setError(null)
    try {
      await api.updateReviewLabel(updated)
    } catch (reason: unknown) {
      setItems((all) => all.map((item) => item.id === current.id ? current : item))
      setError(reason instanceof Error ? reason.message : '问题标记保存失败')
    } finally {
      setSaving(false)
    }
  }, [current, saving])

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (isEditableTarget(event.target) || event.repeat) return
      switch (event.code) {
        case 'KeyA':
        case 'ArrowLeft':
          event.preventDefault()
          navigate(-1)
          break
        case 'KeyD':
        case 'ArrowRight':
          event.preventDefault()
          navigate(1)
          break
        case 'KeyW':
          event.preventDefault()
          changeRound(-1)
          break
        case 'KeyS':
          event.preventDefault()
          changeRound(1)
          break
        case 'KeyQ':
          event.preventDefault()
          void toggleIssue('video_issue')
          break
        case 'KeyE':
          event.preventDefault()
          void toggleIssue('sentence_issue')
          break
        case 'Space':
          event.preventDefault()
          togglePlayback()
          break
        case 'KeyR':
          event.preventDefault()
          replay()
          break
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [changeRound, navigate, replay, toggleIssue, togglePlayback])

  if (loading) {
    return (
      <main className="review-loading">
        <LoaderCircle className="review-spinner" size={28} />
        <span>正在载入审核数据</span>
      </main>
    )
  }

  if (!current) {
    return (
      <main className="review-loading">
        <AlertTriangle size={28} />
        <span>{error ?? '没有可审核的完成录制'}</span>
      </main>
    )
  }

  const videoUrl = reviewFileUrl(current.video_file)
  const poseUrl = reviewFileUrl(current.pose_file)
  const quality = current.meta.hand_capture_quality

  return (
    <main className="review-shell">
      <header className="review-topbar">
        <h1>手语数据审核</h1>
        <label>
          <span>数据集</span>
          <select value={dataset} onChange={(event) => { setLoading(true); setDataset(event.target.value); setIndex(0); event.currentTarget.blur() }}>
            {datasets.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
        </label>
        <label>
          <span>教师</span>
          <select value={teacher} onChange={(event) => { setTeacher(event.target.value); setIndex(0); event.currentTarget.blur() }}>
            {teacherOptions.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
        </label>
        <label>
          <span>轮次</span>
          <select value={activeRoundId} onChange={(event) => { selectRound(event.target.value); event.currentTarget.blur() }}>
            {roundOptions.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
        </label>
        <div className="review-progress">进度 <strong>{activeIndex + 1} / {filteredItems.length}</strong></div>
      </header>

      <section className="review-workspace">
        <div className="review-video-region">
          {videoUrl ? (
            <video
              key={current.id}
              ref={videoRef}
              src={videoUrl}
              controls
              autoPlay
              muted
              playsInline
              onPlay={() => setIsPlaying(true)}
              onPause={() => setIsPlaying(false)}
              onEnded={() => setIsPlaying(false)}
            />
          ) : (
            <div className="review-missing-media">
              <Film size={34} strokeWidth={1.5} />
              <strong>缺少视频文件</strong>
            </div>
          )}
        </div>

        <aside className="review-inspector">
          <section className="review-meta">
            <MetaRow label="Take" value={current.take_id} />
            <MetaRow label="时长" value={formatDuration(current.meta)} />
            <MetaRow label="帧数" value={String(current.meta.pose_frame_count ?? '—')} />
            <MetaRow label="Clean Ratio" value={formatPercent(quality?.clean_ratio)} />
            <MetaRow label="左手" value={formatHand(quality?.left_tracked_ratio, quality?.left_inside_ratio)} />
            <MetaRow label="右手" value={formatHand(quality?.right_tracked_ratio, quality?.right_inside_ratio)} />
          </section>
          <ReviewPosePanel poseUrl={poseUrl} videoRef={videoRef} />
        </aside>
      </section>

      <section className="review-sentence" aria-label="当前句子">
        <span>{current.sentence_id}</span>
        <p>{current.sentence_text}</p>
      </section>

      <footer className="review-actions">
        <div className="review-issue-actions">
          <IssueButton shortcut="Q" active={current.video_issue} disabled={saving} onClick={() => void toggleIssue('video_issue')}>
            视频有问题
          </IssueButton>
          <IssueButton shortcut="E" active={current.sentence_issue} disabled={saving} onClick={() => void toggleIssue('sentence_issue')}>
            句子／动作有问题
          </IssueButton>
          {error && <span className="review-error">{error}</span>}
        </div>
        <div className="review-playback-actions" aria-label="播放控制">
          <ShortcutButton shortcut="Space" ariaKeyShortcut="Space" onClick={togglePlayback}>
            {isPlaying ? <Pause size={17} /> : <Play size={17} />}
            {isPlaying ? '暂停' : '播放'}
          </ShortcutButton>
          <ShortcutButton shortcut="R" ariaKeyShortcut="R" onClick={replay}>
            <RotateCcw size={17} /> 重播
          </ShortcutButton>
        </div>
        <div className="review-round-actions" aria-label="轮次导航">
          <button
            type="button"
            aria-keyshortcuts="W"
            disabled={activeRoundIndex <= 0}
            onClick={() => changeRound(-1)}
          >
            <kbd>W</kbd><ChevronUp size={17} /> 上一轮
          </button>
          <strong>{activeRoundId}</strong>
          <button
            type="button"
            aria-keyshortcuts="S"
            disabled={activeRoundIndex >= roundOptions.length - 1}
            onClick={() => changeRound(1)}
          >
            <kbd>S</kbd><ChevronDown size={17} /> 下一轮
          </button>
        </div>
        <nav className="review-navigation" aria-label="审核导航">
          <button type="button" aria-keyshortcuts="A ArrowLeft" disabled={activeIndex === 0} onClick={() => navigate(-1)}>
            <kbd>A</kbd><ArrowLeft size={20} /> 上一句
          </button>
          <button type="button" aria-keyshortcuts="D ArrowRight" disabled={activeIndex === filteredItems.length - 1} onClick={() => navigate(1)}>
            <kbd>D</kbd>下一句 <ArrowRight size={20} />
          </button>
        </nav>
      </footer>
    </main>
  )
}

function MetaRow({ label, value }: { label: string; value: string }) {
  return <div className="review-meta-row"><span>{label}</span><strong>{value}</strong></div>
}

function IssueButton({ shortcut, active, disabled, onClick, children }: {
  shortcut: string
  active: boolean
  disabled: boolean
  onClick: () => void
  children: string
}) {
  return (
    <button
      type="button"
      className={`review-issue-button ${active ? 'is-active' : ''}`}
      aria-pressed={active}
      aria-keyshortcuts={shortcut}
      disabled={disabled}
      onClick={onClick}
    >
      <kbd>{shortcut}</kbd>
      <span className="review-checkmark" aria-hidden="true">{active ? '✓' : ''}</span>
      {children}
    </button>
  )
}

function ShortcutButton({ shortcut, ariaKeyShortcut, onClick, children }: {
  shortcut: string
  ariaKeyShortcut: string
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button type="button" className="review-command-button" aria-keyshortcuts={ariaKeyShortcut} onClick={onClick}>
      <kbd>{shortcut}</kbd>
      {children}
    </button>
  )
}

function reviewFileUrl(path: string | null): string | null {
  if (!path) return null
  return `/api/review/files/${path.split('/').map(encodeURIComponent).join('/')}`
}

function formatDuration(meta: ReviewMeta): string {
  if (!meta.utc_started || !meta.utc_stopped) return '—'
  const seconds = (Date.parse(meta.utc_stopped) - Date.parse(meta.utc_started)) / 1000
  if (!Number.isFinite(seconds)) return '—'
  const minutes = Math.floor(seconds / 60)
  return `${String(minutes).padStart(2, '0')}:${seconds.toFixed(3).padStart(6, '0')}`
}

function formatPercent(value: number | undefined): string {
  return typeof value === 'number' ? `${(value * 100).toFixed(1)}%` : '—'
}

function formatHand(tracked: number | undefined, inside: number | undefined): string {
  if (typeof tracked !== 'number' || typeof inside !== 'number') return '—'
  return `跟踪 ${Math.round(tracked * 100)}% · 区域 ${Math.round(inside * 100)}%`
}

function isEditableTarget(target: EventTarget | null): boolean {
  return target instanceof HTMLInputElement
    || target instanceof HTMLTextAreaElement
    || target instanceof HTMLSelectElement
    || (target instanceof HTMLElement && target.isContentEditable)
}
