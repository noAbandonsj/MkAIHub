import { apiClient } from './client'
import type {
  CompetitionInput,
  CompetitionListResponse,
  CompetitionRead,
  CompetitionRegistrationListResponse,
  CompetitionRegistrationRead,
  CompetitionRegistrationSummary,
  CompetitionResults,
  CompetitionTaskListResponse,
} from '@/types/competition'

export interface CompetitionListQuery {
  page?: number
  pageSize?: number
  q?: string
}

function queryString(query: CompetitionListQuery): string {
  const params = new URLSearchParams()
  if (query.page) params.set('page', String(query.page))
  if (query.pageSize) params.set('page_size', String(query.pageSize))
  if (query.q?.trim()) params.set('q', query.q.trim())
  const value = params.toString()
  return value ? `?${value}` : ''
}

export const competitionsApi = {
  list(query: CompetitionListQuery = {}) {
    return apiClient.get<CompetitionListResponse>(`/competitions${queryString(query)}`)
  },
  get(id: number) {
    return apiClient.get<CompetitionRead>(`/competitions/${id}`)
  },
  create(input: CompetitionInput) {
    return apiClient.post<CompetitionRead>('/admin/competitions', input)
  },
  update(id: number, input: Partial<CompetitionInput>) {
    return apiClient.patch<CompetitionRead>(`/admin/competitions/${id}`, input)
  },
  remove(id: number) {
    return apiClient.delete<void>(`/admin/competitions/${id}`)
  },
  register(id: number) {
    return apiClient.post<CompetitionRegistrationRead>(`/competitions/${id}/registrations`)
  },
  cancelRegistration(id: number) {
    return apiClient.delete<CompetitionRegistrationSummary>(`/competitions/${id}/registrations/me`)
  },
  listTasks(id: number) {
    return apiClient.get<CompetitionTaskListResponse>(`/competitions/${id}/tasks`)
  },
  listResults(id: number) {
    return apiClient.get<CompetitionResults>(`/competitions/${id}/results`)
  },
  listRegistrations(id: number) {
    return apiClient.get<CompetitionRegistrationListResponse>(`/admin/competitions/${id}/registrations`)
  },
  publish(id: number) {
    return apiClient.post<CompetitionRead>(`/admin/competitions/${id}/publish`)
  },
  archive(id: number) {
    return apiClient.post<CompetitionRead>(`/admin/competitions/${id}/archive`)
  },
}
