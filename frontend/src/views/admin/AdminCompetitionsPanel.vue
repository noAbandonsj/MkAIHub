<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import {
  ElAlert,
  ElButton,
  ElDatePicker,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElSwitch,
  ElTable,
  ElTableColumn,
} from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/date-picker/style/css'
import 'element-plus/es/components/dialog/style/css'
import 'element-plus/es/components/form/style/css'
import 'element-plus/es/components/form-item/style/css'
import 'element-plus/es/components/input/style/css'
import 'element-plus/es/components/input-number/style/css'
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'
import 'element-plus/es/components/pagination/style/css'
import 'element-plus/es/components/switch/style/css'
import 'element-plus/es/components/table/style/css'
import 'element-plus/es/components/table-column/style/css'

import { adminApi } from '@/api/admin'
import { competitionsApi } from '@/api/competitions'
import { getApiErrorMessage } from '@/api/client'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { routeNames } from '@/router/route-names'
import type {
  CompetitionLifecycle,
  CompetitionListItem,
  CompetitionResultRow,
  CompetitionStatus,
  CompetitionTask,
} from '@/types/competition'
import { formatDate, toUtcIsoString } from '@/utils/format'
import {
  competitionLifecycleLabels,
  competitionLifecycleTones,
  competitionStatusLabels,
  competitionStatusTones,
} from '@/utils/status'

const items = ref<CompetitionListItem[]>([])
const query = ref('')
const page = ref(1)
const pageSize = ref(10)
const total = ref(0)
const loading = ref(false)
const errorMessage = ref('')
const submitting = ref(false)

const dialogVisible = ref(false)
const editingId = ref<number | null>(null)
const formRef = ref<FormInstance>()
const form = reactive({
  title: '',
  summary: '',
  rules_markdown: '',
  start_at: null as Date | null,
  end_at: null as Date | null,
})

const taskDialogVisible = ref(false)
const tasksLoading = ref(false)
const activeCompetition = ref<CompetitionListItem | null>(null)
const competitionTasks = ref<CompetitionTask[]>([])

const taskFormVisible = ref(false)
const taskFormRef = ref<FormInstance>()
const taskEditingId = ref<number | null>(null)
const taskForm = reactive({
  title: '',
  description: '',
  deadline_at: null as Date | null,
  required: true,
  sort_order: 1,
  max_score: 30,
  weight: 10,
})

const resultsDialogVisible = ref(false)
const resultRows = ref<CompetitionResultRow[]>([])
const awardInputs = reactive<Record<number, string>>({})

const formRules: FormRules = {
  title: [
    { required: true, message: '请输入标题', trigger: 'blur' },
    { max: 200, message: '标题最多 200 个字符', trigger: 'blur' },
  ],
  summary: [
    { required: true, message: '请输入简介', trigger: 'blur' },
    { max: 500, message: '简介最多 500 个字符', trigger: 'blur' },
  ],
  rules_markdown: [{ required: true, message: '请输入 Markdown 规则', trigger: 'blur' }],
  start_at: [{ required: true, message: '请选择开始时间', trigger: 'change' }],
  end_at: [
    { required: true, message: '请选择结束时间', trigger: 'change' },
    {
      validator: (_rule: unknown, value: Date | null, callback: (error?: Error) => void) => {
        if (value && form.start_at && value.getTime() <= form.start_at.getTime()) {
          callback(new Error('结束时间必须晚于开始时间'))
          return
        }
        callback()
      },
      trigger: 'change',
    },
  ],
}

const taskFormRules: FormRules = {
  title: [
    { required: true, message: '请输入任务标题', trigger: 'blur' },
    { max: 200, message: '标题最多 200 个字符', trigger: 'blur' },
  ],
  description: [{ required: true, message: '请输入任务描述', trigger: 'blur' }],
  max_score: [{ required: true, message: '请输入最高分', trigger: 'blur' }],
  weight: [{ required: true, message: '请输入权重', trigger: 'blur' }],
}

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
  if (page.value === 1) {
    void loadCompetitions()
    return
  }
  page.value = 1
}

function openCreateDialog(): void {
  editingId.value = null
  form.title = ''
  form.summary = ''
  form.rules_markdown = ''
  form.start_at = null
  form.end_at = null
  dialogVisible.value = true
  formRef.value?.clearValidate()
}

