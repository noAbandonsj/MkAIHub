<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElAlert, ElButton, ElMessage, ElMessageBox } from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'

import { artifactsApi } from '@/api/artifacts'
import { getApiErrorMessage } from '@/api/client'
import ArtifactStatusBadge from '@/components/artifact/ArtifactStatusBadge.vue'
import AttachmentList from '@/components/artifact/AttachmentList.vue'
import CommentSection from '@/components/artifact/CommentSection.vue'
import MarkdownViewer from '@/components/artifact/MarkdownViewer.vue'
import { routeNames } from '@/router/route-names'
import { useSessionStore } from '@/stores/session'
import type { ArtifactRead } from '@/types/artifact'
import { formatDate } from '@/utils/format'

const route = useRoute()
const router = useRouter()
const session = useSessionStore()
const artifact = ref<ArtifactRead | null>(null)
const loading = ref(false)
const actionLoading = ref(false)
const errorMessage = ref('')

const artifactId = computed(() => Number(route.params.id))
const isAuthor = computed(() => artifact.value?.author.id === session.currentUser?.id)
const canManageState = computed(() => isAuthor.value || session.isAdmin)

async function loadArtifact(): Promise<void> {
  if (!Number.isInteger(artifactId.value) || artifactId.value <= 0) {
    errorMessage.value = '展品编号无效'
    return
  }
  loading.value = true
  errorMessage.value = ''
  try {
    artifact.value = await artifactsApi.get(artifactId.value)
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '展品加载失败')
  } finally {
    loading.value = false
  }
}

async function publish(): Promise<void> {
  if (!artifact.value) return
  actionLoading.value = true
  try {
    artifact.value = await artifactsApi.publish(artifact.value.id)
    ElMessage.success('展品已发布')
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '发布失败'))
  } finally {
    actionLoading.value = false
  }
}

async function archive(): Promise<void> {
  if (!artifact.value) return
  try {
    await ElMessageBox.confirm('归档后将不再出现在公开列表，确定继续吗？', '归档展品', { type: 'warning' })
    actionLoading.value = true
    artifact.value = await artifactsApi.archive(artifact.value.id)
    ElMessage.success('展品已归档')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') ElMessage.error(getApiErrorMessage(error, '归档失败'))
  } finally {
    actionLoading.value = false
  }
}

async function restore(): Promise<void> {
  if (!artifact.value) return
  actionLoading.value = true
  try {
    artifact.value = await artifactsApi.restore(artifact.value.id)
    ElMessage.success('展品已恢复')
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '恢复失败'))
  } finally {
    actionLoading.value = false
  }
}

async function deleteDraft(): Promise<void> {
  if (!artifact.value) return
  try {
    await ElMessageBox.confirm('草稿删除后无法恢复，确定删除吗？', '删除草稿', { type: 'warning' })
    actionLoading.value = true
    await artifactsApi.deleteDraft(artifact.value.id)
    ElMessage.success('草稿已删除')
    await router.replace({ name: routeNames.artifacts })
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') ElMessage.error(getApiErrorMessage(error, '删除失败'))
  } finally {
    actionLoading.value = false
  }
}

watch(artifactId, () => void loadArtifact())
onMounted(() => void loadArtifact())
</script>

<template>
  <div class="page-container module-page artifact-detail-page">
    <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
    <p v-if="loading" class="muted-copy page-loading">正在加载展品…</p>
    <template v-else-if="artifact">
      <nav class="detail-breadcrumb" aria-label="面包屑">
        <RouterLink :to="{ name: routeNames.artifacts }">展品</RouterLink>
        <span>/</span>
        <span>{{ artifact.title }}</span>
      </nav>

      <article class="artifact-detail-layout">
        <main class="panel-card artifact-detail-main">
          <div class="artifact-detail-heading">
            <ArtifactStatusBadge :status="artifact.status" />
            <h1>{{ artifact.title }}</h1>
            <p class="artifact-detail-summary">{{ artifact.summary }}</p>
            <div class="artifact-detail-meta">
              <span>作者：{{ artifact.author.display_name }}</span>
              <span>更新：{{ formatDate(artifact.updated_at) }}</span>
              <span v-if="artifact.published_at">发布：{{ formatDate(artifact.published_at) }}</span>
            </div>
          </div>

          <section class="artifact-detail-section">
            <div class="detail-section-heading"><h2>正文</h2></div>
            <MarkdownViewer :content="artifact.content_markdown" />
          </section>

          <section class="artifact-detail-section">
            <div class="detail-section-heading">
              <h2>附件</h2>
              <span>{{ artifact.files.length }} 个</span>
            </div>
            <AttachmentList :files="artifact.files" />
          </section>

          <CommentSection :artifact-id="artifact.id" :status="artifact.status" />
        </main>

        <aside class="artifact-detail-side">
          <section class="panel-card artifact-action-card">
            <h2>展品操作</h2>
            <RouterLink
              v-if="isAuthor && artifact.status !== 'ARCHIVED'"
              class="secondary-action full-width-action"
              :to="{ name: routeNames.artifactEdit, params: { id: artifact.id } }"
            >
              编辑内容
            </RouterLink>
            <ElButton
              v-if="isAuthor && artifact.status === 'DRAFT'"
              type="primary"
              :loading="actionLoading"
              @click="publish"
            >
              发布展品
            </ElButton>
            <ElButton
              v-if="canManageState && artifact.status === 'PUBLISHED'"
              :loading="actionLoading"
              @click="archive"
            >
              归档展品
            </ElButton>
            <ElButton
              v-if="canManageState && artifact.status === 'ARCHIVED'"
              type="primary"
              :loading="actionLoading"
              @click="restore"
            >
              恢复发布
            </ElButton>
            <ElButton
              v-if="isAuthor && artifact.status === 'DRAFT'"
              type="danger"
              plain
              :loading="actionLoading"
              @click="deleteDraft"
            >
              删除草稿
            </ElButton>
            <p v-if="!isAuthor && !session.isAdmin" class="muted-copy">你可以查看、下载和评论这条展品。</p>
          </section>
        </aside>
      </article>
    </template>
  </div>
</template>
