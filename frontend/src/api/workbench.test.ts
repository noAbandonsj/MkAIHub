import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { workbenchApi } from './workbench'
import { clearApiCsrfToken } from './client'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

describe('workbench API client', () => {
  beforeEach(() => {
    clearApiCsrfToken()
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('loads personal counts and closure statistics', async () => {
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockResolvedValueOnce(
      jsonResponse({
        counts: {
          participated_tasks: 2,
          competition_tasks: 1,
          pending_task_reviews: 1,
          pending_competition_reviews: 0,
        },
        statistics: {
          participations: 2,
          submissions: 2,
          accepted_submissions: 1,
          competition_task_completions: 1,
          published_results: 0,
        },
      }),
    )

    const response = await workbenchApi.get()

    expect(response.counts.participated_tasks).toBe(2)
    expect(String(fetchMock.mock.calls[0][0])).toContain('/workbench')
  })
})
