import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { issuesApi } from './issues'
import { clearApiCsrfToken } from './client'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

describe('issue API client', () => {
  beforeEach(() => {
    clearApiCsrfToken()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('builds the list query with filters', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [], page: 1, page_size: 12, total: 0 }))

    await issuesApi.list({ q: ' 导出 ', mine: true, status: 'CLOSED' })

    const url = String(fetchMock.mock.calls[0][0])
    expect(url).toContain('/issues?')
    expect(url).toContain('mine=true')
    expect(url).toContain('status=CLOSED')
  })

  it('targets close and reopen actions', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValue(jsonResponse({ csrf_token: 'csrf-issue' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-issue' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 5, status: 'CLOSED' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 5, status: 'OPEN' }))

    await issuesApi.close(5)
    await issuesApi.reopen(5)

    expect(String(fetchMock.mock.calls[1][0])).toContain('/issues/5/close')
    expect(String(fetchMock.mock.calls[2][0])).toContain('/issues/5/reopen')
  })

  it('posts and lists issue comments', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValue(jsonResponse({ csrf_token: 'csrf-issue' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-issue' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 9 }, 201))
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [], page: 1, page_size: 100, total: 0 }))

    await issuesApi.createComment(5, '支持这个建议。')
    await issuesApi.listComments(5)

    expect(String(fetchMock.mock.calls[1][0])).toContain('/issues/5/comments')
    expect(JSON.parse(String(fetchMock.mock.calls[1][1]?.body))).toEqual({ content: '支持这个建议。' })
    expect(String(fetchMock.mock.calls[2][0])).toContain('/issues/5/comments?page_size=100')
  })
})
