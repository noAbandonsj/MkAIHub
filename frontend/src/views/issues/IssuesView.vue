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

import { issuesApi } from '@/api/issues'
import { getApiErrorMessage } from '@/api/client'
import AppPageHeader from '@/components/common/AppPageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { routeNames } from '@/router/route-names'
import type { IssueListItem, IssueStatus } from '@/types/issue'
import { formatDate } from '@/utils/format'
import { issueStatusLabels, issueStatusTones } from '@/utils/status'

const items = ref<IssueListItem[]>([])
const query = ref('')
const mine = ref(false)
const statusFilter = ref<IssueStatus | ''>('')
const page = ref(1)
const pageSize = ref(12)
const total = ref(0)
const loading = ref(false)
const errorMessage = ref('')

async function loadIssues(): Promise<void> {
  loading.value = true
  errorMessage.value = ''
  try {
    const response = await issuesApi.list({
      page: page.value,
      pageSize: pageSize.value,
      q: query.value,
      mine: mine.value,
      status: statusFilter.value,
    })
    items.value = response.items
    total.value = response.total
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, 'Issue 加载失败')
  } finally {
    loading.value = false
  }
}

function search(): void {
  page.value = 1
  void loadIssues()
}

function reset(): void {
  query.value = ''
  statusFilter.value = ''
  page.value = 1
  void loadIssues()
}

function changeFilter(): void {
  page.value = 1
  void loadIssues()
}

function changePage(value: number): void {
  page.value = value
  void loadIssues()
}

onMounted(() => void loadIssues())
</script>

<template>
  <div class="page-container module-page">
    <AppPageHeader
      title="Issues"
      description="记录平台建议、问题和内部讨论。开放期间可以评论，关闭后只读。"
      action-label="发起 Issue"
      :action-to="{ name: routeNames.issueNew }"
    />

    <section class="filter-toolbar panel-card" aria-label="Issue 筛选">
      <ElInput
        v-model="query"
        class="filter-search"
        clearable
        placeholder="搜索标题或正文"
        @keyup.enter="search"
        @clear="search"
      />
      <ElCheckbox v-model="mine" @change="changeFilter">只看我的</ElCheckbox>
      <ElSelect v-model="statusFilter" class="filter-select" placeholder="全部状态" @change="changeFilter">
        <ElOption label="全部状态" value="" />
        <ElOption v-for="(label, value) in issueStatusLabels" :key="value" :label="label" :value="value" />
      </ElSelect>
      <ElButton type="primary" @click="search">搜索</ElButton>
      <ElButton @click="reset">重置</ElButton>
    </section>

    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载 Issues…</p>
    <ElTable v-else-if="items.length" class="module-table" :data="items" aria-label="Issue 列表">
      <ElTableColumn label="标题" min-width="240">
        <template #default="{ row }">
          <RouterLink class="table-title-link" :to="{ name: routeNames.issueDetail, params: { id: row.id } }">
            {{ row.title }}
          </RouterLink>
        </template>
      </ElTableColumn>
      <ElTableColumn label="状态" width="110">
        <template #default="{ row }">
          <StatusBadge
            :label="issueStatusLabels[row.status as IssueStatus]"
            :tone="issueStatusTones[row.status as IssueStatus]"
          />
        </template>
      </ElTableColumn>
      <ElTableColumn prop="author.display_name" label="发起人" width="130" />
      <ElTableColumn label="更新时间" width="180">
        <template #default="{ row }">{{ formatDate(row.updated_at) }}</template>
      </ElTableColumn>
    </ElTable>
    <section v-else class="panel-card">
      <EmptyState :description="mine ? '你还没有符合条件的 Issue' : '暂时没有 Issue，点击右上角发起'" />
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
