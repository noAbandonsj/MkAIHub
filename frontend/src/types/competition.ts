import type { UserSummary } from '@/types/artifact'
import type { SubmissionStatus, TaskStatus } from '@/types/task'

export type CompetitionStatus = 'UPCOMING' | 'ONGOING' | 'ENDED'

export type CompetitionLifecycle = 'DRAFT' | 'PUBLISHED' | 'RESULT_PUBLISHED' | 'ARCHIVED'

export type RegistrationStatus = 'REGISTERED' | 'CANCELLED'

export interface CompetitionListItem {
  id: number
  title: string
  summary: string
  creator: UserSummary
  status: CompetitionStatus
  lifecycle_status: CompetitionLifecycle
  start_at: string
  end_at: string
  created_at: string
  updated_at: string
}

export interface CompetitionRegistrationSummary {
  id: number
  status: RegistrationStatus
  registered_at: string
  cancelled_at?: string | null
}

export interface CompetitionRead extends CompetitionListItem {
  rules_markdown: string
  task_count: number
  registration_count: number
  my_registration: CompetitionRegistrationSummary | null
  results_published: boolean
}

export interface CompetitionListResponse {
  items: CompetitionListItem[]
  page: number
  page_size: number
  total: number
}

export interface CompetitionRegistrationRead {
  id: number
  competition_id: number
  user: UserSummary
  status: RegistrationStatus
  registered_at: string
  cancelled_at?: string | null
}

export interface CompetitionRegistrationListResponse {
  items: CompetitionRegistrationRead[]
}

export interface CompetitionTaskSubmissionSummary {
  id: number
  artifact_id: number
  artifact_title: string
  round_no: number
  status: SubmissionStatus
  is_current: boolean
  submitted_at: string
}

export interface CompetitionTask {
  id: number
  title: string
  description: string
  creator: UserSummary
  status: TaskStatus
  required: boolean
  sort_order: number
  max_score: string
  weight: string
  deadline_at: string | null
  effective_deadline_at: string
  my_submission: CompetitionTaskSubmissionSummary | null
  current_submission_count?: number | null
  reviewed_count?: number | null
  created_at: string
  updated_at: string
}

export interface CompetitionTaskListResponse {
  items: CompetitionTask[]
}

export interface CompetitionResultRow {
  registration_id: number
  user: UserSummary
  total_score: string
  rank: number
  award: string | null
}

export interface CompetitionResults {
  competition_id: number
  published_by: UserSummary | null
  published_at: string | null
  items: CompetitionResultRow[]
}

export interface CompetitionInput {
  title: string
  summary: string
  rules_markdown: string
  start_at: string
  end_at: string
}

export interface CompetitionTaskInput {
  title: string
  description: string
  deadline_at: string | null
  required: boolean
  sort_order: number
  max_score: string
  weight: string
}

export interface CompetitionAwardInput {
  registration_id: number
  award: string
}
