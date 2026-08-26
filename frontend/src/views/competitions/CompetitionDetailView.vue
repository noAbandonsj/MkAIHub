<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import {
  ElAlert,
  ElButton,
  ElMessage,
  ElMessageBox,
  ElTable,
  ElTableColumn,
} from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'
import 'element-plus/es/components/table/style/css'
import 'element-plus/es/components/table-column/style/css'

import { competitionsApi } from '@/api/competitions'
import { getApiErrorMessage } from '@/api/client'
import MarkdownViewer from '@/components/artifact/MarkdownViewer.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { routeNames } from '@/router/route-names'
import { useSessionStore } from '@/stores/session'
import type {
  CompetitionRead,
  CompetitionResults,
  CompetitionTask,
} from '@/types/competition'
import { formatDate } from '@/utils/format'
import {
  competitionLifecycleLabels,
  competitionLifecycleTones,
  competitionStatusLabels,
  competitionStatusTones,
  registrationStatusLabels,
  submissionStatusLabels,
  submissionStatusTones,
} from '@/utils/status'

const route = useRoute()
const session = useSessionStore()
const competition = ref<CompetitionRead | null>(null)
const tasks = ref<CompetitionTask[]>([])
const results = ref<CompetitionResults | null>(null)
const loading = ref(false)
const actionLoading = ref(false)
const errorMessage = ref('')

const competitionId = computed(() => Number(route.params.id))
const myRegistration = computed(() => competition.value?.my_registration ?? null)
const isRegistered = computed(() => myRegistration.value?.status === 'REGISTERED')
const canRegister = computed(() => competition.value?.lifecycle_status === 'PUBLISHED')

async function loadCompetition(): Promise<void> {
  if (!Number.isInteger(competitionId.value) || competitionId.value <= 0) {
    errorMessage.value = '竞赛编号无效'
    return
  }
  loading.value = true
  errorMessage.value = ''
  try {
    const detail = await competitionsApi.get(competitionId.value)
    competition.value = detail
    const taskList = await competitionsApi.listTasks(competitionId.value)
    tasks.value = taskList.items
    if (detail.results_published) {
      results.value = await competitionsApi.listResults(competitionId.value)
    } else {
      results.value = null
    }
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '竞赛加载失败')
  } finally {
    loading.value = false
  }
}

async function runRegistration(action: () => Promise<unknown>, successText: string): Promise<void> {
  actionLoading.value = true
  try {
    await action()
    ElMessage.success(successText)
    await loadCompetition()
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '操作失败'))
  } finally {
    actionLoading.value = false
  }
}

function registerCompetition(): void {
  void runRegistration(
    () => competitionsApi.register(competitionId.value),
    '报名成功',
  )
}

async function cancelRegistration(): Promise<void> {
  try {
    await ElMessageBox.confirm('确定取消报名吗？取消后可在报名窗口内重新报名。', '取消报名', { type: 'warning' })
  } catch {
    return
  }
  await runRegistration(
    () => competitionsApi.cancelRegistration(competitionId.value),
    '已取消报名',
  )
}

watch(competitionId, () => void loadCompetition())
onMounted(() => void loadCompetition())
</script>

