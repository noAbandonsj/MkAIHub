import { apiClient } from './client'
import type { ExploreResponse } from '@/types/artifact'

export const exploreApi = {
  get() {
    return apiClient.get<ExploreResponse>('/explore')
  },
}
