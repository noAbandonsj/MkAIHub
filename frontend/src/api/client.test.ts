import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { apiClient, clearApiCsrfToken } from './client'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

describe('api client authentication boundaries', () => {
  beforeEach(() => {
    clearApiCsrfToken()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('does not request or attach CSRF for login', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ user: {} }))

    await apiClient.post('/auth/login', { username: 'employee', password: 'password' })

    expect(fetchMock).toHaveBeenCalledOnce()
    const [, init] = fetchMock.mock.calls[0]
    expect((init?.headers as Headers).get('X-CSRF-Token')).toBeNull()
    expect(init?.credentials).toBe('include')
  })

  it('gets an in-memory CSRF token before an authenticated write', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-1' }))
      .mockResolvedValueOnce(jsonResponse(undefined, 204))

    await apiClient.post('/auth/logout')

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(String(fetchMock.mock.calls[0][0])).toContain('/auth/csrf-token')
    const [, init] = fetchMock.mock.calls[1]
    expect((init?.headers as Headers).get('X-CSRF-Token')).toBe('csrf-1')
  })

  it('surfaces a forbidden write without retrying or clearing the session', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-2' }))
      .mockResolvedValueOnce(jsonResponse({ code: 'forbidden', message: '无权限' }, 403))

    await expect(apiClient.post('/admin/users', {})).rejects.toMatchObject({
      status: 403,
      code: 'forbidden',
    })
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })
})
