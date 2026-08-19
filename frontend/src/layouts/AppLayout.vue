<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { ElButton, ElMessage } from 'element-plus'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/message/style/css'

import { getApiErrorMessage } from '@/api/client'
import ChangePasswordDialog from '@/components/auth/ChangePasswordDialog.vue'
import { routeNames } from '@/router/route-names'
import { useSessionStore } from '@/stores/session'
import { primaryNavigation } from '@/types/navigation'

const route = useRoute()
const router = useRouter()
const session = useSessionStore()
const isChangePasswordOpen = ref(false)
const activeNavigation = computed(() => route.meta.navRoute ?? route.name)
const displayName = computed(() => session.currentUser?.display_name || session.currentUser?.username || '')

async function logout(): Promise<void> {
  try {
    await session.logout()
  } catch (error) {
    ElMessage.error(getApiErrorMessage(error, '退出失败，请稍后重试'))
  } finally {
    await router.replace({ name: routeNames.login })
  }
}

async function handlePasswordChanged(): Promise<void> {
  isChangePasswordOpen.value = false
  await router.replace({ name: routeNames.login })
}

async function handleSessionExpired(): Promise<void> {
  isChangePasswordOpen.value = false
  await router.replace({
    name: routeNames.login,
    query: { redirect: route.fullPath },
  })
}
</script>

<template>
  <div class="app-layout">
    <header class="app-header">
      <div class="app-header__inner page-container">
        <RouterLink class="brand" :to="{ name: routeNames.explore }" aria-label="MkAIHub 探索">
          MkAIHub
        </RouterLink>

        <nav class="primary-nav" aria-label="主导航">
          <RouterLink
            v-for="item in primaryNavigation"
            :key="item.route"
            class="primary-nav__item"
            :class="{ 'is-active': activeNavigation === item.route }"
            :to="{ name: item.route }"
          >
            {{ item.label }}
          </RouterLink>
        </nav>

        <div class="app-header__actions" v-if="session.isAuthenticated">
          <span class="user-summary" :title="session.currentUser?.username">
            {{ displayName }}
          </span>
          <RouterLink v-if="session.isAdmin" class="admin-link" :to="{ name: routeNames.admin }">
            管理
          </RouterLink>
          <ElButton class="header-button" link type="primary" @click="isChangePasswordOpen = true">
            修改密码
          </ElButton>
          <ElButton class="header-button" link type="danger" :loading="session.isLoading" @click="logout">
            退出
          </ElButton>
        </div>
      </div>
    </header>

    <main class="app-main">
      <RouterView />
    </main>

    <footer class="app-footer">
      <div class="page-container app-footer__inner">
        <span>MkAIHub · 内部 AI 分享平台</span>
        <span>首版工程骨架</span>
      </div>
    </footer>

    <ChangePasswordDialog
      v-model="isChangePasswordOpen"
      @completed="handlePasswordChanged"
      @expired="handleSessionExpired"
    />
  </div>
</template>
