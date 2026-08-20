<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { ElAlert, ElButton, ElInput, ElMessage, ElMessageBox } from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/input/style/css'
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'

import { adminApi } from '@/api/admin'
import { artifactsApi } from '@/api/artifacts'
import { getApiErrorMessage } from '@/api/client'
import { issuesApi } from '@/api/issues'
import { useSessionStore } from '@/stores/session'
import type { CommentListResponse, CommentRead } from '@/types/artifact'
import { formatDate } from '@/utils/format'

type CommentTarget = 'artifact' | 'issue'

const props = defineProps<{
  targetType: CommentTarget
  targetId: number
  canComment: boolean
}>()

const session = useSessionStore()
const comments = ref<CommentRead[]>([])
const content = ref('')
const loading = ref(false)
const submitting = ref(false)
const errorMessage = ref('')

function listComments(): Promise<CommentListResponse> {
  return props.targetType === 'artifact'
    ? artifactsApi.listComments(props.targetId)
    : issuesApi.listComments(props.targetId)
}

async function loadComments(): Promise<void> {
  loading.value = true
  errorMessage.value = ''
  try {
    const response = await listComments()
    comments.value = response.items
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '评论加载失败')
  } finally {
    loading.value = false
  }
}

async function submitComment(): Promise<void> {
  const value = content.value.trim()
  if (!value) {
    ElMessage.warning('请输入评论内容')
    return
  }
  submitting.value = true
  try {
    const created = props.targetType === 'artifact'
      ? await artifactsApi.createComment(props.targetId, value)
      : await issuesApi.createComment(props.targetId, value)
    comments.value.push(created)
    content.value = ''
    ElMessage.success('评论已发表')
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '评论发表失败'))
  } finally {
    submitting.value = false
  }
}

async function deleteComment(comment: CommentRead): Promise<void> {
  try {
    await ElMessageBox.confirm('确定删除这条评论吗？', '删除评论', { type: 'warning' })
    await artifactsApi.deleteComment(comment.id)
    comments.value = comments.value.filter((item) => item.id !== comment.id)
    ElMessage.success('评论已删除')
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    ElMessage.error(getApiErrorMessage(error, '评论删除失败'))
  }
}

// Moderation is administrator-only; the API only returns hidden comments to
// administrators, so employees never render the HIDDEN branch.
async function moderateComment(comment: CommentRead, hide: boolean): Promise<void> {
  try {
    if (hide) {
      await ElMessageBox.confirm('确定隐藏这条评论吗？普通员工将看不到它。', '隐藏评论', { type: 'warning' })
      await adminApi.hideComment(comment.id)
    } else {
      await adminApi.restoreComment(comment.id)
    }
    comment.status = hide ? 'HIDDEN' : 'VISIBLE'
    ElMessage.success(hide ? '评论已隐藏' : '评论已恢复')
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    ElMessage.error(getApiErrorMessage(error, '操作失败'))
  }
}

watch(() => [props.targetType, props.targetId], () => void loadComments())
onMounted(() => void loadComments())
</script>

<template>
  <section class="comment-section" aria-labelledby="comment-section-title">
    <div class="detail-section-heading">
      <h2 id="comment-section-title">评论</h2>
      <span>{{ comments.length }} 条</span>
    </div>
    <ElAlert v-if="errorMessage" :title="errorMessage" type="error" :closable="false" />
    <div v-if="canComment" class="comment-compose">
      <ElInput
        v-model="content"
        type="textarea"
        :rows="3"
        maxlength="2000"
        show-word-limit
        placeholder="写下你的评论，与同事交流实践经验"
      />
      <ElButton type="primary" :loading="submitting" @click="submitComment">发表评论</ElButton>
    </div>
    <p v-else class="muted-copy">当前状态不可新增评论。</p>
    <p v-if="loading" class="muted-copy">正在加载评论…</p>
    <div v-else-if="comments.length" class="comment-list">
      <article v-for="comment in comments" :key="comment.id" class="comment-item" :class="{ 'comment-item--hidden': comment.status === 'HIDDEN' }">
        <div class="comment-item__avatar">{{ comment.author.display_name.slice(0, 1) }}</div>
        <div class="comment-item__body">
          <div class="comment-item__head">
            <strong>{{ comment.author.display_name }}</strong>
            <span v-if="comment.status === 'HIDDEN'" class="comment-hidden-badge">已隐藏</span>
            <span>{{ formatDate(comment.created_at) }}</span>
            <button
              v-if="comment.author.id === session.currentUser?.id"
              class="text-action is-danger"
              type="button"
              @click="deleteComment(comment)"
            >
              删除
            </button>
            <button
              v-if="session.isAdmin && comment.status !== 'HIDDEN'"
              class="text-action"
              type="button"
              @click="moderateComment(comment, true)"
            >
              隐藏
            </button>
            <button
              v-else-if="session.isAdmin"
              class="text-action"
              type="button"
              @click="moderateComment(comment, false)"
            >
              恢复
            </button>
          </div>
          <p>{{ comment.content }}</p>
        </div>
      </article>
    </div>
    <p v-else-if="!loading" class="muted-copy">还没有评论，欢迎分享你的看法。</p>
  </section>
</template>
