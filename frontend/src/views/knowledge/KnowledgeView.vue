<script setup lang="ts">
import { RouterLink } from 'vue-router'

import { routeNames } from '@/router/route-names'
import EmptyState from '@/components/common/EmptyState.vue'
</script>

<template>
  <div class="page-container module-page">
    <header class="page-header">
      <div>
        <p class="page-eyebrow">MkAIHub</p>
        <h1 class="page-title">知识库</h1>
        <p class="page-description">知识管理能力正在规划中，当前版本保留独立导航和占位页面。</p>
      </div>
    </header>

    <section class="panel-card knowledge-placeholder" aria-labelledby="knowledge-placeholder-title">
      <p class="section-kicker">COMING LATER</p>
      <h2 id="knowledge-placeholder-title">知识库占位页</h2>
      <p>
        首版不建立独立知识库数据模型、列表、详情或 API，也不会复制展品数据。
        后续将在需求明确后决定知识内容的建设方式。
      </p>
      <EmptyState description="知识库能力将在后续批次建设">
        <RouterLink class="secondary-action" :to="{ name: routeNames.artifacts }">先去查看展品</RouterLink>
      </EmptyState>
    </section>
  </div>
</template>
