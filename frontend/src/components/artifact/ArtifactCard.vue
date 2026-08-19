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
      <h3>{{ artifact.title }}</h3>
      <p>{{ artifact.summary }}</p>
      <div class="artifact-card__meta">
        <span>{{ artifact.author.display_name }}</span>
        <span>{{ formatDate(artifact.published_at || artifact.updated_at) }}</span>
      </div>
    </div>
  </RouterLink>
</template>
