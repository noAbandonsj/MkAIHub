<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import type { FormInstance, FormRules } from 'element-plus'
import { ElAlert, ElButton, ElDialog, ElForm, ElFormItem, ElInput, ElMessage, ElMessageBox } from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/dialog/style/css'
import 'element-plus/es/components/form/style/css'
import 'element-plus/es/components/form-item/style/css'
import 'element-plus/es/components/input/style/css'
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'

import { issuesApi } from '@/api/issues'
import { getApiErrorMessage } from '@/api/client'
import MarkdownViewer from '@/components/artifact/MarkdownViewer.vue'
import CommentSection from '@/components/common/CommentSection.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { routeNames } from '@/router/route-names'
import { useSessionStore } from '@/stores/session'
import type { IssueRead } from '@/types/issue'
import { formatDate } from '@/utils/format'
import { issueStatusLabels, issueStatusTones } from '@/utils/status'

const route = useRoute()
const session = useSessionStore()
const issue = ref<IssueRead | null>(null)
const loading = ref(false)
const actionLoading = ref(false)
const errorMessage = ref('')

const editDialogVisible = ref(false)
const editFormRef = ref<FormInstance>()
const editSubmitting = ref(false)
const editForm = reactive({ title: '', description: '' })

const editRules: FormRules = {
  title: [
    { required: true, message: '请输入标题', trigger: 'blur' },
    { max: 200, message: '标题最多 200 个字符', trigger: 'blur' },
  ],
  description: [
    { required: true, message: '请输入正文', trigger: 'blur' },
    { max: 100000, message: '正文内容过长', trigger: 'blur' },
  ],
}

const issueId = computed(() => Number(route.params.id))
const isAuthor = computed(() => issue.value?.author.id === session.currentUser?.id)
const canManageState = computed(() => isAuthor.value || session.isAdmin)
const isOpen = computed(() => issue.value?.status === 'OPEN')

async function loadIssue(): Promise<void> {
  if (!Number.isInteger(issueId.value) || issueId.value <= 0) {
    errorMessage.value = 'Issue 编号无效'
    return
  }
  loading.value = true
  errorMessage.value = ''
  try {
    issue.value = await issuesApi.get(issueId.value)
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, 'Issue 加载失败')
  } finally {
    loading.value = false
  }
}

async function runAction(
  confirmText: string | null,
  confirmTitle: string,
  action: () => Promise<IssueRead>,
  successText: string,
): Promise<void> {
  if (!issue.value) return
  try {
    if (confirmText) {
      await ElMessageBox.confirm(confirmText, confirmTitle, { type: 'warning' })
    }
    actionLoading.value = true
    issue.value = await action()
    ElMessage.success(successText)
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      ElMessage.error(getApiErrorMessage(error, '操作失败'))
    }
  } finally {
    actionLoading.value = false
  }
}

function closeIssue(): void {
  void runAction(
    '确认关闭 Issue 吗？关闭后正文和评论都变为只读。',
    '关闭 Issue',
    () => issuesApi.close(issue.value!.id),
    'Issue 已关闭',
  )
}

function reopenIssue(): void {
  void runAction(
    null,
    '',
    () => issuesApi.reopen(issue.value!.id),
    'Issue 已重新开放',
  )
}

function openEditDialog(): void {
  if (!issue.value) return
  editForm.title = issue.value.title
  editForm.description = issue.value.description
  editDialogVisible.value = true
  editFormRef.value?.clearValidate()
}

async function saveEdit(): Promise<void> {
  const valid = await editFormRef.value?.validate().catch(() => false)
  if (!valid || !issue.value) return
  editSubmitting.value = true
  try {
    issue.value = await issuesApi.update(issue.value.id, {
      title: editForm.title.trim(),
      description: editForm.description.trim(),
    })
    editDialogVisible.value = false
    ElMessage.success('Issue 已保存')
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, 'Issue 保存失败'))
  } finally {
    editSubmitting.value = false
  }
}

watch(issueId, () => void loadIssue())
onMounted(() => void loadIssue())
</script>

<template>
  <div class="page-container module-page artifact-detail-page">
    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载 Issue…</p>
    <template v-else-if="issue">
      <nav class="detail-breadcrumb" aria-label="面包屑">
        <RouterLink :to="{ name: routeNames.issues }">Issues</RouterLink>
        <span>/</span>
        <span>{{ issue.title }}</span>
      </nav>

      <article class="artifact-detail-layout">
        <main class="panel-card artifact-detail-main">
          <div class="artifact-detail-heading">
            <StatusBadge :label="issueStatusLabels[issue.status]" :tone="issueStatusTones[issue.status]" />
            <h1>{{ issue.title }}</h1>
            <div class="artifact-detail-meta">
              <span>发起人：{{ issue.author.display_name }}</span>
              <span>创建：{{ formatDate(issue.created_at) }}</span>
              <span>更新：{{ formatDate(issue.updated_at) }}</span>
              <span v-if="issue.closed_at">关闭：{{ formatDate(issue.closed_at) }}</span>
            </div>
          </div>

          <section class="artifact-detail-section">
            <div class="detail-section-heading"><h2>正文</h2></div>
            <MarkdownViewer :content="issue.description" />
          </section>

          <CommentSection
            target-type="issue"
            :target-id="issue.id"
            :can-comment="issue.status === 'OPEN'"
          />
        </main>

        <aside class="artifact-detail-side">
          <section class="panel-card artifact-action-card">
            <h2>Issue 操作</h2>
            <ElButton v-if="isAuthor && isOpen" @click="openEditDialog">编辑内容</ElButton>
            <ElButton v-if="canManageState && isOpen" type="warning" plain :loading="actionLoading" @click="closeIssue">
              关闭 Issue
            </ElButton>
            <ElButton
              v-if="canManageState && !isOpen"
              type="primary"
              :loading="actionLoading"
              @click="reopenIssue"
            >
              重新开放
            </ElButton>
            <p v-if="!isOpen" class="muted-copy">Issue 已关闭，正文和评论只读。</p>
            <p v-else-if="!isAuthor" class="muted-copy">你可以评论这条 Issue；只有发起人和管理员可以关闭。</p>
          </section>
        </aside>
      </article>

      <ElDialog v-model="editDialogVisible" title="编辑 Issue" width="min(680px, 92vw)">
        <ElForm ref="editFormRef" :model="editForm" :rules="editRules" label-position="top">
          <ElFormItem label="标题" prop="title">
            <ElInput v-model="editForm.title" maxlength="200" show-word-limit />
          </ElFormItem>
          <ElFormItem label="Markdown 正文" prop="description">
            <ElInput
              v-model="editForm.description"
              class="markdown-editor-input"
              type="textarea"
              :rows="12"
              maxlength="100000"
            />
          </ElFormItem>
        </ElForm>
        <template #footer>
          <div class="dialog-actions">
            <ElButton @click="editDialogVisible = false">取消</ElButton>
            <ElButton type="primary" :loading="editSubmitting" @click="saveEdit">保存</ElButton>
          </div>
        </template>
      </ElDialog>
    </template>
  </div>
</template>
