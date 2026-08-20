<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElAlert, ElButton, ElMessage, ElMessageBox } from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'

import { tasksApi } from '@/api/tasks'
import { getApiErrorMessage } from '@/api/client'
import MarkdownViewer from '@/components/artifact/MarkdownViewer.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { routeNames } from '@/router/route-names'
import { useSessionStore } from '@/stores/session'
import type { TaskRead } from '@/types/task'
import { formatDate } from '@/utils/format'
import { taskStatusLabels, taskStatusTones } from '@/utils/status'

const route = useRoute()
const session = useSessionStore()
const task = ref<TaskRead | null>(null)
const loading = ref(false)
const actionLoading = ref(false)
const errorMessage = ref('')

const taskId = computed(() => Number(route.params.id))
const isCreator = computed(() => task.value?.creator.id === session.currentUser?.id)
const canClose = computed(() => isCreator.value || session.isAdmin)
const isOpen = computed(() => task.value?.status === 'OPEN')

async function loadTask(): Promise<void> {
  if (!Number.isInteger(taskId.value) || taskId.value <= 0) {
    errorMessage.value = '任务编号无效'
    return
  }
  loading.value = true
  errorMessage.value = ''
  try {
    task.value = await tasksApi.get(taskId.value)
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '任务加载失败')
  } finally {
    loading.value = false
  }
}

async function runAction(
  confirmText: string | null,
  confirmTitle: string,
  action: () => Promise<TaskRead>,
  successText: string,
): Promise<void> {
  if (!task.value) return
  try {
    if (confirmText) {
      await ElMessageBox.confirm(confirmText, confirmTitle, { type: 'warning' })
    }
    actionLoading.value = true
    task.value = await action()
    ElMessage.success(successText)
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      ElMessage.error(getApiErrorMessage(error, '操作失败'))
    }
  } finally {
    actionLoading.value = false
  }
}

function completeTask(): void {
  void runAction(
    '确认把任务标记为完成吗？完成后任务将变为只读。',
    '完成任务',
    () => tasksApi.complete(task.value!.id),
    '任务已完成',
  )
}

function closeTask(): void {
  void runAction(
    '确认关闭任务吗？关闭后任务将变为只读。',
    '关闭任务',
    () => tasksApi.close(task.value!.id),
    '任务已关闭',
  )
}

watch(taskId, () => void loadTask())
onMounted(() => void loadTask())
</script>

<template>
  <div class="page-container module-page artifact-detail-page">
    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载任务…</p>
    <template v-else-if="task">
      <nav class="detail-breadcrumb" aria-label="面包屑">
        <RouterLink :to="{ name: routeNames.tasks }">任务</RouterLink>
        <span>/</span>
        <span>{{ task.title }}</span>
      </nav>

      <article class="artifact-detail-layout">
        <main class="panel-card artifact-detail-main">
          <div class="artifact-detail-heading">
            <StatusBadge :label="taskStatusLabels[task.status]" :tone="taskStatusTones[task.status]" />
            <h1>{{ task.title }}</h1>
            <div class="artifact-detail-meta">
              <span>创建人：{{ task.creator.display_name }}</span>
              <span>创建：{{ formatDate(task.created_at) }}</span>
              <span>更新：{{ formatDate(task.updated_at) }}</span>
              <span v-if="task.deadline_at">截止：{{ formatDate(task.deadline_at) }}</span>
              <span v-if="task.completed_at">完成：{{ formatDate(task.completed_at) }}</span>
              <span v-if="task.closed_at">关闭：{{ formatDate(task.closed_at) }}</span>
            </div>
          </div>

          <section class="artifact-detail-section">
            <div class="detail-section-heading"><h2>任务描述</h2></div>
            <MarkdownViewer :content="task.description" />
          </section>
        </main>

        <aside class="artifact-detail-side">
          <section class="panel-card artifact-action-card">
            <h2>任务操作</h2>
            <RouterLink
              v-if="isCreator && isOpen"
              class="secondary-action full-width-action"
              :to="{ name: routeNames.taskEdit, params: { id: task.id } }"
            >
              编辑任务
            </RouterLink>
            <ElButton v-if="isCreator && isOpen" type="primary" :loading="actionLoading" @click="completeTask">
              标记完成
            </ElButton>
            <ElButton v-if="canClose && isOpen" :loading="actionLoading" @click="closeTask">关闭任务</ElButton>
            <p v-if="!isOpen" class="muted-copy">任务已{{ task.status === 'COMPLETED' ? '完成' : '关闭' }}，内容只读。</p>
            <p v-else-if="!isCreator" class="muted-copy">只有任务创建者可以编辑或完成任务。</p>
          </section>
        </aside>
      </article>
    </template>
  </div>
</template>
