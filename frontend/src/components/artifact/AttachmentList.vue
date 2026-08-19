<script setup lang="ts">
import { artifactsApi } from '@/api/artifacts'
import type { StoredFileRead } from '@/types/artifact'
import { formatFileSize } from '@/utils/format'

defineProps<{ files: StoredFileRead[] }>()
</script>

<template>
  <div v-if="files.length" class="attachment-list">
    <a
      v-for="file in files"
      :key="file.id"
      class="attachment-item"
      :href="artifactsApi.downloadUrl(file.id)"
    >
      <span class="attachment-item__extension">{{ file.extension.toUpperCase() }}</span>
      <span class="attachment-item__name">{{ file.original_name }}</span>
      <span class="attachment-item__size">{{ formatFileSize(file.size_bytes) }}</span>
      <span class="attachment-item__action">下载</span>
    </a>
  </div>
  <p v-else class="muted-copy">没有附件</p>
</template>
