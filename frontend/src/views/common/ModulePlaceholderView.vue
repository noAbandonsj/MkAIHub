<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

import { routeNames } from '@/router/route-names'
import EmptyState from '@/components/common/EmptyState.vue'

const route = useRoute()
const title = computed(() => route.meta.label || '页面')
const identifier = computed(() => {
  const value = route.params.id
  return typeof value === 'string' ? value : undefined
})
</script>

<template>
  <div class="page-container module-page">
    <header class="page-header">
      <div>
        <p class="page-eyebrow">MkAIHub</p>
        <h1 class="page-title">{{ title }}</h1>
        <p class="page-description">
          这是首版页面路由骨架，业务表单和详情能力将在对应批次接入。
          <span v-if="identifier">当前标识：{{ identifier }}</span>
        </p>
      </div>
    </header>
    <section class="panel-card">
      <EmptyState :description="`${title}功能将在后续批次接入`">
        <RouterLink class="secondary-action" :to="{ name: routeNames.explore }">返回探索</RouterLink>
      </EmptyState>
    </section>
  </div>
</template>