function openEditDialog(row: CompetitionListItem): void {
  editingId.value = row.id
  form.title = row.title
  form.summary = row.summary
  form.rules_markdown = ''
  // The list payload omits rules_markdown; load the detail before editing.
  void competitionsApi
    .get(row.id)
    .then((detail) => {
      form.rules_markdown = detail.rules_markdown
    })
    .catch((error) => {
      errorMessage.value = getApiErrorMessage(error, '竞赛详情加载失败')
    })
  form.start_at = new Date(row.start_at)
  form.end_at = new Date(row.end_at)
  dialogVisible.value = true
  formRef.value?.clearValidate()
}

async function submitForm(): Promise<void> {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid || !form.start_at || !form.end_at) return

  const input = {
    title: form.title.trim(),
    summary: form.summary.trim(),
    rules_markdown: form.rules_markdown.trim(),
    start_at: toUtcIsoString(form.start_at) ?? '',
    end_at: toUtcIsoString(form.end_at) ?? '',
  }
  submitting.value = true
  try {
    if (editingId.value === null) {
      await competitionsApi.create(input)
      ElMessage.success('竞赛已创建，初始为草稿，配置任务后发布')
    } else {
      await competitionsApi.update(editingId.value, input)
      ElMessage.success('竞赛已保存')
    }
    dialogVisible.value = false
    await loadCompetitions()
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '竞赛保存失败')
  } finally {
    submitting.value = false
  }
}

async function removeCompetition(row: CompetitionListItem): Promise<void> {
  try {
    await ElMessageBox.confirm(`确定删除竞赛「${row.title}」吗？该操作不可撤销。`, '删除竞赛', { type: 'warning' })
  } catch {
    return
  }
  try {
    await competitionsApi.remove(row.id)
    ElMessage.success('竞赛已删除')
    await loadCompetitions()
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '竞赛删除失败')
  }
}

async function publishCompetition(row: CompetitionListItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定发布竞赛「${row.title}」吗？发布后员工可见并可报名。`,
      '发布竞赛',
      { type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await competitionsApi.publish(row.id)
    ElMessage.success('竞赛已发布')
    await loadCompetitions()
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '竞赛发布失败')
  }
}

async function archiveCompetition(row: CompetitionListItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定归档竞赛「${row.title}」吗？归档后内容保持可读但不再变化。`,
      '归档竞赛',
      { type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await competitionsApi.archive(row.id)
    ElMessage.success('竞赛已归档')
    await loadCompetitions()
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '竞赛归档失败')
  }
}

async function openTasksDialog(row: CompetitionListItem): Promise<void> {
  activeCompetition.value = row
  taskDialogVisible.value = true
  await loadCompetitionTasks()
}

async function loadCompetitionTasks(): Promise<void> {
  if (!activeCompetition.value) return
  tasksLoading.value = true
  try {
    const response = await competitionsApi.listTasks(activeCompetition.value.id)
    competitionTasks.value = response.items
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '竞赛任务加载失败'))
  } finally {
    tasksLoading.value = false
  }
}

function openTaskCreate(): void {
  taskEditingId.value = null
  taskForm.title = ''
  taskForm.description = ''
  taskForm.deadline_at = null
  taskForm.required = true
  taskForm.sort_order = (competitionTasks.value.length || 0) + 1
  taskForm.max_score = 30
  taskForm.weight = 10
  taskFormVisible.value = true
  taskFormRef.value?.clearValidate()
}

function openTaskEdit(task: CompetitionTask): void {
  taskEditingId.value = task.id
  taskForm.title = task.title
  taskForm.description = task.description
  taskForm.deadline_at = task.deadline_at ? new Date(task.deadline_at) : null
  taskForm.required = task.required
  taskForm.sort_order = task.sort_order
  taskForm.max_score = Number(task.max_score)
  taskForm.weight = Number(task.weight)
  taskFormVisible.value = true
  taskFormRef.value?.clearValidate()
}

