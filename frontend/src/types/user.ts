export type UserRole = 'EMPLOYEE' | 'SYSTEM_ADMIN'

/** User representation returned by the batch 1 API. */
export interface UserRead {
  id: number
  username: string
  display_name: string
  role: UserRole
  is_active: boolean
  last_login_at?: string | null
  created_at: string
  updated_at: string
}

// Keep the old name available to callers while the rest of the app migrates to
// the API's canonical snake_case representation.
export type CurrentUser = UserRead
