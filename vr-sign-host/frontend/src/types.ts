export type RecordingStatus = 'ready' | 'countdown' | 'recording' | 'stopping'

export interface DeviceInfo {
  device_id: string
  name: string
  model: string
  app_version: string
  ip: string
  control_port: number
  capabilities: string[]
  state: string
  selected: boolean
  paired: boolean
  paired_station_id: string | null
  last_seen_unix_ms: number
  preview_frames: number
  pose_packets: number
}

export interface SentenceItem {
  sentence_id: string
  index: number
  category: string
  text: string
  status: 'completed' | 'current' | 'pending'
  completed: boolean
  take_count: number
}

export interface TakeQuality {
  frames: number
  clean_ratio: number
  left_tracked_ratio: number
  right_tracked_ratio: number
  left_inside_ratio: number
  right_inside_ratio: number
  guidance_enabled: boolean
}

export interface TakeItem {
  take_id: string
  take_index: number
  status: 'recording' | 'candidate' | 'complete'
  pose_file: string | null
  meta_file: string | null
  video_file: string | null
  quality: TakeQuality | null
}

export interface HostState {
  service_online: boolean
  station_id: string
  session_id: string
  batch_id: string | null
  round_id: string | null
  signing_mode: 'rough' | 'precise' | null
  mode_switch_notice: string | null
  recording_status: RecordingStatus
  selected_device_id: string | null
  current_sentence_index: number
  sentences: SentenceItem[]
  current_take: TakeItem | null
  takes: TakeItem[]
  countdown_seconds: number
  started_at_unix_ms: number | null
  guidance_enabled: boolean
  help_requested: boolean
}

export interface CommandResponse {
  action: string
  state: HostState
  command_id: string
  start_at_unix_ms: number | null
}

export interface RecordingBatchesResponse {
  root: string
  batches: string[]
}

export interface RoundInfo {
  station_id: string
  batch_id: string
  round_id: string
  signing_mode: 'rough' | 'precise' | null
  session_id: string
  current_sentence_index: number
  completed_sentences: number
  total_sentences: number
  created_at_unix_ms: number
  updated_at_unix_ms: number
}

export interface RecordingRoundsResponse {
  batch_id: string
  rounds: RoundInfo[]
  suggested_round_id: string
}

export interface SkeletonMessage {
  type: 'skeleton'
  skeleton_type: string
  joint_names: string[]
  parent_indices: number[]
}

export interface FrameMessage {
  type: 'frame'
  sequence: number
  timestamp: number
  confidence: number
  joint_count: number
  positions: number[]
  valid: boolean[]
}

export interface PoseStatusMessage {
  type: 'status'
  pose_valid: boolean
  joint_count: number
  valid_joint_count: number
  confidence: number
}

export type PoseMessage = SkeletonMessage | FrameMessage | PoseStatusMessage

export interface ReviewHandCaptureQuality {
  frames?: number
  clean_ratio?: number
  left_tracked_ratio?: number
  right_tracked_ratio?: number
  left_inside_ratio?: number
  right_inside_ratio?: number
  guidance_enabled?: boolean
}

export interface ReviewMeta {
  session_id?: string
  sentence_id?: string
  sentence_text?: string
  take_id?: string
  take_index?: number
  capture_status?: string
  review_status?: string
  utc_started?: string
  utc_stopped?: string
  pose_frame_count?: number
  device_model?: string
  hand_capture_quality?: ReviewHandCaptureQuality
  [key: string]: unknown
}

export interface ReviewItem {
  id: string
  dataset: string
  teacher: string
  round_id: string
  sentence_id: string
  take_id: string
  sentence_text: string
  meta: ReviewMeta
  pose_file: string | null
  video_file: string | null
  video_issue: boolean
  sentence_issue: boolean
}

export interface ReviewDatasetsResponse {
  root: string
  datasets: string[]
}

export interface ReviewItemsResponse {
  dataset: string
  items: ReviewItem[]
}
