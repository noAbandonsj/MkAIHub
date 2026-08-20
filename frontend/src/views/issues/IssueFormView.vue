<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { FormInstance, FormRules } from 'element-plus'
import { ElAlert, ElButton, ElForm, ElFormItem, ElInput, ElMessage } from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/form/style/css'
import 'element-plus/es/components/form-item/style/css'
import 'element-plus/es/components/input/style/css'
import 'element-plus/es/components/message/style/css'

import { issuesApi } from '@/api/issues'
import { getApiErrorMessage } from '@/api/client'
import MarkdownViewer from '@/components/artifact/MarkdownViewer.vue'
import { routeNames } from '@/router/route-names'

const router = useRouter()
const formRef = ref<FormInstance>()
const submitting = ref(false)
const errorMessage = ref('')
const form = reactive({ title: '', description: '' })

const rules: FormRules = {
  title: [
    { required: true, message: '请输入标题', trigger: 'blur' },
    { max: 200, message: '标题最多 200 个字符', trigger: 'blur' },
  ],
  description: [
    { required: true, message: '请输入正文', trigger: 'blur' },
    { max: 100000, message: '正文内容过长', trigger: 'blur' },
  ],
}

async function submit(): Promise<void> {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  submitting.value = true
  errorMessage.value = ''
  try {
    const issue = await issuesApi.create({
      title: form.title.trim(),
      description: form.description.trim(),
    })
    ElMessage.success('Issue 已发起')
    await router.replace({ name: routeNames.issueDetail, params: { id: issue.id } })
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, 'Issue 发起失败')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="page-container module-page artifact-form-page">
    <header class="page-header">
      <div>
        <p class="page-eyebrow">Issues</p>
        <h1 class="page-title">发起 Issue</h1>
        <p class="page-description">用一段 Markdown 正文说明建议或问题，发起后全员可见并参与讨论。</p>
      </div>
      <RouterLink class="secondary-action" :to="{ name: routeNames.issues }">返回列表</RouterLink>
    </header>

    <div class="artifact-form-layout">
      <section class="panel-card artifact-editor-panel">
        <ElAlert v-if="errorMessage" class="page-alert" :title="errorMessage" type="error" :closable="false" />
        <ElForm ref="formRef" :model="form" :rules="rules" label-position="top">
          <ElFormItem label="标题" prop="title">
            <ElInput v-model="form.title" maxlength="200" show-word-limit placeholder="例如：希望展品支持导出 Markdown" />
          </ElFormItem>
          <ElFormItem label="Markdown 正文" prop="description">
            <ElInput
              v-model="form.description"
              class="markdown-editor-input"
              type="textarea"
              :rows="14"
              maxlength="100000"
              placeholder="# 背景&#10;&#10;## 期望的效果"
            />
          </ElFormItem>
        </ElForm>

        <div class="form-actions">
          <ElButton type="primary" :loading="submitting" @click="submit">发起 Issue</ElButton>
        </div>
      </section>

      <aside class="panel-card artifact-preview-panel">
        <div class="detail-section-heading"><h2>正文预览</h2></div>
        <MarkdownViewer v-if="form.description.trim()" :content="form.description" />
        <p v-else class="muted-copy">输入 Markdown 正文后在这里预览。</p>
      </aside>
    </div>
  </div>
</template>
