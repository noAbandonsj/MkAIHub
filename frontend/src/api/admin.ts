import { apiClient } from './client'
import type { TaskRead } from '@/types/task'
import type { UserRead, UserRole } from '@/types/user'

export interface UserListParams {
  page: number
  page_size: number
  q?: string
}

export interface UserListResponse {
  items: UserRead[]
  page: number
  page_size: number
  total: number
}

export interface CreateUserInput {
  username: string
  display_name: string
  password: string
  role: UserRole
}

export interface UpdateUserInput {
  display_name?: string
  role?: UserRole
  is_active?: boolean
}

export interface ResetPasswordInput {
  new_password: string
}

function buildUserQuery(params: UserListParams): string {
  const query = new URLSearchParams({
    page: String(params.page),
    page_size: String(params.page_size),
  })

  const keyword = params.q?.trim()
  if (keyword) {
    query.set('q', keyword)
  }

  return `?${query.toString()}`
}

export const adminApi = {
  listUsers(params: UserListParams) {
    return apiClient.get<UserListResponse>(`/admin/users${buildUserQuery(params)}`)
  },

  createUser(input: CreateUserInput) {
    return apiClient.post<UserRead>('/admin/users', input)
  },

  updateUser(id: number, input: UpdateUserInput) {
    return apiClient.patch<UserRead>(`/admin/users/${id}`, input)
  },

  resetPassword(id: number, input: ResetPasswordInput) {
    return apiClient.post<void>(`/admin/users/${id}/reset-password`, input)
  },

  hideComment(commentId: number) {
    return apiClient.post<void>(`/admin/comments/${commentId}/hide`)
  },

  restoreComment(commentId: number) {
    return apiClient.post<void>(`/admin/comments/${commentId}/restore`)
  },

  reopenTask(taskId: number) {
    return apiClient.post<TaskRead>(`/admin/tasks/${taskId}/reopen`)
  },
}

export type AdminApi = typeof adminApi
