<script setup lang="ts">
import { routeNames } from '@/router/route-names'
import AppPageHeader from '@/components/common/AppPageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
</script>

<template>
  <div class="page-container module-page">
    <AppPageHeader
      title="展品"
      description="集中分享和复用团队的 AI 实践内容。首版不设置类型、分类或标签。"
      action-label="新建展品"
      :action-to="{ name: routeNames.artifactNew }"
    />
    <section class="panel-card">
      <EmptyState description="暂时没有可展示的展品" />
    </section>
  </div>
</template>
