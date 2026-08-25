<script setup lang="ts">
import { onMounted, ref } from 'vue'
import {
  ElAlert,
  ElButton,
  ElInput,
  ElPagination,
  ElTable,
  ElTableColumn,
} from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/input/style/css'
import 'element-plus/es/components/pagination/style/css'
import 'element-plus/es/components/table/style/css'

import { competitionsApi } from '@/api/competitions'
import { getApiErrorMessage } from '@/api/client'
import AppPageHeader from '@/components/common/AppPageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { routeNames } from '@/router/route-names'
import type { CompetitionLifecycle, CompetitionListItem, CompetitionStatus } from '@/types/competition'
import { formatDate } from '@/utils/format'
import {
  competitionLifecycleLabels,
  competitionLifecycleTones,
  competitionStatusLabels,
  competitionStatusTones,
} from '@/utils/status'

const items = ref<CompetitionListItem[]>([])
const query = ref('')
const page = ref(1)
const pageSize = ref(12)
const total = ref(0)
const loading = ref(false)
const errorMessage = ref('')

async function loadCompetitions(): Promise<void> {
  loading.value = true
  errorMessage.value = ''
  try {
    const response = await competitionsApi.list({
      page: page.value,
      pageSize: pageSize.value,
      q: query.value,
    })
    items.value = response.items
    total.value = response.total
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '竞赛加载失败')
  } finally {
    loading.value = false
  }
}

function search(): void {
  page.value = 1
  void loadCompetitions()
}

function reset(): void {
  query.value = ''
  page.value = 1
  void loadCompetitions()
}

function changePage(value: number): void {
  page.value = value
  void loadCompetitions()
}

onMounted(() => void loadCompetitions())
</script>

<template>
  <div class="page-container module-page">
    <AppPageHeader
      title="竞赛"
      description="查看内部竞赛的时间安排和规则。竞赛由系统管理员维护，状态随时间自动计算。"
    />

    <section class="list-toolbar panel-card" aria-label="竞赛筛选">
      <ElInput
        v-model="query"
        class="list-search"
        clearable
        placeholder="搜索标题或简介"
        @keyup.enter="search"
        @clear="search"
      />
      <ElButton type="primary" @click="search">搜索</ElButton>
      <ElButton @click="reset">重置</ElButton>
    </section>

    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载竞赛…</p>
    <ElTable v-else-if="items.length" class="module-table" :data="items" aria-label="竞赛列表">
      <ElTableColumn label="标题" min-width="240">
        <template #default="{ row }">
          <RouterLink class="table-title-link" :to="{ name: routeNames.competitionDetail, params: { id: row.id } }">
            {{ row.title }}
          </RouterLink>
        </template>
      </ElTableColumn>
      <ElTableColumn prop="summary" label="简介" min-width="240" show-overflow-tooltip />
      <ElTableColumn label="状态" width="110">
        <template #default="{ row }">
          <StatusBadge
            :label="competitionStatusLabels[row.status as CompetitionStatus]"
            :tone="competitionStatusTones[row.status as CompetitionStatus]"
          />
        </template>
      </ElTableColumn>
      <ElTableColumn label="运营状态" width="110">
        <template #default="{ row }">
          <StatusBadge
            :label="competitionLifecycleLabels[row.lifecycle_status as CompetitionLifecycle]"
            :tone="competitionLifecycleTones[row.lifecycle_status as CompetitionLifecycle]"
          />
        </template>
      </ElTableColumn>
      <ElTableColumn label="开始时间" width="180">
        <template #default="{ row }">{{ formatDate(row.start_at) }}</template>
      </ElTableColumn>
      <ElTableColumn label="结束时间" width="180">
        <template #default="{ row }">{{ formatDate(row.end_at) }}</template>
      </ElTableColumn>
    </ElTable>
    <section v-else class="panel-card">
      <EmptyState description="暂时没有可展示的竞赛" />
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
