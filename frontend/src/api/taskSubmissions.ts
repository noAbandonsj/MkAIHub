import { apiClient } from './client'
import type { TaskSubmission } from '@/types/task'

export const taskSubmissionsApi = {
  requestRevision(id: number, note: string) {
    return apiClient.post<TaskSubmission>(`/task-submissions/${id}/request-revision`, { note })
  },
  accept(id: number, note?: string | null) {
    return apiClient.post<TaskSubmission>(`/task-submissions/${id}/accept`, { note: note ?? null })
  },
  reject(id: number, note: string) {
    return apiClient.post<TaskSubmission>(`/task-submissions/${id}/reject`, { note })
  },
  competitionReview(id: number, rawScore: string, comment?: string | null) {
    return apiClient.post<TaskSubmission>(`/task-submissions/${id}/competition-review`, {
      raw_score: rawScore,
      comment: comment?.trim() || null,
    })
  },
}
