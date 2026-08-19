<script setup lang="ts">
import { ElEmpty } from 'element-plus'
import 'element-plus/es/components/empty/style/css'

withDefaults(
  defineProps<{
    description: string
    imageSize?: number
  }>(),
  { imageSize: 120 },
)
</script>

<template>
  <div class="empty-state">
    <ElEmpty :description="description" :image-size="imageSize" />
    <slot />
  </div>
</template>
