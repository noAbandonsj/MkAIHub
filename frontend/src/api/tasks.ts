import { apiClient } from './client'
import type { TaskInput, TaskListResponse, TaskRead, TaskStatus } from '@/types/task'

export interface TaskListQuery {
  page?: number
  pageSize?: number
  q?: string
  mine?: boolean
  status?: TaskStatus | ''
}

function queryString(query: TaskListQuery): string {
  const params = new URLSearchParams()
  if (query.page) params.set('page', String(query.page))
  if (query.pageSize) params.set('page_size', String(query.pageSize))
  if (query.q?.trim()) params.set('q', query.q.trim())
  if (query.mine) params.set('mine', 'true')
  if (query.status) params.set('status', query.status)
  const value = params.toString()
  return value ? `?${value}` : ''
}

export const tasksApi = {
  list(query: TaskListQuery = {}) {
    return apiClient.get<TaskListResponse>(`/tasks${queryString(query)}`)
  },
  get(id: number) {
    return apiClient.get<TaskRead>(`/tasks/${id}`)
  },
  create(input: TaskInput) {
    return apiClient.post<TaskRead>('/tasks', input)
  },
  update(id: number, input: Partial<TaskInput>) {
    return apiClient.patch<TaskRead>(`/tasks/${id}`, input)
  },
  complete(id: number) {
    return apiClient.post<TaskRead>(`/tasks/${id}/complete`)
  },
  close(id: number) {
    return apiClient.post<TaskRead>(`/tasks/${id}/close`)
  },
}
