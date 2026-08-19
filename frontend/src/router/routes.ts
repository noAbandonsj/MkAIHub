import type { RouteRecordRaw } from 'vue-router'

import AppLayout from '@/layouts/AppLayout.vue'
import AdminView from '@/views/admin/AdminView.vue'
import LoginView from '@/views/auth/LoginView.vue'
import ModulePlaceholderView from '@/views/common/ModulePlaceholderView.vue'
import ArtifactsView from '@/views/artifacts/ArtifactsView.vue'
import CompetitionsView from '@/views/competitions/CompetitionsView.vue'
import ExploreView from '@/views/explore/ExploreView.vue'
import IssuesView from '@/views/issues/IssuesView.vue'
import KnowledgeView from '@/views/knowledge/KnowledgeView.vue'
import TasksView from '@/views/tasks/TasksView.vue'
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
        component: ExploreView,
        meta: { label: '探索', requiresAuth: true, navRoute: routeNames.explore },
      },
      {
        path: 'tasks',
        name: routeNames.tasks,
        component: TasksView,
        meta: { label: '任务', requiresAuth: true, navRoute: routeNames.tasks },
      },
      {
        path: 'tasks/new',
        name: routeNames.taskNew,
        component: ModulePlaceholderView,
        meta: { label: '新建任务', requiresAuth: true, navRoute: routeNames.tasks },
      },
      {
        path: 'tasks/:id',
        name: routeNames.taskDetail,
        component: ModulePlaceholderView,
        meta: { label: '任务详情', requiresAuth: true, navRoute: routeNames.tasks },
      },
      {
        path: 'tasks/:id/edit',
        name: routeNames.taskEdit,
        component: ModulePlaceholderView,
        meta: { label: '编辑任务', requiresAuth: true, navRoute: routeNames.tasks },
      },
      {
        path: 'artifacts',
        name: routeNames.artifacts,
        component: ArtifactsView,
        meta: { label: '展品', requiresAuth: true, navRoute: routeNames.artifacts },
      },
      {
        path: 'artifacts/new',
        name: routeNames.artifactNew,
        component: ModulePlaceholderView,
        meta: { label: '新建展品', requiresAuth: true, navRoute: routeNames.artifacts },
      },
      {
        path: 'artifacts/:id',
        name: routeNames.artifactDetail,
        component: ModulePlaceholderView,
        meta: { label: '展品详情', requiresAuth: true, navRoute: routeNames.artifacts },
      },
      {
        path: 'artifacts/:id/edit',
        name: routeNames.artifactEdit,
        component: ModulePlaceholderView,
        meta: { label: '编辑展品', requiresAuth: true, navRoute: routeNames.artifacts },
      },
      {
        path: 'knowledge',
        name: routeNames.knowledge,
        component: KnowledgeView,
        meta: { label: '知识库', requiresAuth: true, navRoute: routeNames.knowledge },
      },
      {
        path: 'issues',
        name: routeNames.issues,
        component: IssuesView,
        meta: { label: 'Issues', requiresAuth: true, navRoute: routeNames.issues },
      },
      {
        path: 'issues/new',
        name: routeNames.issueNew,
        component: ModulePlaceholderView,
        meta: { label: '发起 Issue', requiresAuth: true, navRoute: routeNames.issues },
      },
      {
        path: 'issues/:id',
        name: routeNames.issueDetail,
        component: ModulePlaceholderView,
        meta: { label: 'Issue 详情', requiresAuth: true, navRoute: routeNames.issues },
      },
      {
        path: 'competitions',
        name: routeNames.competitions,
        component: CompetitionsView,
        meta: { label: '竞赛', requiresAuth: true, navRoute: routeNames.competitions },
      },
      {
        path: 'competitions/:id',
        name: routeNames.competitionDetail,
        component: ModulePlaceholderView,
        meta: { label: '竞赛详情', requiresAuth: true, navRoute: routeNames.competitions },
      },
      {
        path: 'admin',
        name: routeNames.admin,
        component: AdminView,
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
