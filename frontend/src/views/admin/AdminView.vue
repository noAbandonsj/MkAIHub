<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { FormInstance, FormRules } from 'element-plus'
import {
  ElAlert,
  ElButton,
  ElDialog,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElPagination,
  ElSelect,
  ElSwitch,
  ElTabPane,
  ElTable,
  ElTableColumn,
  ElTabs,
  ElTag,
} from 'element-plus'
import 'element-plus/es/components/alert/style/css'
import 'element-plus/es/components/button/style/css'
import 'element-plus/es/components/dialog/style/css'
import 'element-plus/es/components/empty/style/css'
import 'element-plus/es/components/form/style/css'
import 'element-plus/es/components/form-item/style/css'
import 'element-plus/es/components/input/style/css'
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/pagination/style/css'
import 'element-plus/es/components/select/style/css'
import 'element-plus/es/components/switch/style/css'
import 'element-plus/es/components/table/style/css'
import 'element-plus/es/components/tag/style/css'
import 'element-plus/es/components/tab-pane/style/css'
import 'element-plus/es/components/tabs/style/css'

import { adminApi } from '@/api/admin'
import { ApiError, getApiErrorMessage } from '@/api/client'
import { routeNames } from '@/router/route-names'
import { useSessionStore } from '@/stores/session'
import type { UserRead, UserRole } from '@/types/user'

type AdminTab = 'users' | 'content' | 'competitions'

const session = useSessionStore()
const route = useRoute()
const router = useRouter()
const activeTab = ref<AdminTab>('users')
const users = ref<UserRead[]>([])
const query = ref('')
const page = ref(1)
const pageSize = ref(10)
const total = ref(0)
const loading = ref(false)
const submitting = ref(false)
const errorMessage = ref<string | null>(null)
const isRedirecting = ref(false)

const roleLabels: Record<UserRole, string> = {
  EMPLOYEE: '普通员工',
  SYSTEM_ADMIN: '系统管理员',
}
const roleOptions: Array<{ label: string; value: UserRole }> = [
  { label: '普通员工', value: 'EMPLOYEE' },
  { label: '系统管理员', value: 'SYSTEM_ADMIN' },
]

const createDialogVisible = ref(false)
const createFormRef = ref<FormInstance>()
const createForm = reactive({
  username: '',
  display_name: '',
  password: '',
  role: 'EMPLOYEE' as UserRole,
})

const editDialogVisible = ref(false)
const editFormRef = ref<FormInstance>()
const editForm = reactive({
  id: 0,
  username: '',
  display_name: '',
  role: 'EMPLOYEE' as UserRole,
  is_active: true,
})

const resetDialogVisible = ref(false)
const resetFormRef = ref<FormInstance>()
const resetForm = reactive({ id: 0, username: '', new_password: '' })

const createRules: FormRules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  display_name: [{ required: true, message: '请输入显示名', trigger: 'blur' }],
  password: [
    { required: true, message: '请输入初始密码', trigger: 'blur' },
    { min: 8, max: 128, message: '密码长度必须为 8-128 位', trigger: 'blur' },
  ],
  role: [{ required: true, message: '请选择角色', trigger: 'change' }],
}

const editRules: FormRules = {
  display_name: [{ required: true, message: '请输入显示名', trigger: 'blur' }],
  role: [{ required: true, message: '请选择角色', trigger: 'change' }],
}

const resetRules: FormRules = {
  new_password: [
    { required: true, message: '请输入新密码', trigger: 'blur' },
    { min: 8, max: 128, message: '密码长度必须为 8-128 位', trigger: 'blur' },
  ],
}

function roleLabel(role: UserRole): string {
  return roleLabels[role]
}

