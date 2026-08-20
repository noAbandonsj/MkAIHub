import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { adminApi } from './admin'
import { clearApiCsrfToken } from './client'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

describe('admin API client', () => {
  beforeEach(() => {
    clearApiCsrfToken()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('targets the comment moderation endpoints', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValue(jsonResponse({ csrf_token: 'csrf-admin' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-admin' }))
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-admin' }))
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))

    await adminApi.hideComment(9)
    await adminApi.restoreComment(9)

    expect(String(fetchMock.mock.calls[1][0])).toContain('/admin/comments/9/hide')
    expect(fetchMock.mock.calls[1][1]?.method).toBe('POST')
    expect(String(fetchMock.mock.calls[2][0])).toContain('/admin/comments/9/restore')
    expect(fetchMock.mock.calls[2][1]?.method).toBe('POST')
  })
})
