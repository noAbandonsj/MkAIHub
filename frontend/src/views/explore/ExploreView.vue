<script setup lang="ts">
import { RouterLink } from 'vue-router'

import { routeNames } from '@/router/route-names'
import { primaryNavigation } from '@/types/navigation'
import EmptyState from '@/components/common/EmptyState.vue'
</script>

<template>
  <div class="page-container explore-page">
    <section class="explore-hero">
      <p class="hero-eyebrow">内部 AI 分享平台</p>
      <h1 class="hero-title">发现、分享并复用团队的 AI 实践</h1>
      <p class="hero-description">
        MkAIHub 为团队保留一个清晰的入口，让有价值的经验逐步沉淀下来。
      </p>
      <RouterLink class="primary-action" :to="{ name: routeNames.artifacts }">浏览展品</RouterLink>
    </section>

    <section class="content-section" aria-labelledby="module-entry-title">
      <div class="section-heading">
        <div>
          <p class="section-kicker">START HERE</p>
          <h2 id="module-entry-title">模块入口</h2>
        </div>
        <span class="section-note">六个一级模块</span>
      </div>
      <div class="module-grid">
        <RouterLink
          v-for="item in primaryNavigation"
          :key="item.route"
          class="module-card"
          :to="{ name: item.route }"
        >
          <span class="module-card__label">{{ item.label }}</span>
          <span class="module-card__description">{{ item.description }}</span>
          <span class="module-card__arrow" aria-hidden="true">→</span>
        </RouterLink>
      </div>
    </section>

    <section class="content-section" aria-labelledby="latest-artifacts-title">
      <div class="section-heading">
        <div>
          <p class="section-kicker">RECENTLY PUBLISHED</p>
          <h2 id="latest-artifacts-title">最新展品</h2>
        </div>
        <RouterLink class="section-link" :to="{ name: routeNames.artifacts }">查看全部</RouterLink>
      </div>
      <div class="panel-card">
        <EmptyState description="暂时还没有已发布的展品" />
      </div>
    </section>
  </div>
</template>
