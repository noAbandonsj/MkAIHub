<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import {
  ElAlert,
  ElButton,
  ElCheckbox,
  ElInput,
  ElOption,
  ElPagination,
  ElSelect,
} from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/checkbox/style/css'
import 'element-plus/es/components/input/style/css'
import 'element-plus/es/components/pagination/style/css'
import 'element-plus/es/components/select/style/css'

import { artifactsApi } from '@/api/artifacts'
import { getApiErrorMessage } from '@/api/client'
import ArtifactCard from '@/components/artifact/ArtifactCard.vue'
import AppPageHeader from '@/components/common/AppPageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
import { routeNames } from '@/router/route-names'
import type { ArtifactListItem, ArtifactSource, ArtifactStatus } from '@/types/artifact'

const items = ref<ArtifactListItem[]>([])
const route = useRoute()
const query = ref('')
const mine = ref(false)
const statusFilter = ref<ArtifactStatus | ''>('')
const initialSource = route.query.source
const sourceFilter = ref<ArtifactSource | ''>(
  initialSource === 'TASK_RESULT' || initialSource === 'COMPETITION_ENTRY' ? initialSource : '',
)
const page = ref(1)
const pageSize = ref(12)
const total = ref(0)
const loading = ref(false)
const errorMessage = ref('')

async function loadArtifacts(): Promise<void> {
  loading.value = true
  errorMessage.value = ''
  try {
    const response = await artifactsApi.list({
      page: page.value,
      pageSize: pageSize.value,
      q: query.value,
      mine: mine.value,
      status: mine.value ? statusFilter.value : '',
      source: sourceFilter.value,
    })
    items.value = response.items
    total.value = response.total
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '展品加载失败')
  } finally {
    loading.value = false
  }
}

function search(): void {
  page.value = 1
  void loadArtifacts()
}

function reset(): void {
  query.value = ''
  statusFilter.value = ''
  sourceFilter.value = ''
  page.value = 1
  void loadArtifacts()
}

function changeMine(): void {
  if (!mine.value) statusFilter.value = ''
  page.value = 1
  void loadArtifacts()
}

function changeStatus(): void {
  page.value = 1
  void loadArtifacts()
}

function changeSource(): void {
  page.value = 1
  void loadArtifacts()
}

function changePage(value: number): void {
  page.value = value
  void loadArtifacts()
}

onMounted(() => void loadArtifacts())
</script>

<template>
  <div class="page-container module-page">
    <AppPageHeader
      title="展品"
      description="集中分享和复用团队的 AI 实践内容。首版不设置类型、分类或标签。"
      action-label="新建展品"
      :action-to="{ name: routeNames.artifactNew }"
    />

    <section class="filter-toolbar panel-card" aria-label="展品筛选">
      <ElInput
        v-model="query"
        class="filter-search"
        clearable
        placeholder="搜索标题或摘要"
        @keyup.enter="search"
        @clear="search"
      />
      <ElCheckbox v-model="mine" @change="changeMine">只看我的</ElCheckbox>
      <ElSelect
        v-if="mine"
        v-model="statusFilter"
        class="filter-select"
        placeholder="全部状态"
        @change="changeStatus"
      >
        <ElOption label="全部状态" value="" />
        <ElOption label="草稿" value="DRAFT" />
        <ElOption label="已发布" value="PUBLISHED" />
        <ElOption label="已归档" value="ARCHIVED" />
      </ElSelect>
      <ElSelect
        v-model="sourceFilter"
        class="filter-select"
        placeholder="全部来源"
        @change="changeSource"
      >
        <ElOption label="全部来源" value="" />
        <ElOption label="任务成果" value="TASK_RESULT" />
        <ElOption label="竞赛作品" value="COMPETITION_ENTRY" />
      </ElSelect>
      <ElButton type="primary" @click="search">搜索</ElButton>
      <ElButton @click="reset">重置</ElButton>
    </section>

    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载展品…</p>
    <div v-else-if="items.length" class="artifact-grid">
      <ArtifactCard v-for="artifact in items" :key="artifact.id" :artifact="artifact" />
    </div>
    <section v-else class="panel-card">
      <EmptyState :description="mine ? '你还没有符合条件的展品' : '暂时没有已发布的展品'" />
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
