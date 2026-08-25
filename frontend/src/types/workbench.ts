export interface WorkbenchCounts {
  participated_tasks: number
  competition_tasks: number
  pending_task_reviews: number
  pending_competition_reviews: number
}

export interface ClosureStatistics {
  participations: number
  submissions: number
  accepted_submissions: number
  competition_task_completions: number
  published_results: number
}

export interface WorkbenchResponse {
  counts: WorkbenchCounts
  statistics: ClosureStatistics
}
