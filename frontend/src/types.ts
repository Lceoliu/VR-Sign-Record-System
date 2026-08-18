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
  last_seen_unix_ms: number
  preview_frames: number
  pose_packets: number
}

export interface SentenceItem {
  sentence_id: string
  index: number
  text: string
  status: 'completed' | 'current' | 'pending'
}

export interface TakeItem {
  take_id: string
  take_index: number
  status: 'recording' | 'candidate' | 'complete'
  pose_file: string | null
  meta_file: string | null
  video_file: string | null
}

export interface HostState {
  service_online: boolean
  session_id: string
  recording_status: RecordingStatus
  selected_device_id: string | null
  current_sentence_index: number
  sentences: SentenceItem[]
  current_take: TakeItem | null
  takes: TakeItem[]
  countdown_seconds: number
  started_at_unix_ms: number | null
}

export interface CommandResponse {
  action: string
  state: HostState
  command_id: string
  start_at_unix_ms: number | null
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

