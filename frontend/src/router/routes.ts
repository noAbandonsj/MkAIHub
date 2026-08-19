import type { RouteRecordRaw } from 'vue-router'

import AppLayout from '@/layouts/AppLayout.vue'
import LoginView from '@/views/auth/LoginView.vue'
import { routeNames, type AppRouteName } from './route-names'

export type AppRouteRecord = RouteRecordRaw & {
  name: AppRouteName
}

export const routes = [
  {
    path: '/',
    name: routeNames.root,
    component: AppLayout,
    redirect: { name: routeNames.explore },
    meta: { label: 'MkAIHub', requiresAuth: false },
    children: [
      {
        path: 'explore',
        name: routeNames.explore,
        component: () => import('@/views/explore/ExploreView.vue'),
        meta: { label: '探索', requiresAuth: true, navRoute: routeNames.explore },
      },
      {
        path: 'tasks',
        name: routeNames.tasks,
        component: () => import('@/views/tasks/TasksView.vue'),
        meta: { label: '任务', requiresAuth: true, navRoute: routeNames.tasks },
      },
      {
        path: 'tasks/new',
        name: routeNames.taskNew,
        component: () => import('@/views/common/ModulePlaceholderView.vue'),
        meta: { label: '新建任务', requiresAuth: true, navRoute: routeNames.tasks },
      },
      {
        path: 'tasks/:id',
        name: routeNames.taskDetail,
        component: () => import('@/views/common/ModulePlaceholderView.vue'),
        meta: { label: '任务详情', requiresAuth: true, navRoute: routeNames.tasks },
      },
      {
        path: 'tasks/:id/edit',
        name: routeNames.taskEdit,
        component: () => import('@/views/common/ModulePlaceholderView.vue'),
        meta: { label: '编辑任务', requiresAuth: true, navRoute: routeNames.tasks },
      },
      {
        path: 'artifacts',
        name: routeNames.artifacts,
        component: () => import('@/views/artifacts/ArtifactsView.vue'),
        meta: { label: '展品', requiresAuth: true, navRoute: routeNames.artifacts },
      },
      {
        path: 'artifacts/new',
        name: routeNames.artifactNew,
        component: () => import('@/views/artifacts/ArtifactFormView.vue'),
        meta: { label: '新建展品', requiresAuth: true, navRoute: routeNames.artifacts },
      },
      {
        path: 'artifacts/:id',
        name: routeNames.artifactDetail,
        component: () => import('@/views/artifacts/ArtifactDetailView.vue'),
        meta: { label: '展品详情', requiresAuth: true, navRoute: routeNames.artifacts },
      },
      {
        path: 'artifacts/:id/edit',
        name: routeNames.artifactEdit,
        component: () => import('@/views/artifacts/ArtifactFormView.vue'),
        meta: { label: '编辑展品', requiresAuth: true, navRoute: routeNames.artifacts },
      },
      {
        path: 'knowledge',
        name: routeNames.knowledge,
        component: () => import('@/views/knowledge/KnowledgeView.vue'),
        meta: { label: '知识库', requiresAuth: true, navRoute: routeNames.knowledge },
      },
      {
        path: 'issues',
        name: routeNames.issues,
        component: () => import('@/views/issues/IssuesView.vue'),
        meta: { label: 'Issues', requiresAuth: true, navRoute: routeNames.issues },
      },
      {
        path: 'issues/new',
        name: routeNames.issueNew,
        component: () => import('@/views/common/ModulePlaceholderView.vue'),
        meta: { label: '发起 Issue', requiresAuth: true, navRoute: routeNames.issues },
      },
      {
        path: 'issues/:id',
        name: routeNames.issueDetail,
        component: () => import('@/views/common/ModulePlaceholderView.vue'),
        meta: { label: 'Issue 详情', requiresAuth: true, navRoute: routeNames.issues },
      },
      {
        path: 'competitions',
        name: routeNames.competitions,
        component: () => import('@/views/competitions/CompetitionsView.vue'),
        meta: { label: '竞赛', requiresAuth: true, navRoute: routeNames.competitions },
      },
      {
        path: 'competitions/:id',
        name: routeNames.competitionDetail,
        component: () => import('@/views/common/ModulePlaceholderView.vue'),
        meta: { label: '竞赛详情', requiresAuth: true, navRoute: routeNames.competitions },
      },
      {
        path: 'admin',
        name: routeNames.admin,
        component: () => import('@/views/admin/AdminView.vue'),
        meta: { label: '管理', requiresAuth: true, requiresAdmin: true },
      },
    ],
  },
  {
    path: '/login',
    name: routeNames.login,
    component: LoginView,
    meta: { label: '登录', requiresAuth: false },
  },
] satisfies AppRouteRecord[]
