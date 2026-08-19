<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'

defineProps<{
  title: string
  description: string
  actionLabel?: string
  actionTo?: RouteLocationRaw
}>()
</script>

<template>
  <header class="page-header">
    <div>
      <p class="page-eyebrow">MkAIHub</p>
      <h1 class="page-title">{{ title }}</h1>
      <p class="page-description">{{ description }}</p>
    </div>
    <RouterLink v-if="actionLabel && actionTo" class="primary-action" :to="actionTo">
      {{ actionLabel }}
    </RouterLink>
  </header>
</template>
