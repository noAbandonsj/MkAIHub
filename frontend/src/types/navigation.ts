import type { AppRouteName } from '@/router/route-names'

export interface NavigationItem {
  label: string
  description: string
  route: AppRouteName
}

export const primaryNavigation: readonly NavigationItem[] = [
  { label: '探索', description: '从模块入口开始浏览', route: 'explore' },
  { label: '任务', description: '查看与发布内部任务', route: 'tasks' },
  { label: '展品', description: '分享和复用 AI 实践', route: 'artifacts' },
  { label: '知识库', description: '知识沉淀能力占位', route: 'knowledge' },
  { label: 'Issues', description: '讨论平台问题与建议', route: 'issues' },
  { label: '竞赛', description: '查看内部竞赛信息', route: 'competitions' },
]
