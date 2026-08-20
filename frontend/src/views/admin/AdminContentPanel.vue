<script setup lang="ts">
import { onMounted, ref } from 'vue'
import {
  ElAlert,
  ElButton,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
} from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'
import 'element-plus/es/components/option/style/css'
import 'element-plus/es/components/select/style/css'
import 'element-plus/es/components/table/style/css'

import { artifactsApi } from '@/api/artifacts'
import { getApiErrorMessage } from '@/api/client'
import { issuesApi } from '@/api/issues'
import { tasksApi } from '@/api/tasks'
import ArtifactStatusBadge from '@/components/artifact/ArtifactStatusBadge.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { routeNames } from '@/router/route-names'
import type { ArtifactListItem, ArtifactStatus } from '@/types/artifact'
import type { IssueListItem, IssueStatus } from '@/types/issue'
import type { TaskListItem, TaskStatus } from '@/types/task'
import { formatDate } from '@/utils/format'
import { issueStatusLabels, issueStatusTones, taskStatusLabels, taskStatusTones } from '@/utils/status'

const PAGE_SIZE = 10

const artifactStatus = ref<ArtifactStatus>('PUBLISHED')
const artifacts = ref<ArtifactListItem[]>([])
const artifactTotal = ref(0)
const artifactPage = ref(1)
const artifactsLoading = ref(false)

const tasks = ref<TaskListItem[]>([])
const taskTotal = ref(0)
const taskPage = ref(1)
const tasksLoading = ref(false)

const issues = ref<IssueListItem[]>([])
const issueTotal = ref(0)
const issuePage = ref(1)
const issuesLoading = ref(false)

const errorMessage = ref('')
const actionLoading = ref(false)

async function loadArtifacts(): Promise<void> {
  artifactsLoading.value = true
  errorMessage.value = ''
  try {
    const response = await artifactsApi.list({
      page: artifactPage.value,
      pageSize: PAGE_SIZE,
      status: artifactStatus.value,
    })
    artifacts.value = response.items
    artifactTotal.value = response.total
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '展品加载失败')
  } finally {
    artifactsLoading.value = false
  }
}

async function loadTasks(): Promise<void> {
  tasksLoading.value = true
  errorMessage.value = ''
  try {
    const response = await tasksApi.list({ page: taskPage.value, pageSize: PAGE_SIZE })
    tasks.value = response.items
    taskTotal.value = response.total
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '任务加载失败')
  } finally {
    tasksLoading.value = false
  }
}

async function loadIssues(): Promise<void> {
  issuesLoading.value = true
  errorMessage.value = ''
  try {
    const response = await issuesApi.list({ page: issuePage.value, pageSize: PAGE_SIZE })
    issues.value = response.items
    issueTotal.value = response.total
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, 'Issue 加载失败')
  } finally {
    issuesLoading.value = false
  }
}

async function runAction(action: () => Promise<unknown>, successText: string, done: () => Promise<void>): Promise<void> {
  actionLoading.value = true
  try {
    await action()
    ElMessage.success(successText)
    await done()
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '操作失败')
  } finally {
    actionLoading.value = false
  }
}

function archiveArtifact(row: ArtifactListItem): void {
  void runAction(
    async () => {
      await ElMessageBox.confirm(`确定归档展品「${row.title}」吗？归档后普通员工不可见。`, '归档展品', { type: 'warning' })
      await artifactsApi.archive(row.id)
    },
    '展品已归档',
    loadArtifacts,
  )
}

function restoreArtifact(row: ArtifactListItem): void {
  void runAction(() => artifactsApi.restore(row.id), '展品已恢复发布', loadArtifacts)
}

function closeTask(row: TaskListItem): void {
  void runAction(
    async () => {
      await ElMessageBox.confirm(`确定关闭任务「${row.title}」吗？关闭后只读。`, '关闭任务', { type: 'warning' })
      await tasksApi.close(row.id)
    },
    '任务已关闭',
    loadTasks,
  )
}

function closeIssue(row: IssueListItem): void {
  void runAction(
    async () => {
      await ElMessageBox.confirm(`确定关闭 Issue「${row.title}」吗？关闭后正文和评论只读。`, '关闭 Issue', {
        type: 'warning',
      })
      await issuesApi.close(row.id)
    },
    'Issue 已关闭',
    loadIssues,
  )
}

function reopenIssue(row: IssueListItem): void {
  void runAction(() => issuesApi.reopen(row.id), 'Issue 已重新开放', loadIssues)
}

onMounted(() => {
  void loadArtifacts()
  void loadTasks()
  void loadIssues()
})
</script>

