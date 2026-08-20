import type { UserSummary } from '@/types/artifact'

export type CompetitionStatus = 'UPCOMING' | 'ONGOING' | 'ENDED'

export interface CompetitionListItem {
  id: number
  title: string
  summary: string
  creator: UserSummary
  status: CompetitionStatus
  start_at: string
  end_at: string
  created_at: string
  updated_at: string
}

export interface CompetitionRead extends CompetitionListItem {
  rules_markdown: string
}

export interface CompetitionListResponse {
  items: CompetitionListItem[]
  page: number
  page_size: number
  total: number
}

export interface CompetitionInput {
  title: string
  summary: string
  rules_markdown: string
  start_at: string
  end_at: string
}
