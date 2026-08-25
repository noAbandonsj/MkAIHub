import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { competitionsApi } from './competitions'
import { clearApiCsrfToken } from './client'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

describe('competition API client', () => {
  beforeEach(() => {
    clearApiCsrfToken()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('builds the list query without personal filters', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [], page: 2, page_size: 12, total: 0 }))

    await competitionsApi.list({ page: 2, pageSize: 12, q: ' 提示词大赛 ' })

    const url = String(fetchMock.mock.calls[0][0])
    expect(url).toContain('/competitions?')
    expect(url).toContain('page=2')
    expect(url).toContain('page_size=12')
    expect(url).toContain('q=%E6%8F%90%E7%A4%BA%E8%AF%8D%E5%A4%A7%E8%B5%9B')
    expect(url).not.toContain('mine=')
    expect(url).not.toContain('status=')
  })

  it('posts an admin competition with UTC times', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-comp' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 1 }, 201))

    await competitionsApi.create({
      title: '内部提示词大赛',
      summary: '评选最佳内部提示词。',
      rules_markdown: '# 规则',
      start_at: '2998-01-01T00:00:00Z',
      end_at: '2999-01-01T00:00:00Z',
    })

    expect(String(fetchMock.mock.calls[1][0])).toContain('/admin/competitions')
    expect(fetchMock.mock.calls[1][1]?.method).toBe('POST')
  })

  it('registers and cancels through the registration endpoints', async () => {
    const fetchMock = vi.mocked(fetch)
    // Only the first write prefetches the CSRF token; later writes reuse it.
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-reg' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 3, status: 'REGISTERED' }, 201))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 3, status: 'CANCELLED' }))

    await competitionsApi.register(7)
    await competitionsApi.cancelRegistration(7)

    expect(String(fetchMock.mock.calls[1][0])).toContain('/competitions/7/registrations')
    expect(fetchMock.mock.calls[1][1]?.method).toBe('POST')
    expect(String(fetchMock.mock.calls[2][0])).toContain('/competitions/7/registrations/me')
    expect(fetchMock.mock.calls[2][1]?.method).toBe('DELETE')
  })

  it('loads competition tasks and published results', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ competition_id: 7, items: [] }))

    await competitionsApi.listTasks(7)
    await competitionsApi.listResults(7)

    expect(String(fetchMock.mock.calls[0][0])).toContain('/competitions/7/tasks')
    expect(String(fetchMock.mock.calls[1][0])).toContain('/competitions/7/results')
  })

  it('targets the admin update and delete endpoints', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValue(jsonResponse({ csrf_token: 'csrf-comp' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-comp' }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 5 }))
    fetchMock.mockResolvedValueOnce(jsonResponse({ csrf_token: 'csrf-comp' }))
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))

    await competitionsApi.update(5, { title: '更新标题' })
    await competitionsApi.remove(5)

    expect(String(fetchMock.mock.calls[1][0])).toContain('/admin/competitions/5')
    expect(fetchMock.mock.calls[1][1]?.method).toBe('PATCH')
    expect(String(fetchMock.mock.calls[2][0])).toContain('/admin/competitions/5')
    expect(fetchMock.mock.calls[2][1]?.method).toBe('DELETE')
  })
})
