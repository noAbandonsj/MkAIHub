import type { CompetitionLifecycle, CompetitionStatus, RegistrationStatus } from '@/types/competition'
import type { IssueStatus } from '@/types/issue'
import type { ParticipantStatus, SubmissionStatus, TaskStatus } from '@/types/task'

export type StatusTone = 'open' | 'pending' | 'done' | 'closed'

export const taskStatusLabels: Record<TaskStatus, string> = {
  OPEN: '开放',
  IN_PROGRESS: '进行中',
  REVIEWING: '待验收',
  COMPLETED: '已完成',
  CLOSED: '已关闭',
}

export const taskStatusTones: Record<TaskStatus, StatusTone> = {
  OPEN: 'open',
  IN_PROGRESS: 'open',
  REVIEWING: 'pending',
  COMPLETED: 'done',
  CLOSED: 'closed',
}

export const participantStatusLabels: Record<ParticipantStatus, string> = {
  ACTIVE: '参与中',
  LEFT: '已退出',
}

export const submissionStatusLabels: Record<SubmissionStatus, string> = {
  SUBMITTED: '已提交',
  REVISION_REQUIRED: '需修改',
  ACCEPTED: '已验收',
  REJECTED: '未采用',
}

export const submissionStatusTones: Record<SubmissionStatus, StatusTone> = {
  SUBMITTED: 'pending',
  REVISION_REQUIRED: 'open',
  ACCEPTED: 'done',
  REJECTED: 'closed',
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

export const competitionLifecycleLabels: Record<CompetitionLifecycle, string> = {
  DRAFT: '草稿',
  PUBLISHED: '已发布',
  RESULT_PUBLISHED: '结果已发布',
  ARCHIVED: '已归档',
}

export const competitionLifecycleTones: Record<CompetitionLifecycle, StatusTone> = {
  DRAFT: 'pending',
  PUBLISHED: 'open',
  RESULT_PUBLISHED: 'done',
  ARCHIVED: 'closed',
}

export const registrationStatusLabels: Record<RegistrationStatus, string> = {
  REGISTERED: '已报名',
  CANCELLED: '已取消',
}
