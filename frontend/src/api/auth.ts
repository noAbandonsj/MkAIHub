import { apiClient, clearApiCsrfToken } from './client'
import type { UserRead } from '@/types/user'

export interface LoginInput {
  username: string
  password: string
}

export interface LoginResponse {
  user: UserRead
}

export interface CsrfTokenResponse {
  csrf_token: string
}

export interface ChangePasswordInput {
  current_password: string
  new_password: string
}

export const authApi = {
  login(input: LoginInput) {
    // A token from a previous in-memory session must never be reused for a
    // newly established cookie session.
    clearApiCsrfToken()
    return apiClient.post<LoginResponse>('/auth/login', input)
  },

  logout() {
    return apiClient.post<void>('/auth/logout')
  },

  me() {
    return apiClient.get<UserRead>('/auth/me')
  },

  changePassword(input: ChangePasswordInput) {
    return apiClient.post<void>('/auth/change-password', input)
  },

  csrfToken() {
    return apiClient.get<CsrfTokenResponse>('/auth/csrf-token')
  },
}

export type AuthApi = typeof authApi
