<script setup lang="ts">
import StatusBadge from '@/components/common/StatusBadge.vue'
import type { ArtifactStatus } from '@/types/artifact'
import type { StatusTone } from '@/utils/status'

defineProps<{ status: ArtifactStatus }>()

const labels: Record<ArtifactStatus, string> = {
  DRAFT: '草稿',
  PUBLISHED: '已发布',
  ARCHIVED: '已归档',
}

const tones: Record<ArtifactStatus, StatusTone> = {
  DRAFT: 'pending',
  PUBLISHED: 'done',
  ARCHIVED: 'closed',
}
</script>

<template>
  <StatusBadge :label="labels[status]" :tone="tones[status]" />
</template>
