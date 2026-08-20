import { apiClient } from './client'
import type { CommentListResponse, CommentRead } from '@/types/artifact'
import type { IssueInput, IssueListResponse, IssueRead, IssueStatus } from '@/types/issue'

export interface IssueListQuery {
  page?: number
  pageSize?: number
  q?: string
  mine?: boolean
  status?: IssueStatus | ''
}

function queryString(query: IssueListQuery): string {
  const params = new URLSearchParams()
  if (query.page) params.set('page', String(query.page))
  if (query.pageSize) params.set('page_size', String(query.pageSize))
  if (query.q?.trim()) params.set('q', query.q.trim())
  if (query.mine) params.set('mine', 'true')
  if (query.status) params.set('status', query.status)
  const value = params.toString()
  return value ? `?${value}` : ''
}

export const issuesApi = {
  list(query: IssueListQuery = {}) {
    return apiClient.get<IssueListResponse>(`/issues${queryString(query)}`)
  },
  get(id: number) {
    return apiClient.get<IssueRead>(`/issues/${id}`)
  },
  create(input: IssueInput) {
    return apiClient.post<IssueRead>('/issues', input)
  },
  update(id: number, input: Partial<IssueInput>) {
    return apiClient.patch<IssueRead>(`/issues/${id}`, input)
  },
  close(id: number) {
    return apiClient.post<IssueRead>(`/issues/${id}/close`)
  },
  reopen(id: number) {
    return apiClient.post<IssueRead>(`/issues/${id}/reopen`)
  },
  listComments(issueId: number) {
    return apiClient.get<CommentListResponse>(`/issues/${issueId}/comments?page_size=100`)
  },
  createComment(issueId: number, content: string) {
    return apiClient.post<CommentRead>(`/issues/${issueId}/comments`, { content })
  },
  deleteComment(commentId: number) {
    return apiClient.delete<void>(`/comments/${commentId}`)
  },
}
