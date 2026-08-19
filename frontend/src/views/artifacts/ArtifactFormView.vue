<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { FormInstance, FormRules } from 'element-plus'
import { ElAlert, ElButton, ElForm, ElFormItem, ElInput, ElMessage } from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/form/style/css'
import 'element-plus/es/components/form-item/style/css'
import 'element-plus/es/components/input/style/css'
import 'element-plus/es/components/message/style/css'

import { artifactsApi } from '@/api/artifacts'
import { getApiErrorMessage } from '@/api/client'
import MarkdownViewer from '@/components/artifact/MarkdownViewer.vue'
import { routeNames } from '@/router/route-names'
import { useSessionStore } from '@/stores/session'
import type { ArtifactRead, StoredFileRead } from '@/types/artifact'
import { formatFileSize } from '@/utils/format'

const MAX_FILE_SIZE = 50 * 1024 * 1024
const MAX_FILES = 10
const ALLOWED_EXTENSIONS = new Set([
  'pdf', 'docx', 'xlsx', 'pptx', 'md', 'txt', 'csv', 'json',
  'png', 'jpg', 'jpeg', 'webp', 'py', 'js', 'ts', 'vue', 'sql',
  'yaml', 'yml', 'toml', 'ipynb', 'zip',
])

const route = useRoute()
const router = useRouter()
const session = useSessionStore()
const formRef = ref<FormInstance>()
const fileInput = ref<HTMLInputElement>()
const loading = ref(false)
const submitting = ref(false)
const errorMessage = ref('')
const existingFiles = ref<StoredFileRead[]>([])
const removedFileIds = ref<number[]>([])
const selectedFiles = ref<File[]>([])
const loadedArtifact = ref<ArtifactRead | null>(null)
const form = reactive({ title: '', summary: '', content_markdown: '' })

const artifactId = computed(() => Number(route.params.id))
const isEditing = computed(() => route.name === routeNames.artifactEdit)
const pageTitle = computed(() => isEditing.value ? '编辑展品' : '新建展品')
const canPublish = computed(() => !loadedArtifact.value || loadedArtifact.value.status === 'DRAFT')
const rules: FormRules = {
  title: [
    { required: true, message: '请输入标题', trigger: 'blur' },
    { max: 200, message: '标题最多 200 个字符', trigger: 'blur' },
  ],
  summary: [
    { required: true, message: '请输入摘要', trigger: 'blur' },
    { max: 500, message: '摘要最多 500 个字符', trigger: 'blur' },
  ],
  content_markdown: [
    { required: true, message: '请输入正文', trigger: 'blur' },
    { max: 100000, message: '正文内容过长', trigger: 'blur' },
  ],
}

async function loadArtifact(): Promise<void> {
  if (!isEditing.value) return
  if (!Number.isInteger(artifactId.value) || artifactId.value <= 0) {
    errorMessage.value = '展品编号无效'
    return
  }
  loading.value = true
  try {
    const artifact = await artifactsApi.get(artifactId.value)
    if (artifact.author.id !== session.currentUser?.id) {
      errorMessage.value = '只有作者可以编辑展品正文'
      return
    }
    if (artifact.status === 'ARCHIVED') {
      errorMessage.value = '请先恢复已归档展品，再进行编辑'
      return
    }
    loadedArtifact.value = artifact
    form.title = artifact.title
    form.summary = artifact.summary
    form.content_markdown = artifact.content_markdown
    existingFiles.value = [...artifact.files]
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '展品加载失败')
  } finally {
    loading.value = false
  }
}

function extensionOf(name: string): string {
  return name.includes('.') ? name.split('.').pop()!.toLowerCase() : ''
}

function selectFiles(event: Event): void {
  const input = event.target as HTMLInputElement
  const candidates = Array.from(input.files || [])
  input.value = ''
  for (const file of candidates) {
    if (existingFiles.value.length + selectedFiles.value.length >= MAX_FILES) {
      ElMessage.warning(`每条展品最多 ${MAX_FILES} 个附件`)
      break
    }
    if (!ALLOWED_EXTENSIONS.has(extensionOf(file.name))) {
      ElMessage.warning(`${file.name} 的文件类型不支持`)
      continue
    }
    if (file.size <= 0 || file.size > MAX_FILE_SIZE) {
      ElMessage.warning(`${file.name} 必须大于 0 且不超过 50 MB`)
      continue
    }
    const duplicate = selectedFiles.value.some(
      (item) => item.name === file.name && item.size === file.size && item.lastModified === file.lastModified,
    )
    if (!duplicate) selectedFiles.value.push(file)
  }
}

function removeExisting(file: StoredFileRead): void {
  existingFiles.value = existingFiles.value.filter((item) => item.id !== file.id)
  removedFileIds.value.push(file.id)
}

async function cleanupUploads(fileIds: number[]): Promise<void> {
  await Promise.allSettled(fileIds.map((id) => artifactsApi.deleteFile(id)))
}

