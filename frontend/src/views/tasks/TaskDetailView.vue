<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import {
  ElAlert,
  ElButton,
  ElDialog,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElSelect,
} from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/dialog/style/css'
import 'element-plus/es/components/input/style/css'
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'
import 'element-plus/es/components/select/style/css'

import { adminApi } from '@/api/admin'
import { artifactsApi } from '@/api/artifacts'
import { getApiErrorMessage } from '@/api/client'
import { taskSubmissionsApi } from '@/api/taskSubmissions'
import { tasksApi } from '@/api/tasks'
import ArtifactEditor from '@/components/artifact/ArtifactEditor.vue'
import MarkdownViewer from '@/components/artifact/MarkdownViewer.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { routeNames } from '@/router/route-names'
import { useSessionStore } from '@/stores/session'
import type { ArtifactListItem, ArtifactRead } from '@/types/artifact'
import type { TaskParticipant, TaskRead, TaskSubmission } from '@/types/task'
import { formatDate } from '@/utils/format'
import {
  participantStatusLabels,
  submissionStatusLabels,
  submissionStatusTones,
  taskStatusLabels,
  taskStatusTones,
} from '@/utils/status'

interface TimelineEvent {
  at: string
  text: string
}

const route = useRoute()
const session = useSessionStore()
const task = ref<TaskRead | null>(null)
const participants = ref<TaskParticipant[]>([])
const submissions = ref<TaskSubmission[]>([])
const myArtifacts = ref<ArtifactListItem[]>([])
const loading = ref(false)
const actionLoading = ref(false)
const submitLoading = ref(false)
const artifactDialogVisible = ref(false)
const decideLoadingId = ref<number | null>(null)
const errorMessage = ref('')
const submitArtifactId = ref<number | null>(null)
const submitNote = ref('')
const reviewScores = reactive<Record<number, string>>({})
const reviewComments = reactive<Record<number, string>>({})

const taskId = computed(() => Number(route.params.id))
const isCreator = computed(() => task.value?.creator.id === session.currentUser?.id)
const canDecide = computed(() => isCreator.value || session.isAdmin)
const canClose = computed(() => isCreator.value || session.isAdmin)
const isCompetitionTask = computed(() => task.value?.competition_id != null)
const isTerminal = computed(() => task.value?.status === 'COMPLETED' || task.value?.status === 'CLOSED')
const myParticipation = computed(() => task.value?.my_participation ?? null)
const isActiveParticipant = computed(() => myParticipation.value?.status === 'ACTIVE')
const myUserId = computed(() => session.currentUser?.id)
const mySubmissions = computed(() =>
  submissions.value.filter((item) => item.participant.id === myUserId.value),
)
const myCurrentSubmission = computed(() =>
  mySubmissions.value.find((item) => item.is_current) ?? null,
)
const canSubmit = computed(() => {
  if (!isActiveParticipant.value || isTerminal.value) return false
  if (isCompetitionTask.value) {
    // Competition rounds are unlimited until the deadline; the backend gates.
    return task.value?.status === 'OPEN'
  }
  const current = myCurrentSubmission.value
  return !current || current.status === 'REVISION_REQUIRED' || current.status === 'REJECTED'
})
const reviewableSubmissions = computed(() =>
  submissions.value.filter((item) => item.is_current),
)
const timelineEvents = computed<TimelineEvent[]>(() => {
  const events: TimelineEvent[] = []
  const current = task.value
  if (!current) return events
  for (const participant of participants.value) {
    events.push({ at: participant.joined_at, text: `${participant.user.display_name} 参与任务` })
    if (participant.left_at) {
      events.push({ at: participant.left_at, text: `${participant.user.display_name} 退出参与` })
    }
  }
  for (const submission of submissions.value) {
    const who = submission.participant.display_name
    const artifactTitle = submission.artifact.title
    events.push({
      at: submission.submitted_at,
      text: `${who} 提交第 ${submission.round_no} 轮成果《${artifactTitle}》`,
    })
    if (submission.revision_requested_at) {
      events.push({
        at: submission.revision_requested_at,
        text: `第 ${submission.round_no} 轮成果被退回修改${submission.decision_note ? `：${submission.decision_note}` : ''}`,
      })
    }
    if (submission.decided_at && (submission.status === 'ACCEPTED' || submission.status === 'REJECTED')) {
      events.push({
        at: submission.decided_at,
        text:
          submission.status === 'ACCEPTED'
            ? `第 ${submission.round_no} 轮成果验收通过${submission.decision_note ? `：${submission.decision_note}` : ''}`
            : `第 ${submission.round_no} 轮成果未采用${submission.decision_note ? `：${submission.decision_note}` : ''}`,
      })
    }
  }
  if (current.completed_at) events.push({ at: current.completed_at, text: '任务完成' })
  if (current.closed_at) events.push({ at: current.closed_at, text: '任务关闭' })
  return events.sort((a, b) => b.at.localeCompare(a.at))
})

