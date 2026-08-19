<script setup lang="ts">
import type { ArtifactStatus } from '@/types/artifact'

defineProps<{ status: ArtifactStatus }>()

const labels: Record<ArtifactStatus, string> = {
  DRAFT: '草稿',
  PUBLISHED: '已发布',
  ARCHIVED: '已归档',
}
</script>

<template>
  <span class="artifact-status" :class="`is-${status.toLowerCase()}`">{{ labels[status] }}</span>
</template>
