<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { FormInstance, FormRules } from 'element-plus'
import { ElAlert, ElButton, ElForm, ElFormItem, ElInput } from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/form/style/css'
import 'element-plus/es/components/form-item/style/css'
import 'element-plus/es/components/input/style/css'

import { ApiError, getApiErrorMessage } from '@/api/client'
import { resolveLoginRedirect } from '@/router/guards'
import { useSessionStore } from '@/stores/session'

const route = useRoute()
const router = useRouter()
const session = useSessionStore()
const formRef = ref<FormInstance>()
const form = reactive({ username: '', password: '' })
const errorMessage = ref<string | null>(null)

const rules: FormRules = {
  username: [
    { required: true, message: '请输入用户名', trigger: 'blur' },
  ],
  password: [
    { required: true, message: '请输入密码', trigger: 'blur' },
    { min: 8, max: 128, message: '密码长度必须为 8-128 位', trigger: 'blur' },
  ],
}

async function submit(): Promise<void> {
  errorMessage.value = null
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid || session.isLoading) {
    return
  }

  try {
    await session.login(form)
    await router.replace(resolveLoginRedirect(route.query.redirect))
  } catch (error) {
    errorMessage.value = error instanceof ApiError && error.code === 'INVALID_CREDENTIALS'
      ? '用户名或密码错误'
      : getApiErrorMessage(error, session.error || '登录失败')
  }
}
</script>

<template>
  <main class="auth-page">
    <section class="auth-card" aria-labelledby="login-title">
      <div class="auth-brand">MkAIHub</div>
      <h1 id="login-title">登录</h1>
      <p class="auth-description">使用管理员创建的 MkAIHub 账号登录。</p>
      <ElAlert
        v-if="errorMessage"
        :title="errorMessage"
        type="error"
        :closable="false"
        show-icon
        role="alert"
      />
      <ElForm
        ref="formRef"
        class="auth-form"
        :model="form"
        :rules="rules"
        label-position="top"
        @submit.prevent="submit"
      >
        <ElFormItem label="用户名" prop="username">
          <ElInput
            v-model="form.username"
            autocomplete="username"
            autofocus
            placeholder="请输入用户名"
          />
        </ElFormItem>
        <ElFormItem label="密码" prop="password">
          <ElInput
            v-model="form.password"
            type="password"
            show-password
            autocomplete="current-password"
            placeholder="请输入密码"
          />
        </ElFormItem>
        <ElButton
          class="auth-button"
          type="primary"
          native-type="submit"
          :loading="session.isLoading"
        >
          登录
        </ElButton>
      </ElForm>
    </section>
  </main>
</template>