async function loadTask(): Promise<void> {
  if (!Number.isInteger(taskId.value) || taskId.value <= 0) {
    errorMessage.value = '任务编号无效'
    return
  }
  loading.value = true
  errorMessage.value = ''
  try {
    const [detail, participantList, submissionList] = await Promise.all([
      tasksApi.get(taskId.value),
      tasksApi.listParticipants(taskId.value),
      tasksApi.listSubmissions(taskId.value),
    ])
    task.value = detail
    participants.value = participantList.items
    submissions.value = submissionList.items
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '任务加载失败')
  } finally {
    loading.value = false
  }
}

async function loadMyArtifacts(): Promise<void> {
  try {
    const response = await artifactsApi.list({ mine: true, status: 'PUBLISHED', pageSize: 100 })
    myArtifacts.value = response.items
  } catch {
    myArtifacts.value = []
  }
}

async function reloadAfterAction(): Promise<void> {
  await Promise.all([loadTask(), loadMyArtifacts()])
}

async function runAction(
  confirmText: string | null,
  confirmTitle: string,
  action: () => Promise<unknown>,
  successText: string,
): Promise<void> {
  if (!task.value) return
  try {
    if (confirmText) {
      await ElMessageBox.confirm(confirmText, confirmTitle, { type: 'warning' })
    }
    actionLoading.value = true
    await action()
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
  ).then(() => void reloadAfterAction())
}

function closeTask(): void {
  void runAction(
    '确认关闭任务吗？关闭后任务将变为只读。',
    '关闭任务',
    () => tasksApi.close(task.value!.id),
    '任务已关闭',
  ).then(() => void reloadAfterAction())
}

function reopenTask(): void {
  void runAction(
    '确认重开这个已关闭的任务吗？',
    '重开任务',
    () => adminApi.reopenTask(task.value!.id),
    '任务已重开',
  ).then(() => void reloadAfterAction())
}

function joinTask(): void {
  void runAction('确认参与这个任务吗？参与后可以提交成果。', '参与任务', () => tasksApi.join(task.value!.id), '已参与任务').then(
    () => void reloadAfterAction(),
  )
}

function leaveTask(): void {
  void runAction(
    '确认退出参与吗？已提交过成果的参与人不能退出。',
    '退出参与',
    () => tasksApi.leave(task.value!.id),
    '已退出参与',
  ).then(() => void reloadAfterAction())
}

async function submitResult(): Promise<void> {
  if (!task.value || !submitArtifactId.value) {
    ElMessage.warning('请先选择一条要提交的已发布展品')
    return
  }
  submitLoading.value = true
  try {
    await tasksApi.submit(task.value.id, {
      artifact_id: submitArtifactId.value,
      note: submitNote.value.trim() || null,
    })
    ElMessage.success('成果已提交')
    submitArtifactId.value = null
    submitNote.value = ''
    await reloadAfterAction()
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '提交失败'))
  } finally {
    submitLoading.value = false
  }
}

function handleArtifactSaved(artifact: ArtifactRead): void {
  myArtifacts.value = [artifact, ...myArtifacts.value.filter((item) => item.id !== artifact.id)]
  submitArtifactId.value = artifact.id
  artifactDialogVisible.value = false
  ElMessage.success('展品已创建并发布，已自动选中')
}

async function promptNote(title: string, required: boolean): Promise<string | null> {
  try {
    const result = await ElMessageBox.prompt(
      required ? '请填写处理意见（必填）' : '处理意见（可留空）',
      title,
      {
        inputType: 'textarea',
        inputValue: '',
        inputValidator: (value: string) => (required ? Boolean(value?.trim()) || '请填写处理意见' : true),
      },
    )
    return (result.value as string | undefined)?.trim() ?? ''
  } catch {
    return null
  }
}

