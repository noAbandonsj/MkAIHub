<script setup lang="ts">
import { routeNames } from '@/router/route-names'
import AppPageHeader from '@/components/common/AppPageHeader.vue'
import EmptyState from '@/components/common/EmptyState.vue'
</script>

<template>
  <div class="page-container module-page">
    <AppPageHeader
      title="竞赛"
      description="查看内部竞赛的基本介绍、时间和规则。首版不实现报名、组队、提交和评审。"
    />
    <section class="panel-card">
      <EmptyState description="暂时没有可展示的竞赛">
        <RouterLink class="secondary-action" :to="{ name: routeNames.explore }">返回探索</RouterLink>
      </EmptyState>
    </section>
  </div>
</template>