function formatDate(value: string | null | undefined): string {
  if (!value) {
    return '-'
  }
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

async function handleRequestError(error: unknown): Promise<void> {
  errorMessage.value = getApiErrorMessage(error, '请求失败')

  if (error instanceof ApiError && error.status === 401 && !isRedirecting.value) {
    isRedirecting.value = true
    session.clearSession()
    await router.replace({
      name: routeNames.login,
      query: { redirect: route.fullPath },
    })
  }
}

async function loadUsers(): Promise<void> {
  if (!session.isAdmin) {
    // The route guard is the primary protection. This second check also keeps
    // direct component mounting from issuing an admin request for employees.
    errorMessage.value = '只有系统管理员可以访问用户管理'
    return
  }

  loading.value = true
  errorMessage.value = null
  try {
    const response = await adminApi.listUsers({
      page: page.value,
      page_size: pageSize.value,
      q: query.value,
    })
    users.value = response.items
    total.value = response.total
  } catch (error) {
    await handleRequestError(error)
  } finally {
    loading.value = false
  }
}

function searchUsers(): void {
  if (page.value === 1) {
    void loadUsers()
    return
  }
  page.value = 1
}

function resetCreateForm(): void {
  createForm.username = ''
  createForm.display_name = ''
  createForm.password = ''
  createForm.role = 'EMPLOYEE'
  createFormRef.value?.clearValidate()
}

function openCreateDialog(): void {
  resetCreateForm()
  createDialogVisible.value = true
}

async function createUser(): Promise<void> {
  const valid = await createFormRef.value?.validate().catch(() => false)
  if (!valid) {
    return
  }

  submitting.value = true
  try {
    await adminApi.createUser({ ...createForm })
    createDialogVisible.value = false
    ElMessage.success('用户已创建')
    page.value = 1
    await loadUsers()
  } catch (error) {
    await handleRequestError(error)
  } finally {
    submitting.value = false
  }
}

function openEditDialog(value: unknown): void {
  const user = value as UserRead
  editForm.id = user.id
  editForm.username = user.username
  editForm.display_name = user.display_name
  editForm.role = user.role
  editForm.is_active = user.is_active
  errorMessage.value = null
  editDialogVisible.value = true
}

async function updateUser(): Promise<void> {
  const valid = await editFormRef.value?.validate().catch(() => false)
  if (!valid) {
    return
  }

  submitting.value = true
  try {
    await adminApi.updateUser(editForm.id, {
      display_name: editForm.display_name,
      role: editForm.role,
      is_active: editForm.is_active,
    })
    editDialogVisible.value = false
    ElMessage.success('用户信息已更新')
    await loadUsers()
  } catch (error) {
    await handleRequestError(error)
  } finally {
    submitting.value = false
  }
}

function openResetDialog(value: unknown): void {
  const user = value as UserRead
  resetForm.id = user.id
  resetForm.username = user.username
  resetForm.new_password = ''
  resetFormRef.value?.clearValidate()
  resetDialogVisible.value = true
}

async function resetPassword(): Promise<void> {
  const valid = await resetFormRef.value?.validate().catch(() => false)
  if (!valid) {
    return
  }

  submitting.value = true
  try {
    await adminApi.resetPassword(resetForm.id, { new_password: resetForm.new_password })
    resetDialogVisible.value = false
    ElMessage.success('密码已重置')
  } catch (error) {
    await handleRequestError(error)
  } finally {
    submitting.value = false
  }
}

watch([page, pageSize], () => {
  void loadUsers()
})

onMounted(() => {
  void loadUsers()
})
</script>

<template>
  <div class="page-container module-page">
    <header class="page-header">
      <div>
        <p class="page-eyebrow">MkAIHub</p>
        <h1 class="page-title">管理</h1>
        <p class="page-description">系统管理员可在一个页面维护账号，内容和竞赛页签暂保留为骨架。</p>
      </div>
    </header>

    <section class="panel-card admin-panel">
      <ElTabs v-model="activeTab">
        <ElTabPane label="用户" name="users">
          <div class="admin-toolbar">
            <ElInput
              v-model="query"
              class="admin-search"
              clearable
              placeholder="按用户名或显示名查询"
              @keyup.enter="searchUsers"
            />
            <ElButton type="primary" @click="searchUsers">查询</ElButton>
            <ElButton :loading="loading" @click="loadUsers">刷新</ElButton>
            <ElButton type="success" @click="openCreateDialog">创建用户</ElButton>
          </div>

          <ElAlert
            v-if="errorMessage"
            class="admin-error"
            :title="errorMessage"
            type="error"
            :closable="false"
            show-icon
            role="alert"
          />

          <ElTable :data="users" row-key="id" class="user-table">
            <ElTableColumn prop="username" label="用户名" min-width="150" />
            <ElTableColumn prop="display_name" label="显示名" min-width="150" />
            <ElTableColumn label="角色" min-width="130">
              <template #default="{ row }">
                <ElTag :type="row.role === 'SYSTEM_ADMIN' ? 'warning' : 'info'">
                  {{ roleLabel(row.role) }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="状态" min-width="100">
              <template #default="{ row }">
                <ElTag :type="row.is_active ? 'success' : 'info'">
                  {{ row.is_active ? '启用' : '停用' }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="最近登录" min-width="170">
              <template #default="{ row }">{{ formatDate(row.last_login_at) }}</template>
            </ElTableColumn>
            <ElTableColumn label="操作" fixed="right" min-width="160">
              <template #default="{ row }">
                <ElButton link type="primary" @click="openEditDialog(row)">编辑</ElButton>
                <ElButton link type="warning" @click="openResetDialog(row)">重置密码</ElButton>
              </template>
            </ElTableColumn>
          </ElTable>
          <p v-if="loading" class="table-loading" role="status">正在加载用户…</p>

          <div class="admin-pagination">
            <ElPagination
              v-model:current-page="page"
              v-model:page-size="pageSize"
              :page-sizes="[10, 20, 50]"
              :total="total"
              layout="total, sizes, prev, pager, next"
              background
            />
          </div>
        </ElTabPane>

        <ElTabPane label="内容" name="content">
          <ElEmpty description="内容管理将在后续批次接入" />
        </ElTabPane>

        <ElTabPane label="竞赛" name="competitions">
          <ElEmpty description="竞赛维护将在后续批次接入" />
        </ElTabPane>
      </ElTabs>
    </section>

    <ElDialog v-model="createDialogVisible" title="创建用户" width="min(480px, calc(100vw - 32px))">
      <ElForm ref="createFormRef" :model="createForm" :rules="createRules" label-position="top">
        <ElFormItem label="用户名" prop="username">
          <ElInput v-model="createForm.username" autocomplete="off" />
        </ElFormItem>
        <ElFormItem label="显示名" prop="display_name">
          <ElInput v-model="createForm.display_name" autocomplete="off" />
        </ElFormItem>
        <ElFormItem label="初始密码" prop="password">
          <ElInput v-model="createForm.password" type="password" show-password autocomplete="new-password" />
        </ElFormItem>
        <ElFormItem label="角色" prop="role">
          <ElSelect v-model="createForm.role" class="admin-control" placeholder="请选择角色">
            <ElOption v-for="option in roleOptions" :key="option.value" v-bind="option" />
          </ElSelect>
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="createDialogVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="submitting" @click="createUser">创建</ElButton>
      </template>
    </ElDialog>

    <ElDialog v-model="editDialogVisible" title="编辑用户" width="min(480px, calc(100vw - 32px))">
      <ElForm ref="editFormRef" :model="editForm" :rules="editRules" label-position="top">
        <ElFormItem label="用户名">
          <ElInput :model-value="editForm.username" disabled />
        </ElFormItem>
        <ElFormItem label="显示名" prop="display_name">
          <ElInput v-model="editForm.display_name" autocomplete="off" />
        </ElFormItem>
        <ElFormItem label="角色" prop="role">
          <ElSelect v-model="editForm.role" class="admin-control">
            <ElOption v-for="option in roleOptions" :key="option.value" v-bind="option" />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="账号状态">
          <ElSwitch v-model="editForm.is_active" active-text="启用" inactive-text="停用" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="editDialogVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="submitting" @click="updateUser">保存</ElButton>
      </template>
    </ElDialog>

    <ElDialog
      v-model="resetDialogVisible"
      title="重置密码"
      width="min(440px, calc(100vw - 32px))"
    >
      <ElForm ref="resetFormRef" :model="resetForm" :rules="resetRules" label-position="top">
        <ElFormItem label="用户">
          <ElInput :model-value="resetForm.username" disabled />
        </ElFormItem>
        <ElFormItem label="新密码" prop="new_password">
          <ElInput v-model="resetForm.new_password" type="password" show-password autocomplete="new-password" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="resetDialogVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="submitting" @click="resetPassword">确认重置</ElButton>
      </template>
    </ElDialog>
  </div>
</template>
