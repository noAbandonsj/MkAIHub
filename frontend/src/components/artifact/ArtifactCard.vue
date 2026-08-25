<script setup lang="ts">
import { routeNames } from '@/router/route-names'
import type { ArtifactListItem } from '@/types/artifact'
import { formatDate } from '@/utils/format'
import ArtifactStatusBadge from './ArtifactStatusBadge.vue'

defineProps<{
  artifact: ArtifactListItem
}>()
</script>

<template>
  <RouterLink
    class="artifact-card"
    :to="{ name: routeNames.artifactDetail, params: { id: artifact.id } }"
  >
    <div class="artifact-card__visual" aria-hidden="true">
      <span>AI</span>
    </div>
    <div class="artifact-card__content">
      <div class="artifact-card__topline">
        <ArtifactStatusBadge :status="artifact.status" />
        <span v-if="artifact.attachment_count" class="artifact-card__files">
          {{ artifact.attachment_count }} 个附件
        </span>
      </div>
      <div v-if="artifact.source_types.length" class="artifact-card__sources">
        <span v-if="artifact.source_types.includes('TASK_RESULT')" class="source-badge">任务成果</span>
        <span v-if="artifact.source_types.includes('COMPETITION_ENTRY')" class="source-badge is-competition">
          竞赛作品
        </span>
      </div>
      <h3>{{ artifact.title }}</h3>
      <p>{{ artifact.summary }}</p>
      <div class="artifact-card__meta">
        <span>{{ artifact.author.display_name }}</span>
        <span>{{ formatDate(artifact.published_at || artifact.updated_at) }}</span>
      </div>
    </div>
  </RouterLink>
</template>
