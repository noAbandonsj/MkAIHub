import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

const authApiMock = vi.hoisted(() => ({
  login: vi.fn(),
  logout: vi.fn(),
  me: vi.fn(),
  changePassword: vi.fn(),
  csrfToken: vi.fn(),
}))

vi.mock('@/api/auth', () => ({ authApi: authApiMock }))

import { useSessionStore } from './session'

const user = {
  id: 1,
  username: 'admin',
  display_name: '系统管理员',
  role: 'SYSTEM_ADMIN' as const,
  is_active: true,
  last_login_at: null,
  created_at: '2026-08-19T00:00:00Z',
  updated_at: '2026-08-19T00:00:00Z',
}

describe('session store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    authApiMock.me.mockResolvedValue(user)
    authApiMock.login.mockResolvedValue({ user })
    authApiMock.logout.mockResolvedValue(undefined)
    authApiMock.changePassword.mockResolvedValue(undefined)
  })

  it('restores the current user only once during initialization', async () => {
    const store = useSessionStore()

    await Promise.all([store.initialize(), store.initialize()])
    await store.initialize()

    expect(authApiMock.me).toHaveBeenCalledOnce()
    expect(store.currentUser).toEqual(user)
    expect(store.isAuthenticated).toBe(true)
    expect(store.isAdmin).toBe(true)
    expect(store.isLoading).toBe(false)
  })

  it('logs in and clears the in-memory session on logout', async () => {
    const store = useSessionStore()

    await store.login({ username: 'admin', password: 'password' })
    expect(store.currentUser).toEqual(user)

    await store.logout()
    expect(authApiMock.logout).toHaveBeenCalledOnce()
    expect(store.currentUser).toBeNull()
    expect(store.isAuthenticated).toBe(false)
    expect(store.isLoading).toBe(false)
  })

  it('clears the session after a successful password change', async () => {
    const store = useSessionStore()
    store.setCurrentUser(user)

    await store.changePassword('old-password', 'new-password')

    expect(authApiMock.changePassword).toHaveBeenCalledWith({
      current_password: 'old-password',
      new_password: 'new-password',
    })
    expect(store.currentUser).toBeNull()
    expect(store.isLoading).toBe(false)
  })
})