async function decideSubmission(submission: TaskSubmission, action: 'request_revision' | 'accept' | 'reject'): Promise<void> {
  const required = action !== 'accept'
  const title = action === 'request_revision' ? '退回修改' : action === 'accept' ? '验收通过' : '不采用'
  const note = await promptNote(title, required)
  if (note === null) return
  decideLoadingId.value = submission.id
  try {
    if (action === 'request_revision') {
      await taskSubmissionsApi.requestRevision(submission.id, note)
    } else if (action === 'accept') {
      await taskSubmissionsApi.accept(submission.id, note || null)
    } else {
      await taskSubmissionsApi.reject(submission.id, note)
    }
    ElMessage.success('处理完成')
    await loadTask()
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '操作失败'))
  } finally {
    decideLoadingId.value = null
  }
}

async function submitCompetitionReview(submission: TaskSubmission): Promise<void> {
  const rawScore = (reviewScores[submission.id] ?? '').trim()
  if (!rawScore) {
    ElMessage.warning('请填写评审分数')
    return
  }
  decideLoadingId.value = submission.id
  try {
    await taskSubmissionsApi.competitionReview(submission.id, rawScore, reviewComments[submission.id] ?? '')
    ElMessage.success('评审已保存')
    await loadTask()
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '评审失败'))
  } finally {
    decideLoadingId.value = null
  }
}

watch(taskId, () => void loadTask())
onMounted(() => {
  void loadTask()
  void loadMyArtifacts()
})
</script>

