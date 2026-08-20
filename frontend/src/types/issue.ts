import type { UserSummary } from '@/types/artifact'

export type IssueStatus = 'OPEN' | 'CLOSED'

export interface IssueListItem {
  id: number
  title: string
  author: UserSummary
  status: IssueStatus
  closed_at?: string | null
  created_at: string
  updated_at: string
}

export interface IssueRead extends IssueListItem {
  description: string
}

export interface IssueListResponse {
  items: IssueListItem[]
  page: number
  page_size: number
  total: number
}

export interface IssueInput {
  title: string
  description: string
}
