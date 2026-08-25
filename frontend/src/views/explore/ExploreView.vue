<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import type { RouteLocationRaw } from 'vue-router'

import { exploreApi } from '@/api/explore'
import { workbenchApi } from '@/api/workbench'
import { getApiErrorMessage } from '@/api/client'
import ArtifactCard from '@/components/artifact/ArtifactCard.vue'
import { routeNames } from '@/router/route-names'
import { primaryNavigation } from '@/types/navigation'
import EmptyState from '@/components/common/EmptyState.vue'
import type { ArtifactListItem } from '@/types/artifact'
import type { WorkbenchResponse } from '@/types/workbench'
import { useSessionStore } from '@/stores/session'

const latestArtifacts = ref<ArtifactListItem[]>([])
const workbench = ref<WorkbenchResponse | null>(null)
const session = useSessionStore()
const loading = ref(false)
const artifactErrorMessage = ref('')
const workbenchErrorMessage = ref('')

interface TodoCard {
  key: string
  label: string
  description: string
  count: number
  to: RouteLocationRaw
}

const todoCards = computed<TodoCard[]>(() => {
  if (!workbench.value) return []
  const cards: TodoCard[] = [
    {
      key: 'participated',
      label: '我参与的任务',
      description: '查看当前领取和参与的任务',
      count: workbench.value.counts.participated_tasks,
      to: { name: routeNames.tasks, query: { participated: 'true' } },
    },
    {
      key: 'competition',
      label: '我的竞赛任务',
      description: '集中查看已经参与的竞赛任务',
      count: workbench.value.counts.competition_tasks,
      to: { name: routeNames.tasks, query: { participated: 'true', competition_only: 'true' } },
    },
    {
      key: 'acceptance',
      label: '待我验收',
      description: '需要处理的普通任务成果',
      count: workbench.value.counts.pending_task_reviews,
      to: { name: routeNames.tasks, query: { pending_review: 'true' } },
    },
  ]
  if (session.isAdmin) {
    cards.push({
      key: 'competition-review',
      label: '待我评审',
      description: '尚未完成评分的竞赛任务',
      count: workbench.value.counts.pending_competition_reviews,
      to: { name: routeNames.tasks, query: { pending_competition_review: 'true' } },
    })
  }
  return cards
})

const statistics = computed(() => {
  if (!workbench.value) return []
  return [
    { label: '当前参与', value: workbench.value.statistics.participations },
    { label: '成果提交', value: workbench.value.statistics.submissions },
    { label: '验收通过', value: workbench.value.statistics.accepted_submissions },
    { label: '竞赛任务完成', value: workbench.value.statistics.competition_task_completions },
    { label: '已发布结果', value: workbench.value.statistics.published_results },
  ]
})

async function loadExplore(): Promise<void> {
  loading.value = true
  artifactErrorMessage.value = ''
  workbenchErrorMessage.value = ''
  const [exploreResult, workbenchResult] = await Promise.allSettled([exploreApi.get(), workbenchApi.get()])
  if (exploreResult.status === 'fulfilled') {
    latestArtifacts.value = exploreResult.value.latest_artifacts
  } else {
    artifactErrorMessage.value = getApiErrorMessage(exploreResult.reason, '最新展品加载失败')
  }
  if (workbenchResult.status === 'fulfilled') {
    workbench.value = workbenchResult.value
  } else {
    workbenchErrorMessage.value = getApiErrorMessage(workbenchResult.reason, '个人待办加载失败')
  }
  loading.value = false
}

onMounted(() => void loadExplore())
</script>

<template>
  <div class="page-container explore-page">
    <section class="explore-hero">
      <p class="hero-eyebrow">内部 AI 分享平台</p>
      <h1 class="hero-title">发现、分享并复用团队的 AI 实践</h1>
      <p class="hero-description">
        MkAIHub 为团队保留一个清晰的入口，让有价值的经验逐步沉淀下来。
      </p>
      <RouterLink class="primary-action" :to="{ name: routeNames.artifacts }">浏览展品</RouterLink>
    </section>

    <section class="content-section" aria-labelledby="workbench-title">
      <div class="section-heading">
        <div>
          <p class="section-kicker">MY WORKBENCH</p>
          <h2 id="workbench-title">我的待办</h2>
        </div>
        <span class="section-note">从这里直达需要处理的闭环事项</span>
      </div>
      <p v-if="loading" class="muted-copy page-loading">正在加载个人待办…</p>
      <p v-else-if="workbenchErrorMessage" class="inline-error">{{ workbenchErrorMessage }}</p>
      <div v-else class="workbench-grid">
        <RouterLink v-for="item in todoCards" :key="item.key" class="workbench-card panel-card" :to="item.to">
          <span class="workbench-card__count">{{ item.count }}</span>
          <strong>{{ item.label }}</strong>
          <span>{{ item.description }}</span>
        </RouterLink>
      </div>
    </section>

    <section v-if="workbench" class="content-section" aria-labelledby="closure-statistics-title">
      <div class="section-heading">
        <div>
          <p class="section-kicker">CLOSURE SNAPSHOT</p>
          <h2 id="closure-statistics-title">闭环统计</h2>
        </div>
        <span class="section-note">基于业务记录实时重算</span>
      </div>
      <div class="statistics-grid panel-card">
        <div v-for="item in statistics" :key="item.label" class="statistics-item">
          <strong>{{ item.value }}</strong>
          <span>{{ item.label }}</span>
        </div>
      </div>
    </section>

    <section class="content-section" aria-labelledby="module-entry-title">
      <div class="section-heading">
        <div>
          <p class="section-kicker">START HERE</p>
          <h2 id="module-entry-title">模块入口</h2>
        </div>
        <span class="section-note">六个一级模块</span>
      </div>
      <div class="module-grid">
        <RouterLink
          v-for="item in primaryNavigation"
          :key="item.route"
          class="module-card"
          :to="{ name: item.route }"
        >
          <span class="module-card__label">{{ item.label }}</span>
          <span class="module-card__description">{{ item.description }}</span>
          <span class="module-card__arrow" aria-hidden="true">→</span>
        </RouterLink>
      </div>
    </section>

    <section class="content-section" aria-labelledby="latest-artifacts-title">
      <div class="section-heading">
        <div>
          <p class="section-kicker">RECENTLY PUBLISHED</p>
          <h2 id="latest-artifacts-title">最新展品</h2>
        </div>
        <RouterLink class="section-link" :to="{ name: routeNames.artifacts }">查看全部</RouterLink>
      </div>
      <p v-if="loading" class="muted-copy page-loading">正在加载最新展品…</p>
      <p v-else-if="artifactErrorMessage" class="inline-error">{{ artifactErrorMessage }}</p>
      <div v-else-if="latestArtifacts.length" class="artifact-grid explore-artifact-grid">
        <ArtifactCard v-for="artifact in latestArtifacts" :key="artifact.id" :artifact="artifact" />
      </div>
      <div v-else class="panel-card">
        <EmptyState description="暂时还没有已发布的展品" />
      </div>
    </section>
  </div>
</template>
