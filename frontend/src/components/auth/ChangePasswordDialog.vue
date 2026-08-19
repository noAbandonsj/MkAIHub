<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import { ElAlert, ElButton, ElDialog, ElForm, ElFormItem, ElInput } from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/dialog/style/css'
import 'element-plus/es/components/form/style/css'
import 'element-plus/es/components/form-item/style/css'
import 'element-plus/es/components/input/style/css'

import { ApiError, getApiErrorMessage } from '@/api/client'
import { useSessionStore } from '@/stores/session'

const props = defineProps<{
  modelValue: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  completed: []
  expired: []
}>()

const session = useSessionStore()
const formRef = ref<FormInstance>()
const errorMessage = ref<string | null>(null)
const form = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: '',
})

const validateConfirmPassword = (_rule: unknown, value: string, callback: (error?: Error) => void) => {
  if (value !== form.newPassword) {
    callback(new Error('两次输入的新密码不一致'))
    return
  }
  callback()
}

const rules: FormRules = {
  currentPassword: [
    { required: true, message: '请输入当前密码', trigger: 'blur' },
  ],
  newPassword: [
    { required: true, message: '请输入新密码', trigger: 'blur' },
    { min: 8, max: 128, message: '密码长度必须为 8-128 位', trigger: 'blur' },
  ],
  confirmPassword: [
    { required: true, message: '请再次输入新密码', trigger: 'blur' },
    { validator: validateConfirmPassword, trigger: 'blur' },
  ],
}

function resetForm(): void {
  form.currentPassword = ''
  form.newPassword = ''
  form.confirmPassword = ''
  errorMessage.value = null
  formRef.value?.clearValidate()
}

watch(
  () => props.modelValue,
  (visible) => {
    if (visible) {
      resetForm()
    }
  },
)

async function submit(): Promise<void> {
  errorMessage.value = null
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid || session.isLoading) {
    return
  }

  try {
    await session.changePassword(form.currentPassword, form.newPassword)
    emit('update:modelValue', false)
    emit('completed')
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      session.clearSession()
      emit('update:modelValue', false)
      emit('expired')
      return
    }
    errorMessage.value = getApiErrorMessage(error, session.error || '修改密码失败')
  }
}
</script>

<template>
  <ElDialog
    :model-value="modelValue"
    title="修改密码"
    width="min(440px, calc(100vw - 32px))"
    @update:model-value="emit('update:modelValue', $event)"
  >
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
      :model="form"
      :rules="rules"
      label-position="top"
      @submit.prevent="submit"
    >
      <ElFormItem label="当前密码" prop="currentPassword">
        <ElInput
          v-model="form.currentPassword"
          type="password"
          show-password
          autocomplete="current-password"
        />
      </ElFormItem>
      <ElFormItem label="新密码" prop="newPassword">
        <ElInput
          v-model="form.newPassword"
          type="password"
          show-password
          autocomplete="new-password"
        />
      </ElFormItem>
      <ElFormItem label="确认新密码" prop="confirmPassword">
        <ElInput
          v-model="form.confirmPassword"
          type="password"
          show-password
          autocomplete="new-password"
        />
      </ElFormItem>
      <div class="dialog-actions">
        <ElButton @click="emit('update:modelValue', false)">取消</ElButton>
        <ElButton type="primary" native-type="submit" :loading="session.isLoading">确认修改</ElButton>
      </div>
    </ElForm>
  </ElDialog>
</template>
