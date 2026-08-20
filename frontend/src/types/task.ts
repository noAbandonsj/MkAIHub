import type { UserSummary } from '@/types/artifact'

export type TaskStatus = 'OPEN' | 'COMPLETED' | 'CLOSED'

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

export interface TaskRead extends TaskListItem {
  description: string
}

export interface TaskListResponse {
  items: TaskListItem[]
  page: number
  page_size: number
  total: number
}

export interface TaskInput {
  title: string
  description: string
  deadline_at: string | null
}
