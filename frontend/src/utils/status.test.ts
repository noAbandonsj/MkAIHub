import { describe, expect, it } from 'vitest'

import type { CompetitionLifecycle, CompetitionStatus, RegistrationStatus } from '@/types/competition'
import type { IssueStatus } from '@/types/issue'
import type { TaskStatus } from '@/types/task'

import {
  competitionLifecycleLabels,
  competitionLifecycleTones,
  competitionStatusLabels,
  competitionStatusTones,
  registrationStatusLabels,
  issueStatusLabels,
  issueStatusTones,
  taskStatusLabels,
  taskStatusTones,
} from './status'

describe('task status mappings', () => {
  it('labels every task status', () => {
    expect(taskStatusLabels.OPEN).toBe('开放')
    expect(taskStatusLabels.COMPLETED).toBe('已完成')
    expect(taskStatusLabels.CLOSED).toBe('已关闭')
  })

  it('maps every task status to a badge tone', () => {
    const statuses: TaskStatus[] = ['OPEN', 'COMPLETED', 'CLOSED']
    for (const status of statuses) {
      expect(['open', 'done', 'closed']).toContain(taskStatusTones[status])
    }
    expect(taskStatusTones.COMPLETED).toBe('done')
    expect(taskStatusTones.CLOSED).toBe('closed')
  })
})

describe('issue status mappings', () => {
  it('labels every issue status', () => {
    expect(issueStatusLabels.OPEN).toBe('开放')
    expect(issueStatusLabels.CLOSED).toBe('已关闭')
  })

  it('maps every issue status to a badge tone', () => {
    const statuses: IssueStatus[] = ['OPEN', 'CLOSED']
    for (const status of statuses) {
      expect(['open', 'closed']).toContain(issueStatusTones[status])
    }
  })
})

describe('competition status mappings', () => {
  it('labels every computed competition status', () => {
    expect(competitionStatusLabels.UPCOMING).toBe('即将开始')
    expect(competitionStatusLabels.ONGOING).toBe('进行中')
    expect(competitionStatusLabels.ENDED).toBe('已结束')
  })

  it('maps every competition status to a badge tone', () => {
    const statuses: CompetitionStatus[] = ['UPCOMING', 'ONGOING', 'ENDED']
    for (const status of statuses) {
      expect(['open', 'done', 'closed']).toContain(competitionStatusTones[status])
    }
    expect(competitionStatusTones.ENDED).toBe('closed')
  })
})

describe('competition lifecycle mappings', () => {
  it('labels every lifecycle status', () => {
    expect(competitionLifecycleLabels.DRAFT).toBe('草稿')
    expect(competitionLifecycleLabels.PUBLISHED).toBe('已发布')
    expect(competitionLifecycleLabels.RESULT_PUBLISHED).toBe('结果已发布')
    expect(competitionLifecycleLabels.ARCHIVED).toBe('已归档')
  })

  it('maps every lifecycle status to a badge tone', () => {
    const statuses: CompetitionLifecycle[] = ['DRAFT', 'PUBLISHED', 'RESULT_PUBLISHED', 'ARCHIVED']
    for (const status of statuses) {
      expect(['open', 'pending', 'done', 'closed']).toContain(competitionLifecycleTones[status])
    }
    expect(competitionLifecycleTones.RESULT_PUBLISHED).toBe('done')
  })
})

describe('registration status mappings', () => {
  it('labels every registration status', () => {
    const statuses: RegistrationStatus[] = ['REGISTERED', 'CANCELLED']
    expect(registrationStatusLabels.REGISTERED).toBe('已报名')
    expect(registrationStatusLabels.CANCELLED).toBe('已取消')
  })
})
