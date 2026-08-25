import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { artifactsApi } from './artifacts'
import { clearApiCsrfToken } from './client'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

describe('artifact API client', () => {
  beforeEach(() => {
    clearApiCsrfToken()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('builds the lightweight list query', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [], page: 2, page_size: 12, total: 0 }))

    await artifactsApi.list({
      page: 2,
      pageSize: 12,
      q: ' 提示词 ',
      mine: true,
      status: 'DRAFT',
      source: 'TASK_RESULT',
    })

    const url = String(fetchMock.mock.calls[0][0])
    expect(url).toContain('/artifacts?')
    expect(url).toContain('page=2')
    expect(url).toContain('page_size=12')
    expect(url).toContain('q=%E6%8F%90%E7%A4%BA%E8%AF%8D')
    expect(url).toContain('mine=true')
    expect(url).toContain('status=DRAFT')
    expect(url).toContain('source=TASK_RESULT')
  })

  it('uploads multipart data without forcing a JSON content type', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-upload' }))
      .mockResolvedValueOnce(jsonResponse({ id: 1 }, 201))

    await artifactsApi.upload(new File(['hello'], 'guide.md', { type: 'text/markdown' }))

    expect(fetchMock).toHaveBeenCalledTimes(2)
    const [, init] = fetchMock.mock.calls[1]
    expect(init?.body).toBeInstanceOf(FormData)
    expect((init?.headers as Headers).has('Content-Type')).toBe(false)
    expect((init?.headers as Headers).get('X-CSRF-Token')).toBe('csrf-upload')
  })

  it('lists the task submission sources of one artifact', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }))

    await artifactsApi.listTaskSources(12)

    expect(String(fetchMock.mock.calls[0][0])).toContain('/artifacts/12/task-submissions')
  })
})
