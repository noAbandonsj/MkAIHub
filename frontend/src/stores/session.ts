import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

import { authApi } from '@/api/auth'
import { ApiError, clearApiCsrfToken, getApiErrorMessage } from '@/api/client'
import type { LoginInput } from '@/api/auth'
import type { UserRead, UserRole } from '@/types/user'

export const useSessionStore = defineStore('session', () => {
  const currentUser = ref<UserRead | null>(null)
  const isInitialized = ref(false)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  let initializationPromise: Promise<UserRead | null> | null = null

  const isAuthenticated = computed(() => currentUser.value !== null)
  const role = computed<UserRole | null>(() => currentUser.value?.role ?? null)
  const isAdmin = computed(() => role.value === 'SYSTEM_ADMIN')

  function setCurrentUser(user: UserRead | null): void {
    currentUser.value = user
  }

  function markInitialized(): void {
    isInitialized.value = true
  }

  function clearError(): void {
    error.value = null
  }

  function clearSession(): void {
    currentUser.value = null
    clearApiCsrfToken()
    isInitialized.value = true
  }

  async function initialize(): Promise<UserRead | null> {
    if (isInitialized.value) {
      return currentUser.value
    }

    if (initializationPromise) {
      return initializationPromise
    }

    initializationPromise = (async () => {
      isLoading.value = true
      clearError()
      try {
        currentUser.value = await authApi.me()
        return currentUser.value
      } catch (caughtError) {
        // A missing/expired cookie is the normal anonymous startup state. It
        // is not shown as an error and must not trigger a redirect loop on
        // /login.
        if (!(caughtError instanceof ApiError && caughtError.status === 401)) {
          error.value = getApiErrorMessage(caughtError, '无法恢复登录会话')
        }
        clearSession()
        return null
      } finally {
        isInitialized.value = true
        isLoading.value = false
      }
    })()

    try {
      return await initializationPromise
    } finally {
      initializationPromise = null
    }
  }

  async function login(input: LoginInput): Promise<UserRead> {
    isLoading.value = true
    clearError()
    try {
      const response = await authApi.login(input)
      currentUser.value = response.user
      isInitialized.value = true
      return response.user
    } catch (caughtError) {
      error.value = getApiErrorMessage(caughtError, '登录失败')
      clearSession()
      throw caughtError
    } finally {
      isLoading.value = false
    }
  }

  async function logout(): Promise<void> {
    isLoading.value = true
    clearError()
    try {
      await authApi.logout()
    } catch (caughtError) {
      // Local state is cleared even when the server already considers the
      // session gone. This makes logout idempotent and prevents stale access.
      if (!(caughtError instanceof ApiError && caughtError.status === 401)) {
        error.value = getApiErrorMessage(caughtError, '退出失败')
        throw caughtError
      }
    } finally {
      clearSession()
      isLoading.value = false
    }
  }

  async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
    isLoading.value = true
    clearError()
    try {
      await authApi.changePassword({
        current_password: currentPassword,
        new_password: newPassword,
      })
      // The API revokes the current session after a successful change.
      clearSession()
    } catch (caughtError) {
      error.value = getApiErrorMessage(caughtError, '修改密码失败')
      throw caughtError
    } finally {
      isLoading.value = false
    }
  }

  return {
    currentUser,
    isInitialized,
    isLoading,
    error,
    isAuthenticated,
    role,
    isAdmin,
    initialize,
    login,
    logout,
    changePassword,
    setCurrentUser,
    markInitialized,
    clearError,
    clearSession,
  }
})
