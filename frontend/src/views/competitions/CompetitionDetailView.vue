<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElAlert } from 'element-plus'
import 'element-plus/es/components/alert/style/css'

import { competitionsApi } from '@/api/competitions'
import { getApiErrorMessage } from '@/api/client'
import MarkdownViewer from '@/components/artifact/MarkdownViewer.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { routeNames } from '@/router/route-names'
import type { CompetitionRead } from '@/types/competition'
import { formatDate } from '@/utils/format'
import { competitionStatusLabels, competitionStatusTones } from '@/utils/status'

const route = useRoute()
const competition = ref<CompetitionRead | null>(null)
const loading = ref(false)
const errorMessage = ref('')

const competitionId = computed(() => Number(route.params.id))

async function loadCompetition(): Promise<void> {
  if (!Number.isInteger(competitionId.value) || competitionId.value <= 0) {
    errorMessage.value = '竞赛编号无效'
    return
  }
  loading.value = true
  errorMessage.value = ''
  try {
    competition.value = await competitionsApi.get(competitionId.value)
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '竞赛加载失败')
  } finally {
    loading.value = false
  }
}

watch(competitionId, () => void loadCompetition())
onMounted(() => void loadCompetition())
</script>

<template>
  <div class="page-container module-page artifact-detail-page">
    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载竞赛…</p>
    <template v-else-if="competition">
      <nav class="detail-breadcrumb" aria-label="面包屑">
        <RouterLink :to="{ name: routeNames.competitions }">竞赛</RouterLink>
        <span>/</span>
        <span>{{ competition.title }}</span>
      </nav>

      <article class="panel-card artifact-detail-main competition-detail-card">
        <div class="artifact-detail-heading">
          <StatusBadge
            :label="competitionStatusLabels[competition.status]"
            :tone="competitionStatusTones[competition.status]"
          />
          <h1>{{ competition.title }}</h1>
          <div class="artifact-detail-meta">
            <span>创建人：{{ competition.creator.display_name }}</span>
            <span>开始：{{ formatDate(competition.start_at) }}</span>
            <span>结束：{{ formatDate(competition.end_at) }}</span>
            <span>更新：{{ formatDate(competition.updated_at) }}</span>
          </div>
        </div>

        <section class="artifact-detail-section">
          <div class="detail-section-heading"><h2>简介</h2></div>
          <p>{{ competition.summary }}</p>
        </section>

        <section class="artifact-detail-section">
          <div class="detail-section-heading"><h2>规则</h2></div>
          <MarkdownViewer :content="competition.rules_markdown" />
        </section>
      </article>
    </template>
  </div>
</template>
