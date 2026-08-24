import type { UserSummary } from '@/types/artifact'

export type TaskStatus = 'OPEN' | 'IN_PROGRESS' | 'REVIEWING' | 'COMPLETED' | 'CLOSED'

export type ParticipantStatus = 'ACTIVE' | 'LEFT'

export type SubmissionStatus = 'SUBMITTED' | 'REVISION_REQUIRED' | 'ACCEPTED' | 'REJECTED'

export interface TaskListItem {
  id: number
  title: string
  creator: UserSummary
  status: TaskStatus
  deadline_at?: string | null
  completed_at?: string | null
  closed_at?: string | null
  created_at: string
  updated_at: string
}

export interface TaskParticipant {
  id: number
  task_id: number
  user: UserSummary
  status: ParticipantStatus
  joined_at: string
  left_at?: string | null
}

export interface TaskRead extends TaskListItem {
  description: string
  my_participation?: TaskParticipant | null
  participant_count: number
  submission_count: number
}

export interface TaskListResponse {
  items: TaskListItem[]
  page: number
  page_size: number
  total: number
}

export interface TaskParticipantListResponse {
  items: TaskParticipant[]
}

export interface SubmissionArtifactSummary {
  id: number
  title: string
  status: string
}

export interface SubmissionTaskSummary {
  id: number
  title: string
  status: TaskStatus
}

export interface TaskSubmission {
  id: number
  task_id: number
  participant_id: number
  participant: UserSummary
  artifact: SubmissionArtifactSummary
  round_no: number
  note?: string | null
  status: SubmissionStatus
  is_current: boolean
  submitted_at: string
  revision_requested_at?: string | null
  decided_at?: string | null
  decider?: UserSummary | null
  decision_note?: string | null
  task?: SubmissionTaskSummary | null
}

export interface TaskSubmissionListResponse {
  items: TaskSubmission[]
  page: number
  page_size: number
  total: number
}

export interface ArtifactTaskSourceListResponse {
  items: TaskSubmission[]
}

export interface TaskInput {
  title: string
  description: string
  deadline_at: string | null
}

export interface TaskSubmissionInput {
  artifact_id: number
  note?: string | null
}