<template>
  <div class="page-container module-page detail-page">
    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载竞赛…</p>
    <template v-else-if="competition">
      <nav class="detail-breadcrumb" aria-label="面包屑">
        <RouterLink :to="{ name: routeNames.competitions }">竞赛</RouterLink>
        <span>/</span>
        <span>{{ competition.title }}</span>
      </nav>

      <article class="detail-layout">
        <main class="panel-card detail-main competition-detail-card">
          <div class="detail-heading">
            <StatusBadge
              :label="competitionStatusLabels[competition.status]"
              :tone="competitionStatusTones[competition.status]"
            />
            <StatusBadge
              :label="competitionLifecycleLabels[competition.lifecycle_status]"
              :tone="competitionLifecycleTones[competition.lifecycle_status]"
            />
            <h1>{{ competition.title }}</h1>
            <div class="detail-meta">
              <span>创建人：{{ competition.creator.display_name }}</span>
              <span>开始：{{ formatDate(competition.start_at) }}</span>
              <span>结束：{{ formatDate(competition.end_at) }}</span>
              <span>任务：{{ competition.task_count }} 项</span>
              <span>报名：{{ competition.registration_count }} 人</span>
            </div>
          </div>

          <section class="detail-section">
            <div class="detail-section-heading"><h2>简介</h2></div>
            <p>{{ competition.summary }}</p>
          </section>

          <section class="detail-section">
            <div class="detail-section-heading">
              <h2>竞赛任务</h2>
              <span>{{ tasks.length }} 项</span>
            </div>
            <p v-if="!tasks.length" class="muted-copy">管理员尚未配置竞赛任务。</p>
            <ul v-else class="task-submission-list">
              <li v-for="task in tasks" :key="task.id" class="task-submission-item competition-task-item">
                <div class="task-submission-head">
                  <StatusBadge
                    :label="task.required ? '必做' : '选做'"
                    :tone="task.required ? 'pending' : 'open'"
                  />
                  <RouterLink
                    class="table-title-link"
                    :to="{ name: routeNames.taskDetail, params: { id: task.id } }"
                  >
                    {{ task.title }}
                  </RouterLink>
                  <StatusBadge
                    :label="task.status === 'CLOSED' ? '已停用' : '开放中'"
                    :tone="task.status === 'CLOSED' ? 'closed' : 'open'"
                  />
                </div>
                <p class="muted-copy">
                  最高分 {{ task.max_score }} · 权重 {{ task.weight }} ·
                  提交截止：{{ formatDate(task.effective_deadline_at) }}
                </p>
                <p v-if="task.my_submission" class="muted-copy">
                  我的提交：第 {{ task.my_submission.round_no }} 轮
                  《{{ task.my_submission.artifact_title }}》
                  <StatusBadge
                    :label="submissionStatusLabels[task.my_submission.status]"
                    :tone="submissionStatusTones[task.my_submission.status]"
                  />
                </p>
                <p v-else-if="isRegistered" class="muted-copy">
                  尚未提交，进入任务页领取并提交成果。
                </p>
                <p
                  v-if="session.isAdmin && task.current_submission_count !== null && task.current_submission_count !== undefined"
                  class="muted-copy"
                >
                  评审进度：{{ task.reviewed_count ?? 0 }} / {{ task.current_submission_count }}
                </p>
              </li>
            </ul>
          </section>

          <section class="detail-section">
            <div class="detail-section-heading"><h2>规则</h2></div>
            <MarkdownViewer :content="competition.rules_markdown" />
          </section>

          <section v-if="results" class="detail-section">
            <div class="detail-section-heading">
              <h2>排行榜</h2>
              <span v-if="results.published_at">
                {{ formatDate(results.published_at) }} 发布，排名已冻结
              </span>
            </div>
            <ElTable :data="results.items" row-key="registration_id" class="module-table">
              <ElTableColumn label="名次" width="80">
                <template #default="{ row }">第 {{ row.rank }} 名</template>
              </ElTableColumn>
              <ElTableColumn prop="user.display_name" label="参赛人" min-width="140" />
              <ElTableColumn label="总分" width="120">
                <template #default="{ row }">{{ Number(row.total_score).toFixed(4) }}</template>
              </ElTableColumn>
              <ElTableColumn label="奖项" min-width="120">
                <template #default="{ row }">{{ row.award ?? '—' }}</template>
              </ElTableColumn>
            </ElTable>
          </section>
        </main>

        <aside class="detail-side">
          <section class="panel-card detail-action-card">
            <h2>竞赛报名</h2>
            <template v-if="canRegister">
              <ElButton v-if="!isRegistered" type="primary" :loading="actionLoading" @click="registerCompetition">
                报名参赛
              </ElButton>
              <ElButton v-else :loading="actionLoading" @click="cancelRegistration">取消报名</ElButton>
            </template>
            <p v-if="myRegistration" class="muted-copy">
              我的报名状态：{{ registrationStatusLabels[myRegistration.status] }}
            </p>
            <p v-if="competition.lifecycle_status === 'DRAFT'" class="muted-copy">
              竞赛尚未发布，仅管理员可见。
            </p>
            <p v-else-if="!canRegister" class="muted-copy">
              竞赛{{ competitionLifecycleLabels[competition.lifecycle_status] }}，报名已截止。
            </p>
            <p class="muted-copy">
              个人报名；报名后在提交截止时间前可对每个竞赛任务提交多轮成果，以最后一轮为准。
            </p>
          </section>

          <section v-if="session.isAdmin" class="panel-card detail-action-card">
            <h2>管理入口</h2>
            <RouterLink class="secondary-action full-width-action" :to="{ name: routeNames.admin }">
              前往管理页配置任务、评审与发布结果
            </RouterLink>
          </section>
        </aside>
      </article>
    </template>
  </div>
</template>
