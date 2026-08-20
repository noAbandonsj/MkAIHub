import type { CompetitionStatus } from '@/types/competition'
import type { IssueStatus } from '@/types/issue'
import type { TaskStatus } from '@/types/task'

export type StatusTone = 'open' | 'done' | 'closed'

export const taskStatusLabels: Record<TaskStatus, string> = {
  OPEN: '开放',
  COMPLETED: '已完成',
  CLOSED: '已关闭',
}

export const taskStatusTones: Record<TaskStatus, StatusTone> = {
  OPEN: 'open',
  COMPLETED: 'done',
  CLOSED: 'closed',
}

export const issueStatusLabels: Record<IssueStatus, string> = {
  OPEN: '开放',
  CLOSED: '已关闭',
}

export const issueStatusTones: Record<IssueStatus, StatusTone> = {
  OPEN: 'open',
  CLOSED: 'closed',
}

export const competitionStatusLabels: Record<CompetitionStatus, string> = {
  UPCOMING: '即将开始',
  ONGOING: '进行中',
  ENDED: '已结束',
}

export const competitionStatusTones: Record<CompetitionStatus, StatusTone> = {
  UPCOMING: 'open',
  ONGOING: 'done',
  ENDED: 'closed',
}
