export interface ApiErrorPayload {
  code?: string
  message?: string
  details?: unknown
}

export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly details?: unknown

  constructor(status: number, message: string, payload?: ApiErrorPayload) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = payload?.code
    this.details = payload?.details
  }
}

type RequestOptions = Omit<RequestInit, 'body'> & {
  body?: unknown
}

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL || '/api/v1').replace(/\/$/, '')
const csrfTokenPath = '/auth/csrf-token'
const loginPath = '/auth/login'

let csrfToken: string | null = null
let csrfRefreshPromise: Promise<string> | null = null

async function readPayload(response: Response): Promise<unknown> {
  if (response.status === 204) {
    return undefined
  }

  const contentType = response.headers.get('content-type') || ''
  if (contentType.includes('application/json')) {
    return response.json()
  }

  const text = await response.text()
  return text || undefined
}

function isWriteMethod(method: string): boolean {
  return !['GET', 'HEAD', 'OPTIONS'].includes(method.toUpperCase())
}

async function requestCsrfToken(force = false): Promise<string> {
  if (!force && csrfToken) {
    return csrfToken
  }

  if (csrfRefreshPromise) {
    return csrfRefreshPromise
  }

  csrfRefreshPromise = request<CsrfTokenPayload>(csrfTokenPath, { method: 'GET' })
    .then((payload) => {
      if (!payload?.csrf_token) {
        throw new ApiError(500, 'CSRF Token 响应无效')
      }
      csrfToken = payload.csrf_token
      return csrfToken
    })
    .finally(() => {
      csrfRefreshPromise = null
    })

  return csrfRefreshPromise
}

interface CsrfTokenPayload {
  csrf_token?: string
}

async function request<T>(
  path: string,
  options: RequestOptions = {},
): Promise<T> {
  const { body, headers, ...requestInit } = options
  const method = String(requestInit.method || 'GET').toUpperCase()
  const shouldAttachCsrf = isWriteMethod(method) && path !== loginPath
  const isFormDataBody = typeof FormData !== 'undefined' && body instanceof FormData

  const resolvedCsrfToken = shouldAttachCsrf ? await requestCsrfToken() : null
  const requestHeaders = new Headers(headers)
  requestHeaders.set('Accept', 'application/json')
  if (body !== undefined && !isFormDataBody && !requestHeaders.has('Content-Type')) {
    requestHeaders.set('Content-Type', 'application/json')
  }
  if (resolvedCsrfToken) {
    requestHeaders.set('X-CSRF-Token', resolvedCsrfToken)
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...requestInit,
    method,
    credentials: 'include',
    headers: requestHeaders,
    body: body === undefined
      ? undefined
      : isFormDataBody
        ? body as FormData
        : JSON.stringify(body),
  })

  const payload = await readPayload(response)
  if (!response.ok) {
    const errorPayload = typeof payload === 'object' && payload !== null
      ? payload as ApiErrorPayload
      : undefined
    const message = errorPayload?.message || response.statusText || '请求失败'
    const error = new ApiError(response.status, message, errorPayload)
    if (response.status === 401) {
      clearApiCsrfToken()
    }
    throw error
  }

  return payload as T
}

export function clearApiCsrfToken(): void {
  csrfToken = null
}

export async function refreshApiCsrfToken(): Promise<string> {
  clearApiCsrfToken()
  return requestCsrfToken(true)
}

export function getApiErrorMessage(error: unknown, fallback = '请求失败'): string {
  if (error instanceof ApiError) {
    return error.message || fallback
  }
  if (error instanceof Error) {
    return error.message || fallback
  }
  return fallback
}

export function resolveApiUrl(path: string): string {
  return `${apiBaseUrl}${path}`
}

export const apiClient = {
  get<T>(path: string, options?: Omit<RequestOptions, 'method' | 'body'>) {
    return request<T>(path, { ...options, method: 'GET' })
  },
  post<T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>) {
    return request<T>(path, { ...options, method: 'POST', body })
  },
  patch<T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>) {
    return request<T>(path, { ...options, method: 'PATCH', body })
  },
  delete<T>(path: string, options?: Omit<RequestOptions, 'method' | 'body'>) {
    return request<T>(path, { ...options, method: 'DELETE' })
  },
}
