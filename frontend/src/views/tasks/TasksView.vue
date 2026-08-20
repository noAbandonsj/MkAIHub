<script setup lang="ts">
import { onMounted, ref } from 'vue'
import {
  ElAlert,
  ElButton,
  ElCheckbox,
  ElInput,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
} from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/checkbox/style/css'
import 'element-plus/es/components/input/style/css'
import 'element-plus/es/components/pagination/style/css'
import 'element-plus/es/components/select/style/css'
import 'element-plus/es/components/table/style/css'

import { tasksApi } from '@/api/tasks'
import { getApiErrorMessage } from '@/api/client'
import AppPageHeader from '@/components/common/AppPageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { routeNames } from '@/router/route-names'
import type { TaskListItem, TaskStatus } from '@/types/task'
import { formatDate } from '@/utils/format'
import { taskStatusLabels, taskStatusTones } from '@/utils/status'

const items = ref<TaskListItem[]>([])
const query = ref('')
const mine = ref(false)
const statusFilter = ref<TaskStatus | ''>('')
const page = ref(1)
const pageSize = ref(12)
const total = ref(0)
const loading = ref(false)
const errorMessage = ref('')

async function loadTasks(): Promise<void> {
  loading.value = true
  errorMessage.value = ''
  try {
    const response = await tasksApi.list({
      page: page.value,
      pageSize: pageSize.value,
      q: query.value,
      mine: mine.value,
      status: statusFilter.value,
    })
    items.value = response.items
    total.value = response.total
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '任务加载失败')
  } finally {
    loading.value = false
  }
}

function search(): void {
  page.value = 1
  void loadTasks()
}

function reset(): void {
  query.value = ''
  statusFilter.value = ''
  page.value = 1
  void loadTasks()
}

function changeFilter(): void {
  page.value = 1
  void loadTasks()
}

function changePage(value: number): void {
  page.value = value
  void loadTasks()
}

onMounted(() => void loadTasks())
</script>

<template>
  <div class="page-container module-page">
    <AppPageHeader
      title="任务"
      description="发布和跟进内部 AI 相关任务。任务创建后立即开放，完成或关闭后只读。"
      action-label="新建任务"
      :action-to="{ name: routeNames.taskNew }"
    />

    <section class="list-toolbar panel-card" aria-label="任务筛选">
      <ElInput
        v-model="query"
        class="list-search"
        clearable
        placeholder="搜索标题或描述"
        @keyup.enter="search"
        @clear="search"
      />
      <ElCheckbox v-model="mine" @change="changeFilter">只看我的</ElCheckbox>
      <ElSelect v-model="statusFilter" class="list-status-filter" placeholder="全部状态" @change="changeFilter">
        <ElOption label="全部状态" value="" />
        <ElOption v-for="(label, value) in taskStatusLabels" :key="value" :label="label" :value="value" />
      </ElSelect>
      <ElButton type="primary" @click="search">搜索</ElButton>
      <ElButton @click="reset">重置</ElButton>
    </section>

    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载任务…</p>
    <ElTable v-else-if="items.length" class="module-table" :data="items" aria-label="任务列表">
      <ElTableColumn label="标题" min-width="220">
        <template #default="{ row }">
          <RouterLink class="table-title-link" :to="{ name: routeNames.taskDetail, params: { id: row.id } }">
            {{ row.title }}
          </RouterLink>
        </template>
      </ElTableColumn>
      <ElTableColumn label="状态" width="110">
        <template #default="{ row }">
          <StatusBadge
            :label="taskStatusLabels[row.status as TaskStatus]"
            :tone="taskStatusTones[row.status as TaskStatus]"
          />
        </template>
      </ElTableColumn>
      <ElTableColumn prop="creator.display_name" label="创建人" width="130" />
      <ElTableColumn label="截止时间" width="180">
        <template #default="{ row }">{{ formatDate(row.deadline_at) }}</template>
      </ElTableColumn>
      <ElTableColumn label="更新时间" width="180">
        <template #default="{ row }">{{ formatDate(row.updated_at) }}</template>
      </ElTableColumn>
    </ElTable>
    <section v-else class="panel-card">
      <EmptyState :description="mine ? '你还没有符合条件的任务' : '暂时没有任务，点击右上角新建'" />
    </section>

    <ElPagination
      v-if="total > pageSize"
      class="artifact-pagination"
      background
      layout="prev, pager, next"
      :current-page="page"
      :page-size="pageSize"
      :total="total"
      @current-change="changePage"
    />
  </div>
</template>
