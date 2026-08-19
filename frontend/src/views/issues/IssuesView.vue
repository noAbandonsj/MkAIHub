<script setup lang="ts">
import { routeNames } from '@/router/route-names'
import AppPageHeader from '@/components/common/AppPageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
</script>

<template>
  <div class="page-container module-page">
    <AppPageHeader
      title="Issues"
      description="记录平台建议、问题和内部讨论。Issue 数据接口将在后续批次接入。"
      action-label="发起 Issue"
      :action-to="{ name: routeNames.issueNew }"
    />
    <section class="panel-card">
      <EmptyState description="暂时没有可展示的 Issue" />
    </section>
  </div>
</template>
