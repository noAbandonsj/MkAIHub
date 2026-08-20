import { describe, expect, it } from 'vitest'

import type { IssueStatus } from '@/types/issue'
import type { TaskStatus } from '@/types/task'

import {
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
