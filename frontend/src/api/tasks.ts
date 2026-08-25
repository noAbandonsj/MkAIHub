import { apiClient } from './client'
import type {
  TaskInput,
  TaskListResponse,
  TaskParticipant,
  TaskParticipantListResponse,
  TaskRead,
  TaskStatus,
  TaskSubmission,
  TaskSubmissionInput,
  TaskSubmissionListResponse,
} from '@/types/task'

export interface TaskListQuery {
  page?: number
  pageSize?: number
  q?: string
  mine?: boolean
  participated?: boolean
  pendingReview?: boolean
  competitionOnly?: boolean
  pendingCompetitionReview?: boolean
  status?: TaskStatus | ''
}

function queryString(query: TaskListQuery): string {
  const params = new URLSearchParams()
  if (query.page) params.set('page', String(query.page))
  if (query.pageSize) params.set('page_size', String(query.pageSize))
  if (query.q?.trim()) params.set('q', query.q.trim())
  if (query.mine) params.set('mine', 'true')
  if (query.participated) params.set('participated', 'true')
  if (query.pendingReview) params.set('pending_review', 'true')
  if (query.competitionOnly) params.set('competition_only', 'true')
  if (query.pendingCompetitionReview) params.set('pending_competition_review', 'true')
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
  join(id: number) {
    return apiClient.post<TaskParticipant>(`/tasks/${id}/participants`)
  },
  leave(id: number) {
    return apiClient.delete<TaskParticipant>(`/tasks/${id}/participants/me`)
  },
  listParticipants(id: number) {
    return apiClient.get<TaskParticipantListResponse>(`/tasks/${id}/participants`)
  },
  listSubmissions(id: number, page = 1, pageSize = 100) {
    return apiClient.get<TaskSubmissionListResponse>(
      `/tasks/${id}/submissions?page=${page}&page_size=${pageSize}`,
    )
  },
  submit(id: number, input: TaskSubmissionInput) {
    return apiClient.post<TaskSubmission>(`/tasks/${id}/submissions`, input)
  },
}
