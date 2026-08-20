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
