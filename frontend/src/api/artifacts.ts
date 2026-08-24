import { apiClient, resolveApiUrl } from './client'
import type {
  ArtifactInput,
  ArtifactListResponse,
  ArtifactRead,
  ArtifactStatus,
  CommentListResponse,
  CommentRead,
  StoredFileRead,
} from '@/types/artifact'
import type { ArtifactTaskSourceListResponse } from '@/types/task'

export interface ArtifactListQuery {
  page?: number
  pageSize?: number
  q?: string
  mine?: boolean
  status?: ArtifactStatus | ''
}

function queryString(query: ArtifactListQuery): string {
  const params = new URLSearchParams()
  if (query.page) params.set('page', String(query.page))
  if (query.pageSize) params.set('page_size', String(query.pageSize))
  if (query.q?.trim()) params.set('q', query.q.trim())
  if (query.mine) params.set('mine', 'true')
  if (query.status) params.set('status', query.status)
  const value = params.toString()
  return value ? `?${value}` : ''
}

export const artifactsApi = {
  list(query: ArtifactListQuery = {}) {
    return apiClient.get<ArtifactListResponse>(`/artifacts${queryString(query)}`)
  },
  get(id: number) {
    return apiClient.get<ArtifactRead>(`/artifacts/${id}`)
  },
  create(input: ArtifactInput) {
    return apiClient.post<ArtifactRead>('/artifacts', input)
  },
  update(id: number, input: ArtifactInput) {
    return apiClient.patch<ArtifactRead>(`/artifacts/${id}`, input)
  },
  deleteDraft(id: number) {
    return apiClient.delete<void>(`/artifacts/${id}`)
  },
  publish(id: number) {
    return apiClient.post<ArtifactRead>(`/artifacts/${id}/publish`)
  },
  archive(id: number) {
    return apiClient.post<ArtifactRead>(`/artifacts/${id}/archive`)
  },
  restore(id: number) {
    return apiClient.post<ArtifactRead>(`/artifacts/${id}/restore`)
  },
  upload(file: File) {
    const body = new FormData()
    body.append('file', file)
    return apiClient.post<StoredFileRead>('/files', body)
  },
  deleteFile(id: number) {
    return apiClient.delete<void>(`/files/${id}`)
  },
  downloadUrl(id: number) {
    return resolveApiUrl(`/files/${id}/download`)
  },
  listComments(artifactId: number) {
    return apiClient.get<CommentListResponse>(`/artifacts/${artifactId}/comments?page_size=100`)
  },
  listTaskSources(artifactId: number) {
    return apiClient.get<ArtifactTaskSourceListResponse>(`/artifacts/${artifactId}/task-submissions`)
  },
  createComment(artifactId: number, content: string) {
    return apiClient.post<CommentRead>(`/artifacts/${artifactId}/comments`, { content })
  },
  deleteComment(commentId: number) {
    return apiClient.delete<void>(`/comments/${commentId}`)
  },
}