<template>
  <section aria-label="内容维护">
    <ElAlert
      v-if="errorMessage"
      class="admin-error"
      :title="errorMessage"
      type="error"
      :closable="false"
      show-icon
      role="alert"
    />

    <div class="admin-section">
      <div class="admin-toolbar">
        <h3>展品</h3>
        <ElSelect v-model="artifactStatus" class="list-status-filter" @change="artifactPage = 1; loadArtifacts()">
          <ElOption label="已发布" value="PUBLISHED" />
          <ElOption label="已归档" value="ARCHIVED" />
        </ElSelect>
        <ElButton :loading="artifactsLoading" @click="loadArtifacts">刷新</ElButton>
      </div>
      <ElTable :data="artifacts" row-key="id" class="user-table">
        <ElTableColumn label="标题" min-width="220">
          <template #default="{ row }">
            <RouterLink class="table-title-link" :to="{ name: routeNames.artifactDetail, params: { id: row.id } }">
              {{ row.title }}
            </RouterLink>
          </template>
        </ElTableColumn>
        <ElTableColumn label="状态" width="100">
          <template #default="{ row }"><ArtifactStatusBadge :status="row.status" /></template>
        </ElTableColumn>
        <ElTableColumn prop="author.display_name" label="作者" width="120" />
        <ElTableColumn label="更新时间" width="170">
          <template #default="{ row }">{{ formatDate(row.updated_at) }}</template>
        </ElTableColumn>
        <ElTableColumn label="操作" fixed="right" min-width="100">
          <template #default="{ row }">
            <ElButton v-if="row.status === 'PUBLISHED'" link type="warning" :disabled="actionLoading" @click="archiveArtifact(row as ArtifactListItem)">
              归档
            </ElButton>
            <ElButton v-else-if="row.status === 'ARCHIVED'" link type="success" :disabled="actionLoading" @click="restoreArtifact(row as ArtifactListItem)">
              恢复
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
      <div v-if="artifactTotal > PAGE_SIZE" class="admin-pagination">
        <ElButton size="small" :disabled="artifactPage <= 1" @click="artifactPage -= 1; loadArtifacts()">上一页</ElButton>
        <span class="muted-copy">{{ artifactPage }} / {{ Math.ceil(artifactTotal / PAGE_SIZE) }} 页</span>
        <ElButton
          size="small"
          :disabled="artifactPage >= Math.ceil(artifactTotal / PAGE_SIZE)"
          @click="artifactPage += 1; loadArtifacts()"
        >
          下一页
        </ElButton>
      </div>
    </div>

    <div class="admin-section">
      <div class="admin-toolbar">
        <h3>任务</h3>
        <ElButton :loading="tasksLoading" @click="loadTasks">刷新</ElButton>
      </div>
      <ElTable :data="tasks" row-key="id" class="user-table">
        <ElTableColumn label="标题" min-width="220">
          <template #default="{ row }">
            <RouterLink class="table-title-link" :to="{ name: routeNames.taskDetail, params: { id: row.id } }">
              {{ row.title }}
            </RouterLink>
          </template>
        </ElTableColumn>
        <ElTableColumn label="状态" width="100">
          <template #default="{ row }">
            <StatusBadge :label="taskStatusLabels[row.status as TaskStatus]" :tone="taskStatusTones[row.status as TaskStatus]" />
          </template>
        </ElTableColumn>
        <ElTableColumn prop="creator.display_name" label="创建人" width="120" />
        <ElTableColumn label="更新时间" width="170">
          <template #default="{ row }">{{ formatDate(row.updated_at) }}</template>
        </ElTableColumn>
        <ElTableColumn label="操作" fixed="right" min-width="100">
          <template #default="{ row }">
            <ElButton v-if="row.status === 'OPEN'" link type="warning" :disabled="actionLoading" @click="closeTask(row as TaskListItem)">
              关闭
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
      <div v-if="taskTotal > PAGE_SIZE" class="admin-pagination">
        <ElButton size="small" :disabled="taskPage <= 1" @click="taskPage -= 1; loadTasks()">上一页</ElButton>
        <span class="muted-copy">{{ taskPage }} / {{ Math.ceil(taskTotal / PAGE_SIZE) }} 页</span>
        <ElButton
          size="small"
          :disabled="taskPage >= Math.ceil(taskTotal / PAGE_SIZE)"
          @click="taskPage += 1; loadTasks()"
        >
          下一页
        </ElButton>
      </div>
    </div>

    <div class="admin-section">
      <div class="admin-toolbar">
        <h3>Issues</h3>
        <ElButton :loading="issuesLoading" @click="loadIssues">刷新</ElButton>
      </div>
      <ElTable :data="issues" row-key="id" class="user-table">
        <ElTableColumn label="标题" min-width="220">
          <template #default="{ row }">
            <RouterLink class="table-title-link" :to="{ name: routeNames.issueDetail, params: { id: row.id } }">
              {{ row.title }}
            </RouterLink>
          </template>
        </ElTableColumn>
        <ElTableColumn label="状态" width="100">
          <template #default="{ row }">
            <StatusBadge :label="issueStatusLabels[row.status as IssueStatus]" :tone="issueStatusTones[row.status as IssueStatus]" />
          </template>
        </ElTableColumn>
        <ElTableColumn prop="author.display_name" label="发起人" width="120" />
        <ElTableColumn label="更新时间" width="170">
          <template #default="{ row }">{{ formatDate(row.updated_at) }}</template>
        </ElTableColumn>
        <ElTableColumn label="操作" fixed="right" min-width="120">
          <template #default="{ row }">
            <ElButton v-if="row.status === 'OPEN'" link type="warning" :disabled="actionLoading" @click="closeIssue(row as IssueListItem)">
              关闭
            </ElButton>
            <ElButton v-else-if="row.status === 'CLOSED'" link type="success" :disabled="actionLoading" @click="reopenIssue(row as IssueListItem)">
              重开
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
      <div v-if="issueTotal > PAGE_SIZE" class="admin-pagination">
        <ElButton size="small" :disabled="issuePage <= 1" @click="issuePage -= 1; loadIssues()">上一页</ElButton>
        <span class="muted-copy">{{ issuePage }} / {{ Math.ceil(issueTotal / PAGE_SIZE) }} 页</span>
        <ElButton
          size="small"
          :disabled="issuePage >= Math.ceil(issueTotal / PAGE_SIZE)"
          @click="issuePage += 1; loadIssues()"
        >
          下一页
        </ElButton>
      </div>
    </div>

    <p class="muted-copy admin-comment-hint">
      评论的隐藏与恢复在对应的展品或 Issue 详情页操作；管理员会在评论列表中看到已隐藏的评论。
    </p>
  </section>
</template>
