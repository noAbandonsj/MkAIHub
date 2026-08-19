export const routeNames = {
  root: 'root',
  login: 'login',
  explore: 'explore',
  tasks: 'tasks',
  taskNew: 'task-new',
  taskDetail: 'task-detail',
  taskEdit: 'task-edit',
  artifacts: 'artifacts',
  artifactNew: 'artifact-new',
  artifactDetail: 'artifact-detail',
  artifactEdit: 'artifact-edit',
  knowledge: 'knowledge',
  issues: 'issues',
  issueNew: 'issue-new',
  issueDetail: 'issue-detail',
  competitions: 'competitions',
  competitionDetail: 'competition-detail',
  admin: 'admin',
} as const

export type AppRouteName = (typeof routeNames)[keyof typeof routeNames]
