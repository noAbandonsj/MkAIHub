import { apiClient } from './client'
import type { WorkbenchResponse } from '@/types/workbench'

export const workbenchApi = {
  get() {
    return apiClient.get<WorkbenchResponse>('/workbench')
  },
}
