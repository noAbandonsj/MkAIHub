<script setup lang="ts">
import { routeNames } from '@/router/route-names'
import AppPageHeader from '@/components/common/AppPageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
</script>

<template>
  <div class="page-container module-page">
    <AppPageHeader
      title="任务"
      description="查看和发布内部 AI 相关任务。任务数据接口将在后续批次接入。"
      action-label="新建任务"
      :action-to="{ name: routeNames.taskNew }"
    />
    <section class="panel-card">
      <EmptyState description="暂时没有可展示的任务" />
    </section>
  </div>
</template>