<template>
  <div class="page-container module-page detail-page">
    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载任务…</p>
    <template v-else-if="task">
      <nav class="detail-breadcrumb" aria-label="面包屑">
        <RouterLink :to="{ name: routeNames.tasks }">任务</RouterLink>
        <span>/</span>
        <span>{{ task.title }}</span>
      </nav>

      <article class="detail-layout">
        <main class="panel-card detail-main">
          <div class="detail-heading">
            <StatusBadge :label="taskStatusLabels[task.status]" :tone="taskStatusTones[task.status]" />
            <h1>{{ task.title }}</h1>
            <div class="detail-meta">
              <span>创建人：{{ task.creator.display_name }}</span>
              <span v-if="task.competition_id">
                所属竞赛：
                <RouterLink
                  class="table-title-link"
                  :to="{ name: routeNames.competitionDetail, params: { id: task.competition_id } }"
                >
                  {{ task.competition_title ?? `竞赛 #${task.competition_id}` }}
                </RouterLink>
              </span>
              <span>参与：{{ task.participant_count }} 人</span>
              <span>成果：{{ task.submission_count }} 次</span>
              <span>创建：{{ formatDate(task.created_at) }}</span>
              <span v-if="task.deadline_at">截止：{{ formatDate(task.deadline_at) }}</span>
              <span v-if="task.completed_at">完成：{{ formatDate(task.completed_at) }}</span>
              <span v-if="task.closed_at">关闭：{{ formatDate(task.closed_at) }}</span>
            </div>
          </div>

          <section class="detail-section">
            <div class="detail-section-heading"><h2>任务描述</h2></div>
            <MarkdownViewer :content="task.description" />
          </section>

          <section v-if="isActiveParticipant || mySubmissions.length" class="detail-section">
            <div class="detail-section-heading">
              <h2>成果提交</h2>
              <span v-if="myCurrentSubmission">当前轮次：第 {{ myCurrentSubmission.round_no }} 轮</span>
            </div>
            <div v-if="canSubmit" class="task-submit-form">
              <ElSelect
                v-model="submitArtifactId"
                placeholder="选择我的已发布展品"
                class="task-submit-select"
                filterable
              >
                <ElOption
                  v-for="item in myArtifacts"
                  :key="item.id"
                  :label="item.title"
                  :value="item.id"
                />
              </ElSelect>
              <ElInput
                v-model="submitNote"
                type="textarea"
                :rows="2"
                placeholder="提交说明（可选）"
                class="task-submit-note"
              />
              <div class="task-submit-actions">
                <ElButton type="primary" :loading="submitLoading" @click="submitResult">提交成果</ElButton>
                <ElButton @click="artifactDialogVisible = true">新建展品</ElButton>
              </div>
              <p v-if="!myArtifacts.length" class="muted-copy">
                还没有可提交的已发布展品，可在这里直接创建并发布。
              </p>
            </div>
            <p v-else-if="isActiveParticipant">
              {{ isCompetitionTask
                ? '截止时间前可继续提交新版本，竞赛计分以最后一次有效提交为准。'
                : '当前轮次正在等待发布人处理，暂不能提交新成果。' }}
            </p>

            <ul v-if="mySubmissions.length" class="task-submission-list">
              <li
                v-for="submission in mySubmissions"
                :key="submission.id"
                class="task-submission-item"
                :class="{ 'is-current': submission.is_current }"
              >
                <div class="task-submission-head">
                  <StatusBadge
                    :label="submissionStatusLabels[submission.status]"
                    :tone="submissionStatusTones[submission.status]"
                  />
                  <span>第 {{ submission.round_no }} 轮</span>
                  <span v-if="submission.is_current">当前有效版本</span>
                  <RouterLink
                    class="table-title-link"
                    :to="{ name: routeNames.artifactDetail, params: { id: submission.artifact.id } }"
                  >
                    {{ submission.artifact.title }}
                  </RouterLink>
                </div>
                <p class="muted-copy">
                  提交：{{ formatDate(submission.submitted_at) }}
                  <template v-if="submission.note">说明：{{ submission.note }}</template>
                </p>
                <p v-if="submission.decision_note" class="muted-copy">处理意见：{{ submission.decision_note }}</p>
                <p v-if="submission.competition_review" class="muted-copy">
                  评审得分：{{ submission.competition_review.raw_score }}
                  <template v-if="submission.competition_review.comment">
                    评语：{{ submission.competition_review.comment }}
                  </template>
                </p>
                <p v-if="submission.competition_rank" class="muted-copy">
                  竞赛名次：第 {{ submission.competition_rank }} 名
                  <template v-if="submission.competition_award">（{{ submission.competition_award }}）</template>
                </p>
              </li>
            </ul>
          </section>

          <section v-if="canDecide && !isCompetitionTask" class="detail-section">
            <div class="detail-section-heading">
              <h2>成果验收</h2>
              <span>共 {{ submissions.length }} 次提交</span>
            </div>
            <p v-if="!submissions.length" class="muted-copy">还没有参与人提交成果。</p>
            <ul v-else class="task-submission-list">
              <li
                v-for="submission in submissions"
                :key="submission.id"
                class="task-submission-item"
                :class="{ 'is-current': submission.is_current }"
              >
                <div class="task-submission-head">
                  <StatusBadge
                    :label="submissionStatusLabels[submission.status]"
                    :tone="submissionStatusTones[submission.status]"
                  />
                  <span>{{ submission.participant.display_name }}</span>
                  <span>第 {{ submission.round_no }} 轮</span>
                  <RouterLink
                    class="table-title-link"
                    :to="{ name: routeNames.artifactDetail, params: { id: submission.artifact.id } }"
                  >
                    {{ submission.artifact.title }}
                  </RouterLink>
                </div>
                <p class="muted-copy">
                  提交：{{ formatDate(submission.submitted_at) }}
                  <template v-if="submission.note">说明：{{ submission.note }}</template>
                </p>
                <p v-if="submission.decision_note" class="muted-copy">
                  {{ submission.decider?.display_name ?? '发布人' }}：{{ submission.decision_note }}
                </p>
                <div v-if="submission.is_current && (submission.status === 'SUBMITTED' || submission.status === 'REVISION_REQUIRED')" class="task-submission-actions">
                  <ElButton
                    size="small"
                    :loading="decideLoadingId === submission.id"
                    @click="decideSubmission(submission, 'request_revision')"
                  >
                    退回修改
                  </ElButton>
                  <ElButton
                    size="small"
                    type="primary"
                    :loading="decideLoadingId === submission.id"
                    @click="decideSubmission(submission, 'accept')"
                  >
                    验收通过
                  </ElButton>
                  <ElButton
                    size="small"
                    type="danger"
                    plain
                    :loading="decideLoadingId === submission.id"
                    @click="decideSubmission(submission, 'reject')"
                  >
                    不采用
                  </ElButton>
                </div>
              </li>
            </ul>
          </section>

          <section v-if="isCompetitionTask && session.isAdmin" class="detail-section">
            <div class="detail-section-heading">
              <h2>竞赛评审</h2>
              <span>{{ reviewableSubmissions.length }} 条当前有效提交</span>
            </div>
            <p v-if="!reviewableSubmissions.length" class="muted-copy">还没有参赛人提交成果。</p>
            <ul v-else class="task-submission-list">
              <li
                v-for="submission in reviewableSubmissions"
                :key="submission.id"
                class="task-submission-item is-current"
              >
                <div class="task-submission-head">
                  <span>{{ submission.participant.display_name }}</span>
                  <span>第 {{ submission.round_no }} 轮</span>
                  <RouterLink
                    class="table-title-link"
                    :to="{ name: routeNames.artifactDetail, params: { id: submission.artifact.id } }"
                  >
                    {{ submission.artifact.title }}
                  </RouterLink>
                  <template v-if="submission.competition_review">
                    <StatusBadge label="已评审" tone="done" />
                    <span class="muted-copy">
                      {{ submission.competition_review.raw_score }} 分 ·
                      {{ formatDate(submission.competition_review.reviewed_at) }}
                    </span>
                  </template>
                  <StatusBadge v-else label="待评审" tone="pending" />
                </div>
                <p class="muted-copy">
                  提交：{{ formatDate(submission.submitted_at) }}
                  <template v-if="submission.note">说明：{{ submission.note }}</template>
                </p>
                <div class="task-review-form">
                  <ElInput
                    v-model="reviewScores[submission.id]"
                    class="task-review-score"
                    placeholder="分数"
                  />
                  <ElInput
                    v-model="reviewComments[submission.id]"
                    class="task-review-comment"
                    placeholder="评语（可选）"
                  />
                  <ElButton
                    size="small"
                    type="primary"
                    :loading="decideLoadingId === submission.id"
                    @click="submitCompetitionReview(submission)"
                  >
                    {{ submission.competition_review ? '更新评审' : '提交评审' }}
                  </ElButton>
                </div>
              </li>
            </ul>
          </section>

          <section v-if="timelineEvents.length" class="detail-section">
            <div class="detail-section-heading"><h2>处理时间线</h2></div>
            <ol class="task-timeline">
              <li v-for="(event, index) in timelineEvents" :key="index" class="task-timeline-item">
                <span class="task-timeline-time">{{ formatDate(event.at) }}</span>
                <span class="task-timeline-content">{{ event.text }}</span>
              </li>
            </ol>
          </section>
        </main>

        <aside class="detail-side">
          <section class="panel-card detail-action-card">
            <h2>任务操作</h2>
            <RouterLink
              v-if="isCreator && !isTerminal"
              class="secondary-action full-width-action"
              :to="{ name: routeNames.taskEdit, params: { id: task.id } }"
            >
              编辑任务
            </RouterLink>
            <ElButton
              v-if="isCreator && !isTerminal && !isCompetitionTask"
              type="primary"
              :loading="actionLoading"
              @click="completeTask"
            >
              标记完成
            </ElButton>
            <ElButton v-if="canClose && !isTerminal" :loading="actionLoading" @click="closeTask">关闭任务</ElButton>
            <ElButton v-if="session.isAdmin && task.status === 'CLOSED'" :loading="actionLoading" @click="reopenTask">
              重开任务
            </ElButton>
            <p v-if="isTerminal" class="muted-copy">
              任务已{{ task.status === 'COMPLETED' ? '完成' : '关闭' }}，内容只读。
            </p>
            <p v-else-if="!canDecide" class="muted-copy">只有任务创建者可以编辑或完成任务。</p>
          </section>

          <section class="panel-card detail-action-card">
            <h2>参与任务</h2>
            <template v-if="!isTerminal && !isCompetitionTask">
              <ElButton v-if="!myParticipation || myParticipation.status === 'LEFT'" type="primary" :loading="actionLoading" @click="joinTask">
                领取任务
              </ElButton>
              <ElButton v-if="isActiveParticipant" :loading="actionLoading" @click="leaveTask">退出参与</ElButton>
            </template>
            <p v-if="isCompetitionTask" class="muted-copy">
              竞赛任务由报名自动领取；如需参与或退出，请到所属竞赛处理报名。
            </p>
            <p v-if="myParticipation" class="muted-copy">
              我的参与状态：{{ participantStatusLabels[myParticipation.status] }}
            </p>
            <ul class="task-participant-list">
              <li v-for="participant in participants" :key="participant.id">
                <span>{{ participant.user.display_name }}</span>
                <span class="muted-copy">{{ participantStatusLabels[participant.status] }} · {{ formatDate(participant.joined_at) }}</span>
              </li>
            </ul>
          </section>
        </aside>
      </article>
    </template>

    <ElDialog
      v-model="artifactDialogVisible"
      class="artifact-create-dialog"
      title="新建任务展品"
      width="1120px"
      destroy-on-close
    >
      <p class="muted-copy artifact-create-dialog-hint">
        创建并发布后会自动选中这条展品；关闭弹窗后仍需点击“提交成果”完成任务提交。
      </p>
      <ArtifactEditor
        v-if="artifactDialogVisible"
        mode="create"
        :allow-draft="false"
        @saved="handleArtifactSaved"
      />
    </ElDialog>
  </div>
</template>
