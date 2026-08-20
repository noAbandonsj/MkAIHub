import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { tasksApi } from './tasks'
import { clearApiCsrfToken } from './client'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

describe('task API client', () => {
  beforeEach(() => {
    clearApiCsrfToken()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('builds the list query with filters', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [], page: 2, page_size: 12, total: 0 }))

    await tasksApi.list({ page: 2, pageSize: 12, q: ' 提示词 ', mine: true, status: 'OPEN' })

    const url = String(fetchMock.mock.calls[0][0])
    expect(url).toContain('/tasks?')
    expect(url).toContain('page=2')
    expect(url).toContain('page_size=12')
    expect(url).toContain('q=%E6%8F%90%E7%A4%BA%E8%AF%8D')
    expect(url).toContain('mine=true')
    expect(url).toContain('status=OPEN')
  })

  it('sends a UTC deadline when creating a task', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-task' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 1 }, 201))

    await tasksApi.create({
      title: '整理提示词',
      description: '正文',
      deadline_at: '2026-09-01T12:00:00Z',
    })

    const [, init] = fetchMock.mock.calls[1]
    expect(init?.method).toBe('POST')
    expect(String(fetchMock.mock.calls[1][0])).toContain('/tasks')
    expect(JSON.parse(String(init?.body))).toEqual({
      title: '整理提示词',
      description: '正文',
      deadline_at: '2026-09-01T12:00:00Z',
    })
  })

  it('targets the state action endpoints', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValue(jsonResponse({ csrf_token: 'csrf-task' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-task' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 3, status: 'COMPLETED' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 3, status: 'CLOSED' }))

    await tasksApi.complete(3)
    await tasksApi.close(3)

    expect(String(fetchMock.mock.calls[1][0])).toContain('/tasks/3/complete')
    expect(String(fetchMock.mock.calls[2][0])).toContain('/tasks/3/close')
    expect(fetchMock.mock.calls[1][1]?.method).toBe('POST')
    expect(fetchMock.mock.calls[2][1]?.method).toBe('POST')
  })
})
