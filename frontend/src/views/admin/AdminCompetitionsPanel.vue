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
  ElMessage,
  ElMessageBox,
  ElPagination,
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
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'
import 'element-plus/es/components/pagination/style/css'
import 'element-plus/es/components/table/style/css'

import { competitionsApi } from '@/api/competitions'
import { getApiErrorMessage } from '@/api/client'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { routeNames } from '@/router/route-names'
import type { CompetitionListItem, CompetitionStatus } from '@/types/competition'
import { formatDate, toUtcIsoString } from '@/utils/format'
import { competitionStatusLabels, competitionStatusTones } from '@/utils/status'

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
      ElMessage.success('竞赛已创建')
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
      <ElTableColumn label="标题" min-width="200">
        <template #default="{ row }">
          <RouterLink class="table-title-link" :to="{ name: routeNames.competitionDetail, params: { id: row.id } }">
            {{ row.title }}
          </RouterLink>
        </template>
      </ElTableColumn>
      <ElTableColumn label="状态" width="110">
        <template #default="{ row }">
          <StatusBadge
            :label="competitionStatusLabels[row.status as CompetitionStatus]"
            :tone="competitionStatusTones[row.status as CompetitionStatus]"
          />
        </template>
      </ElTableColumn>
      <ElTableColumn label="开始时间" width="170">
        <template #default="{ row }">{{ formatDate(row.start_at) }}</template>
      </ElTableColumn>
      <ElTableColumn label="结束时间" width="170">
        <template #default="{ row }">{{ formatDate(row.end_at) }}</template>
      </ElTableColumn>
      <ElTableColumn prop="creator.display_name" label="创建人" width="120" />
      <ElTableColumn label="操作" fixed="right" min-width="140">
        <template #default="{ row }">
          <ElButton link type="primary" @click="openEditDialog(row as CompetitionListItem)">编辑</ElButton>
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
      :title="editingId === null ? '创建竞赛' : '编辑竞赛'"
      width="min(640px, calc(100vw - 32px))"
    >
      <ElForm ref="formRef" :model="form" :rules="formRules" label-position="top">
        <ElFormItem label="标题" prop="title">
          <ElInput v-model="form.title" maxlength="200" show-word-limit />
        </ElFormItem>
        <ElFormItem label="简介" prop="summary">
          <ElInput v-model="form.summary" maxlength="500" show-word-limit />
        </ElFormItem>
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
  </section>
</template>
