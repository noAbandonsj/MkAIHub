<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import 'element-plus/es/components/message/style/css'

import ArtifactEditor from '@/components/artifact/ArtifactEditor.vue'
import { routeNames } from '@/router/route-names'
import type { ArtifactRead } from '@/types/artifact'

const route = useRoute()
const router = useRouter()
const isEditing = computed(() => route.name === routeNames.artifactEdit)
const artifactId = computed(() => Number(route.params.id))
const pageTitle = computed(() => isEditing.value ? '编辑展品' : '新建展品')

async function handleSaved(artifact: ArtifactRead, published: boolean): Promise<void> {
  ElMessage.success(published ? '展品已发布' : '展品已保存')
  await router.replace({ name: routeNames.artifactDetail, params: { id: artifact.id } })
}
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

    <ArtifactEditor
      :key="route.fullPath"
      :mode="isEditing ? 'edit' : 'create'"
      :artifact-id="isEditing ? artifactId : null"
      @saved="handleSaved"
    />
  </div>
</template>