async function submitTaskForm(): Promise<void> {
  const valid = await taskFormRef.value?.validate().catch(() => false)
  if (!valid || !activeCompetition.value) return
  const input = {
    title: taskForm.title.trim(),
    description: taskForm.description.trim(),
    deadline_at: taskForm.deadline_at ? toUtcIsoString(taskForm.deadline_at) ?? null : null,
    required: taskForm.required,
    sort_order: taskForm.sort_order,
    max_score: taskForm.max_score.toFixed(2),
    weight: taskForm.weight.toFixed(2),
  }
  submitting.value = true
  try {
    if (taskEditingId.value === null) {
      await adminApi.createCompetitionTask(activeCompetition.value.id, input)
      ElMessage.success('竞赛任务已创建')
    } else {
      await adminApi.updateCompetitionTask(activeCompetition.value.id, taskEditingId.value, input)
      ElMessage.success('竞赛任务已保存')
    }
    taskFormVisible.value = false
    await loadCompetitionTasks()
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '竞赛任务保存失败'))
  } finally {
    submitting.value = false
  }
}

async function removeCompetitionTask(task: CompetitionTask): Promise<void> {
  if (!activeCompetition.value) return
  try {
    await ElMessageBox.confirm(`确定删除竞赛任务「${task.title}」吗？仅草稿竞赛可删除。`, '删除任务', {
      type: 'warning',
    })
  } catch {
    return
  }
  try {
    await adminApi.deleteCompetitionTask(activeCompetition.value.id, task.id)
    ElMessage.success('竞赛任务已删除')
    await loadCompetitionTasks()
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '竞赛任务删除失败'))
  }
}

async function openResultsDialog(row: CompetitionListItem): Promise<void> {
  if (row.lifecycle_status === 'PUBLISHED') {
    try {
      await ElMessageBox.confirm(
        '确定发布结果吗？发布前需竞赛已结束且合格参赛人的当前提交均已评审。',
        '发布结果',
        { type: 'warning' },
      )
    } catch {
      return
    }
  }
  try {
    const competition = await adminApi.publishCompetitionResults(row.id)
    ElMessage.success('竞赛结果已发布')
    await loadCompetitions()
    activeCompetition.value = { ...row, lifecycle_status: competition.lifecycle_status }
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '结果发布失败')
    return
  }
  resultsDialogVisible.value = true
  await loadResultRows(row.id)
}

async function loadResultRows(competitionId: number): Promise<void> {
  try {
    const results = await competitionsApi.listResults(competitionId)
    resultRows.value = results.items
    for (const key of Object.keys(awardInputs)) {
      delete awardInputs[Number(key)]
    }
    for (const row of results.items) {
      awardInputs[row.registration_id] = row.award ?? ''
    }
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '排行榜加载失败'))
  }
}

async function saveAwards(): Promise<void> {
  if (!activeCompetition.value) return
  const awards = resultRows.value
    .map((row) => ({ registration_id: row.registration_id, award: (awardInputs[row.registration_id] ?? '').trim() }))
    .filter((item) => item.award.length > 0)
  submitting.value = true
  try {
    await adminApi.publishCompetitionResults(activeCompetition.value.id, awards)
    ElMessage.success('奖项已保存，结果快照已更新')
    await loadResultRows(activeCompetition.value.id)
    await loadCompetitions()
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '奖项保存失败'))
  } finally {
    submitting.value = false
  }
}

onMounted(() => void loadCompetitions())
</script>

