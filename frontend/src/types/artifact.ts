export type ArtifactStatus = 'DRAFT' | 'PUBLISHED' | 'ARCHIVED'
export type CommentStatus = 'VISIBLE' | 'HIDDEN'

export interface UserSummary {
  id: number
  username: string
  display_name: string
}

export interface StoredFileRead {
  id: number
  original_name: string
  extension: string
  mime_type: string
  size_bytes: number
  uploader_id: number
  created_at: string
}

export interface ArtifactListItem {
  id: number
  title: string
  summary: string
  author: UserSummary
  status: ArtifactStatus
  attachment_count: number
  published_at?: string | null
  created_at: string
  updated_at: string
}

export interface ArtifactRead extends ArtifactListItem {
  content_markdown: string
  archived_at?: string | null
  files: StoredFileRead[]
}

export interface ArtifactListResponse {
  items: ArtifactListItem[]
  page: number
  page_size: number
  total: number
}

export interface ArtifactInput {
  title: string
  summary: string
  content_markdown: string
  file_ids: number[]
}

export interface CommentRead {
  id: number
  artifact_id: number
  author: UserSummary
  content: string
  status: CommentStatus
  created_at: string
  updated_at: string
}

export interface CommentListResponse {
  items: CommentRead[]
  page: number
  page_size: number
  total: number
}

export interface ExploreResponse {
  latest_artifacts: ArtifactListItem[]
}