async function save(publishAfterSave: boolean): Promise<void> {
  if (errorMessage.value && isEditing.value && !loadedArtifact.value) return
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  submitting.value = true
  errorMessage.value = ''
  const uploadedIds: number[] = []
  try {
    for (const file of selectedFiles.value) {
      const uploaded = await artifactsApi.upload(file)
      uploadedIds.push(uploaded.id)
    }
    const input = {
      title: form.title.trim(),
      summary: form.summary.trim(),
      content_markdown: form.content_markdown.trim(),
      file_ids: [...existingFiles.value.map((file) => file.id), ...uploadedIds],
    }
    let artifact = isEditing.value
      ? await artifactsApi.update(artifactId.value, input)
      : await artifactsApi.create(input)
    if (publishAfterSave && artifact.status === 'DRAFT') {
      artifact = await artifactsApi.publish(artifact.id)
    }
    await cleanupUploads(removedFileIds.value)
    ElMessage.success(publishAfterSave ? '展品已发布' : '展品已保存')
    await router.replace({ name: routeNames.artifactDetail, params: { id: artifact.id } })
  } catch (error) {
    await cleanupUploads(uploadedIds)
    errorMessage.value = getApiErrorMessage(error, '展品保存失败')
  } finally {
    submitting.value = false
  }
}

function resetFormState(): void {
  loadedArtifact.value = null
  existingFiles.value = []
  removedFileIds.value = []
  selectedFiles.value = []
  form.title = ''
  form.summary = ''
  form.content_markdown = ''
  errorMessage.value = ''
  formRef.value?.clearValidate()
}

// The same component instance backs both /artifacts/new and /artifacts/:id/edit;
// vue-router reuses it across those routes, so rebuild the form on each entry.
watch(
  () => route.fullPath,
  () => {
    if (route.name !== routeNames.artifactNew && route.name !== routeNames.artifactEdit) return
    resetFormState()
    void loadArtifact()
  },
)

onMounted(() => void loadArtifact())
</script>

<template>
  <div class="page-container module-page artifact-form-page">
    <header class="page-header">
      <div>
        <p class="page-eyebrow">展品</p>
        <h1 class="page-title">{{ pageTitle }}</h1>
        <p class="page-description">用简洁的摘要和 Markdown 正文记录可复用的 AI 实践。</p>
      </div>
      <RouterLink class="secondary-action" :to="{ name: routeNames.artifacts }">返回列表</RouterLink>
    </header>

    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载展品…</p>
    <div v-else-if="!isEditing || loadedArtifact" class="artifact-form-layout">
      <section class="panel-card artifact-editor-panel">
        <ElForm ref="formRef" :model="form" :rules="rules" label-position="top">
          <ElFormItem label="标题" prop="title">
            <ElInput v-model="form.title" maxlength="200" show-word-limit placeholder="例如：客服提示词优化实践" />
          </ElFormItem>
          <ElFormItem label="摘要" prop="summary">
            <ElInput
              v-model="form.summary"
              type="textarea"
              :rows="3"
              maxlength="500"
              show-word-limit
              placeholder="用几句话说明内容价值和适用场景"
            />
          </ElFormItem>
          <ElFormItem label="Markdown 正文" prop="content_markdown">
            <ElInput
              v-model="form.content_markdown"
              class="markdown-editor-input"
              type="textarea"
              :rows="16"
              maxlength="100000"
              placeholder="# 背景&#10;&#10;## 使用方法"
            />
          </ElFormItem>
        </ElForm>

        <div class="artifact-upload-block">
          <div class="detail-section-heading">
            <h2>附件</h2>
            <span>{{ existingFiles.length + selectedFiles.length }} / 10</span>
          </div>
          <input ref="fileInput" class="native-file-input" type="file" multiple @change="selectFiles" />
          <ElButton @click="fileInput?.click()">选择文件</ElButton>
          <p class="form-help">单文件不超过 50 MB；不提供在线执行或预览。</p>
          <div v-if="existingFiles.length || selectedFiles.length" class="editable-file-list">
            <div v-for="file in existingFiles" :key="`stored-${file.id}`" class="editable-file-item">
              <span>{{ file.original_name }}</span>
              <span>{{ formatFileSize(file.size_bytes) }}</span>
              <button type="button" class="text-action is-danger" @click="removeExisting(file)">移除</button>
            </div>
            <div v-for="(file, index) in selectedFiles" :key="`new-${file.name}-${file.lastModified}`" class="editable-file-item">
              <span>{{ file.name }}</span>
              <span>{{ formatFileSize(file.size) }}</span>
              <button type="button" class="text-action is-danger" @click="selectedFiles.splice(index, 1)">移除</button>
            </div>
          </div>
        </div>

        <div class="form-actions">
          <ElButton :loading="submitting" @click="save(false)">
            {{ loadedArtifact?.status === 'PUBLISHED' ? '保存修改' : '保存草稿' }}
          </ElButton>
          <ElButton v-if="canPublish" type="primary" :loading="submitting" @click="save(true)">保存并发布</ElButton>
        </div>
      </section>

      <aside class="panel-card artifact-preview-panel">
        <div class="detail-section-heading"><h2>正文预览</h2></div>
        <MarkdownViewer v-if="form.content_markdown.trim()" :content="form.content_markdown" />
        <p v-else class="muted-copy">输入 Markdown 正文后在这里预览。</p>
      </aside>
    </div>
  </div>
</template>