<template>
  <section aria-label="竞赛维护">
    <div class="admin-toolbar">
      <ElInput
        v-model="query"
        class="admin-search"
        clearable
        placeholder="按标题或简介查询"
        @keyup.enter="search"
      />
      <ElButton type="primary" @click="search">查询</ElButton>
      <ElButton :loading="loading" @click="loadCompetitions">刷新</ElButton>
      <ElButton type="success" @click="openCreateDialog">创建竞赛</ElButton>
    </div>

    <ElAlert
      v-if="errorMessage"
      class="admin-error"
      :title="errorMessage"
      type="error"
      :closable="false"
      show-icon
      role="alert"
    />

    <ElTable :data="items" row-key="id" class="user-table">
      <ElTableColumn label="标题" min-width="180">
        <template #default="{ row }">
          <RouterLink class="table-title-link" :to="{ name: routeNames.competitionDetail, params: { id: row.id } }">
            {{ row.title }}
          </RouterLink>
        </template>
      </ElTableColumn>
      <ElTableColumn label="时间状态" width="100">
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
      <ElTableColumn label="开始时间" width="160">
        <template #default="{ row }">{{ formatDate(row.start_at) }}</template>
      </ElTableColumn>
      <ElTableColumn label="结束时间" width="160">
        <template #default="{ row }">{{ formatDate(row.end_at) }}</template>
      </ElTableColumn>
      <ElTableColumn label="操作" fixed="right" min-width="260">
        <template #default="{ row }">
          <ElButton link type="primary" @click="openEditDialog(row as CompetitionListItem)">编辑</ElButton>
          <ElButton link type="primary" @click="openTasksDialog(row as CompetitionListItem)">任务</ElButton>
          <ElButton
            v-if="row.lifecycle_status === 'DRAFT'"
            link
            type="success"
            @click="publishCompetition(row as CompetitionListItem)"
          >
            发布
          </ElButton>
          <ElButton
            v-if="row.lifecycle_status === 'PUBLISHED' || row.lifecycle_status === 'RESULT_PUBLISHED'"
            link
            type="warning"
            @click="openResultsDialog(row as CompetitionListItem)"
          >
            {{ row.lifecycle_status === 'RESULT_PUBLISHED' ? '结果/奖项' : '发布结果' }}
          </ElButton>
          <ElButton
            v-if="row.lifecycle_status === 'PUBLISHED' || row.lifecycle_status === 'RESULT_PUBLISHED'"
            link
            @click="archiveCompetition(row as CompetitionListItem)"
          >
            归档
          </ElButton>
          <ElButton link type="danger" @click="removeCompetition(row as CompetitionListItem)">删除</ElButton>
        </template>
      </ElTableColumn>
    </ElTable>
    <p v-if="loading" class="table-loading" role="status">正在加载竞赛…</p>

    <div class="admin-pagination">
      <ElPagination
        v-model:current-page="page"
        :page-size="pageSize"
        :total="total"
        layout="total, prev, pager, next"
        background
        @current-change="loadCompetitions"
      />
    </div>

    <ElDialog
      v-model="dialogVisible"
      class="admin-competition-dialog"
      :title="editingId === null ? '创建竞赛' : '编辑竞赛'"
      width="min(640px, calc(100vw - 32px))"
    >
      <ElForm ref="formRef" class="admin-dialog-form" :model="form" :rules="formRules" label-position="top">
        <ElFormItem label="标题" prop="title">
          <ElInput v-model="form.title" maxlength="200" show-word-limit />
        </ElFormItem>
        <ElFormItem label="简介" prop="summary">
          <ElInput v-model="form.summary" maxlength="500" show-word-limit />
        </ElFormItem>
        <div class="admin-form-grid">
          <ElFormItem label="开始时间" prop="start_at">
            <ElDatePicker
              v-model="form.start_at"
              class="task-deadline-picker"
              type="datetime"
              placeholder="选择开始时间"
            />
          </ElFormItem>
          <ElFormItem label="结束时间" prop="end_at">
            <ElDatePicker
              v-model="form.end_at"
              class="task-deadline-picker"
              type="datetime"
              placeholder="选择结束时间"
            />
          </ElFormItem>
        </div>
        <ElFormItem label="Markdown 规则" prop="rules_markdown">
          <ElInput
            v-model="form.rules_markdown"
            class="markdown-editor-input"
            type="textarea"
            :rows="8"
            maxlength="100000"
          />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="dialogVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="submitting" @click="submitForm">保存</ElButton>
      </template>
    </ElDialog>

    <ElDialog
      v-model="taskDialogVisible"
      class="admin-competition-dialog admin-competition-dialog--wide"
      :title="`竞赛任务配置${activeCompetition ? `：${activeCompetition.title}` : ''}`"
      width="min(880px, calc(100vw - 32px))"
    >
      <p class="muted-copy admin-dialog-hint">
        草稿竞赛可自由配置；发布后一旦产生报名或提交，仅排序可调整，删除仅限草稿。评审在任务详情页进行，此处显示评审进度。
      </p>
      <ElTable :data="competitionTasks" row-key="id" class="user-table admin-dialog-table">
        <ElTableColumn prop="sort_order" label="顺序" width="70" />
        <ElTableColumn label="任务" min-width="180">
          <template #default="{ row }">
            <RouterLink
              class="table-title-link"
              :to="{ name: routeNames.taskDetail, params: { id: row.id } }"
            >
              {{ row.title }}
            </RouterLink>
          </template>
        </ElTableColumn>
        <ElTableColumn label="必做" width="70">
          <template #default="{ row }">{{ row.required ? '必做' : '选做' }}</template>
        </ElTableColumn>
        <ElTableColumn prop="max_score" label="最高分" width="80" />
        <ElTableColumn prop="weight" label="权重" width="70" />
        <ElTableColumn label="评审进度" width="100">
          <template #default="{ row }">
            <span v-if="row.current_submission_count !== null && row.current_submission_count !== undefined">
              {{ row.reviewed_count ?? 0 }} / {{ row.current_submission_count }}
            </span>
            <span v-else>—</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="操作" fixed="right" min-width="120">
          <template #default="{ row }">
            <ElButton link type="primary" @click="openTaskEdit(row as CompetitionTask)">编辑</ElButton>
            <ElButton link type="danger" @click="removeCompetitionTask(row as CompetitionTask)">删除</ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
      <p v-if="tasksLoading" class="table-loading" role="status">正在加载任务…</p>
      <template #footer>
        <ElButton @click="taskDialogVisible = false">关闭</ElButton>
        <ElButton type="primary" @click="openTaskCreate">添加任务</ElButton>
      </template>
    </ElDialog>

    <ElDialog
      v-model="taskFormVisible"
      class="admin-competition-dialog"
      :title="taskEditingId === null ? '添加竞赛任务' : '编辑竞赛任务'"
      width="min(560px, calc(100vw - 32px))"
    >
      <ElForm ref="taskFormRef" class="admin-dialog-form" :model="taskForm" :rules="taskFormRules" label-position="top">
        <ElFormItem label="任务标题" prop="title">
          <ElInput v-model="taskForm.title" maxlength="200" show-word-limit />
        </ElFormItem>
        <ElFormItem label="任务描述" prop="description">
          <ElInput v-model="taskForm.description" type="textarea" :rows="4" maxlength="100000" />
        </ElFormItem>
        <ElFormItem label="是否必做">
          <ElSwitch
            v-model="taskForm.required"
            active-text="必做"
            inactive-text="选做"
          />
        </ElFormItem>
        <div class="admin-form-grid admin-form-grid--three">
          <ElFormItem label="显示顺序">
            <ElInputNumber v-model="taskForm.sort_order" :min="0" :max="9999" />
          </ElFormItem>
          <ElFormItem label="最高分（原始评分上限）">
            <ElInputNumber v-model="taskForm.max_score" :min="0.01" :max="9999.99" :precision="2" />
          </ElFormItem>
          <ElFormItem label="权重（计入竞赛总分）">
            <ElInputNumber v-model="taskForm.weight" :min="0.01" :max="999.99" :precision="2" />
          </ElFormItem>
        </div>
        <ElFormItem label="提交截止时间（可选，不晚于竞赛结束）">
          <ElDatePicker
            v-model="taskForm.deadline_at"
            class="task-deadline-picker"
            type="datetime"
            placeholder="默认取竞赛结束时间"
          />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="taskFormVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="submitting" @click="submitTaskForm">保存</ElButton>
      </template>
    </ElDialog>

    <ElDialog
      v-model="resultsDialogVisible"
      class="admin-competition-dialog admin-competition-dialog--wide"
      :title="`竞赛结果${activeCompetition ? `：${activeCompetition.title}` : ''}`"
      width="min(720px, calc(100vw - 32px))"
    >
      <p class="muted-copy admin-dialog-hint">
        排名在发布时冻结；修改奖项或评分后需重新发布（整体重算并替换快照）。
      </p>
      <ElTable :data="resultRows" row-key="registration_id" class="user-table admin-dialog-table">
        <ElTableColumn label="名次" width="70">
          <template #default="{ row }">第 {{ row.rank }} 名</template>
        </ElTableColumn>
        <ElTableColumn prop="user.display_name" label="参赛人" min-width="120" />
        <ElTableColumn label="总分" width="110">
          <template #default="{ row }">{{ Number(row.total_score).toFixed(4) }}</template>
        </ElTableColumn>
        <ElTableColumn label="奖项" min-width="140">
          <template #default="{ row }">
            <ElInput
              v-model="awardInputs[row.registration_id]"
              placeholder="奖项名称（可空）"
              maxlength="200"
            />
          </template>
        </ElTableColumn>
      </ElTable>
      <template #footer>
        <ElButton @click="resultsDialogVisible = false">关闭</ElButton>
        <ElButton type="primary" :loading="submitting" @click="saveAwards">保存奖项并重新发布</ElButton>
      </template>
    </ElDialog>
  </section>
</template>
