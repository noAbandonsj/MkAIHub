import type { AppRouteName } from './route-names'
import type { UserRole } from '@/types/user'

declare module 'vue-router' {
  interface RouteMeta {
    label: string
    requiresAuth: boolean
    navRoute?: AppRouteName
    requiresAdmin?: boolean
    requiresRole?: UserRole
  }
}
