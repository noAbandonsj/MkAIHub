<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { FormInstance, FormRules } from 'element-plus'
import {
  ElAlert,
  ElButton,
  ElDatePicker,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
} from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/date-picker/style/css'
import 'element-plus/es/components/form/style/css'
import 'element-plus/es/components/form-item/style/css'
import 'element-plus/es/components/input/style/css'
import 'element-plus/es/components/message/style/css'

import { tasksApi } from '@/api/tasks'
import { getApiErrorMessage } from '@/api/client'
import MarkdownViewer from '@/components/artifact/MarkdownViewer.vue'
import { routeNames } from '@/router/route-names'
import { useSessionStore } from '@/stores/session'
import type { TaskRead } from '@/types/task'
import { toUtcIsoString } from '@/utils/format'

const route = useRoute()
const router = useRouter()
const session = useSessionStore()
const formRef = ref<FormInstance>()
const loading = ref(false)
const submitting = ref(false)
const errorMessage = ref('')
const loadedTask = ref<TaskRead | null>(null)
const form = reactive({ title: '', description: '', deadline_at: null as Date | null })

const taskId = computed(() => Number(route.params.id))
const isEditing = computed(() => route.name === routeNames.taskEdit)
const pageTitle = computed(() => (isEditing.value ? '编辑任务' : '新建任务'))
const rules: FormRules = {
  title: [
    { required: true, message: '请输入标题', trigger: 'blur' },
    { max: 200, message: '标题最多 200 个字符', trigger: 'blur' },
  ],
  description: [
    { required: true, message: '请输入任务描述', trigger: 'blur' },
    { max: 100000, message: '描述内容过长', trigger: 'blur' },
  ],
}

async function loadTask(): Promise<void> {
  if (!isEditing.value) return
  if (!Number.isInteger(taskId.value) || taskId.value <= 0) {
    errorMessage.value = '任务编号无效'
    return
  }
  loading.value = true
  try {
    const task = await tasksApi.get(taskId.value)
    if (task.creator.id !== session.currentUser?.id) {
      errorMessage.value = '只有任务创建者可以编辑任务'
      return
    }
    if (task.status !== 'OPEN') {
      errorMessage.value = '已完成或已关闭的任务不能继续编辑'
      return
    }
    loadedTask.value = task
    form.title = task.title
    form.description = task.description
    form.deadline_at = task.deadline_at ? new Date(task.deadline_at) : null
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '任务加载失败')
  } finally {
    loading.value = false
  }
}

async function save(): Promise<void> {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  submitting.value = true
  errorMessage.value = ''
  const input = {
    title: form.title.trim(),
    description: form.description.trim(),
    deadline_at: toUtcIsoString(form.deadline_at),
  }
  try {
    const task = isEditing.value
      ? await tasksApi.update(taskId.value, input)
      : await tasksApi.create(input)
    ElMessage.success(isEditing.value ? '任务已保存' : '任务已创建')
    await router.replace({ name: routeNames.taskDetail, params: { id: task.id } })
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '任务保存失败')
  } finally {
    submitting.value = false
  }
}

function resetFormState(): void {
  loadedTask.value = null
  form.title = ''
  form.description = ''
  form.deadline_at = null
  errorMessage.value = ''
  formRef.value?.clearValidate()
}

// The same component instance backs both /tasks/new and /tasks/:id/edit;
// vue-router reuses it across those routes, so rebuild the form on each entry.
watch(
  () => route.fullPath,
  () => {
    if (route.name !== routeNames.taskNew && route.name !== routeNames.taskEdit) return
    resetFormState()
    void loadTask()
  },
)

onMounted(() => void loadTask())
</script>

<template>
  <div class="page-container module-page artifact-form-page">
    <header class="page-header">
      <div>
        <p class="page-eyebrow">任务</p>
        <h1 class="page-title">{{ pageTitle }}</h1>
        <p class="page-description">用简洁的 Markdown 描述说明任务目标和验收口径。创建后立即对全员开放。</p>
      </div>
      <RouterLink class="secondary-action" :to="{ name: routeNames.tasks }">返回列表</RouterLink>
    </header>

    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载任务…</p>
    <div v-else-if="!isEditing || loadedTask" class="artifact-form-layout">
      <section class="panel-card artifact-editor-panel">
        <ElForm ref="formRef" :model="form" :rules="rules" label-position="top">
          <ElFormItem label="标题" prop="title">
            <ElInput v-model="form.title" maxlength="200" show-word-limit placeholder="例如：整理客服提示词清单" />
          </ElFormItem>
          <ElFormItem label="截止时间（可选）" prop="deadline_at">
            <ElDatePicker
              v-model="form.deadline_at"
              class="task-deadline-picker"
              type="datetime"
              placeholder="选择截止时间"
              format="YYYY-MM-DD HH:mm"
              clearable
            />
          </ElFormItem>
          <ElFormItem label="Markdown 描述" prop="description">
            <ElInput
              v-model="form.description"
              class="markdown-editor-input"
              type="textarea"
              :rows="14"
              maxlength="100000"
              placeholder="# 任务目标&#10;&#10;## 说明"
            />
          </ElFormItem>
        </ElForm>

        <div class="form-actions">
          <ElButton type="primary" :loading="submitting" @click="save">
            {{ isEditing ? '保存修改' : '创建任务' }}
          </ElButton>
        </div>
      </section>

      <aside class="panel-card artifact-preview-panel">
        <div class="detail-section-heading"><h2>描述预览</h2></div>
        <MarkdownViewer v-if="form.description.trim()" :content="form.description" />
        <p v-else class="muted-copy">输入 Markdown 描述后在这里预览。</p>
      </aside>
    </div>
  </div>
</template>
