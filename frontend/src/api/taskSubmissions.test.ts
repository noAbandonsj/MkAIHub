import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { taskSubmissionsApi } from './taskSubmissions'
import { clearApiCsrfToken } from './client'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

describe('task submission API client', () => {
  beforeEach(() => {
    clearApiCsrfToken()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('targets the three decision endpoints with notes', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-decide' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 4, status: 'REVISION_REQUIRED' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 4, status: 'ACCEPTED' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 4, status: 'REJECTED' }))

    await taskSubmissionsApi.requestRevision(4, '请补充说明')
    await taskSubmissionsApi.accept(4, '验收通过')
    await taskSubmissionsApi.reject(4, '本次不采用')

    expect(String(fetchMock.mock.calls[1][0])).toContain('/task-submissions/4/request-revision')
    expect(JSON.parse(String(fetchMock.mock.calls[1][1]?.body))).toEqual({ note: '请补充说明' })
    expect(String(fetchMock.mock.calls[2][0])).toContain('/task-submissions/4/accept')
    expect(JSON.parse(String(fetchMock.mock.calls[2][1]?.body))).toEqual({ note: '验收通过' })
    expect(String(fetchMock.mock.calls[3][0])).toContain('/task-submissions/4/reject')
    expect(JSON.parse(String(fetchMock.mock.calls[3][1]?.body))).toEqual({ note: '本次不采用' })
    expect(fetchMock.mock.calls[1][1]?.method).toBe('POST')
    expect(fetchMock.mock.calls[2][1]?.method).toBe('POST')
    expect(fetchMock.mock.calls[3][1]?.method).toBe('POST')
  })

  it('omits the accept note when none is provided', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-decide' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 4, status: 'ACCEPTED' }))

    await taskSubmissionsApi.accept(4)

    expect(JSON.parse(String(fetchMock.mock.calls[1][1]?.body))).toEqual({ note: null })
  })
})
