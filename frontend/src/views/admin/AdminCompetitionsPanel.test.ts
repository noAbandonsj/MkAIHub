// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ElMessage, ElMessageBox } from 'element-plus'

vi.mock('element-plus', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return {
    ...actual,
    ElMessage: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
    ElMessageBox: { confirm: vi.fn(), prompt: vi.fn() },
  }
})

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return {
    ...actual,
    useRoute: () => ({ params: { id: '1' } }),
  }
})

vi.mock('@/api/competitions', () => ({
  competitionsApi: {
    list: vi.fn(),
    get: vi.fn(),
    listTasks: vi.fn(),
    listResults: vi.fn(),
  },
}))
vi.mock('@/api/admin', () => ({
  adminApi: {
    createCompetitionTask: vi.fn(),
    updateCompetitionTask: vi.fn(),
    deleteCompetitionTask: vi.fn(),
    publishCompetitionResults: vi.fn(),
  },
}))

import { ApiError } from '@/api/client'
import { adminApi } from '@/api/admin'
import { competitionsApi } from '@/api/competitions'
import type { CompetitionListItem, CompetitionTask } from '@/types/competition'
import AdminCompetitionsPanel from './AdminCompetitionsPanel.vue'

function makeCompetitionRow(overrides: Partial<CompetitionListItem> = {}): CompetitionListItem {
  return {
    id: 1,
    title: '提示词大赛',
    summary: 's',
    creator: { id: 1, username: 'admin', display_name: '管理员' },
    status: 'ONGOING',
    lifecycle_status: 'PUBLISHED',
    start_at: '2026-08-01T00:00:00Z',
    end_at: '2026-08-31T00:00:00Z',
    created_at: '2026-08-01T00:00:00Z',
    updated_at: '2026-08-01T00:00:00Z',
    ...overrides,
  }
}

function makeCompetitionTask(overrides: Partial<CompetitionTask> = {}): CompetitionTask {
  return {
    id: 7,
    title: '必做任务',
    description: 'd',
    creator: { id: 1, username: 'admin', display_name: '管理员' },
    status: 'OPEN',
    required: true,
    sort_order: 1,
    max_score: '30.00',
    weight: '10.00',
    deadline_at: null,
    effective_deadline_at: '2026-08-31T00:00:00Z',
    my_submission: null,
    current_submission_count: 2,
    reviewed_count: 1,
    created_at: '2026-08-01T00:00:00Z',
    updated_at: '2026-08-01T00:00:00Z',
    ...overrides,
  }
}

let pinia: ReturnType<typeof createPinia>
let wrapper: ReturnType<typeof mount> | undefined

function view() {
  return wrapper!
}

async function mountPanel(rows: CompetitionListItem[]) {
  vi.mocked(competitionsApi.list).mockResolvedValue({ items: rows, page: 1, page_size: 10, total: rows.length })
  wrapper = mount(AdminCompetitionsPanel, {
    attachTo: document.body,
    global: {
      plugins: [pinia],
      stubs: {
        RouterLink: { props: ['to'], template: '<a><slot /></a>' },
      },
    },
  })
  await flushPromises()
  return wrapper
}

function rowButton(text: string, rowTitle?: string) {
  const button = Array.from(document.querySelectorAll('button')).find((item) => {
    if (!item.textContent?.includes(text)) return false
    if (!rowTitle) return true
    return item.closest('tr')?.textContent?.includes(rowTitle) ?? false
  })
  expect(button, `未找到按钮：${text}`).toBeTruthy()
  return button!
}

function bodyText(): string {
  return document.body.textContent ?? ''
}

async function openTaskFormDialog(rows: CompetitionListItem[], tasks: CompetitionTask[]) {
  vi.mocked(competitionsApi.listTasks).mockResolvedValue({ items: tasks })
  const view = await mountPanel(rows)
  rowButton('任务', rows[0].title).click()
  await flushPromises()
  rowButton('添加任务').click()
  await flushPromises()
  return view
}

function fillTaskForm(title: string, description: string) {
  const formDialog = Array.from(document.querySelectorAll('.el-dialog')).find((el) =>
    el.textContent?.includes('添加竞赛任务'),
  )
  expect(formDialog, '竞赛任务表单弹窗未打开').toBeTruthy()
  const inputs = view()
    .findAllComponents({ name: 'ElInput' })
    .filter((item) => formDialog!.contains(item.element))
  expect(inputs.length).toBeGreaterThanOrEqual(2)
  inputs.find((item) => item.props('type') !== 'textarea')!.vm.$emit('update:modelValue', title)
  inputs.find((item) => item.props('type') === 'textarea')!.vm.$emit('update:modelValue', description)
}

function taskFormOverlay(): HTMLElement | null {
  const formDialog = Array.from(document.querySelectorAll('.el-dialog')).find((el) =>
    el.textContent?.includes('添加竞赛任务'),
  )
  return (formDialog?.closest('.el-overlay') as HTMLElement | null) ?? null
}

function makePublishedRead(row: CompetitionListItem) {
  return {
    ...row,
    lifecycle_status: 'RESULT_PUBLISHED' as const,
    rules_markdown: '# r',
    task_count: 1,
    registration_count: 1,
    my_registration: null,
    results_published: true,
  }
}

describe('AdminCompetitionsPanel 组件', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    pinia = createPinia()
    setActivePinia(pinia)
  })

  // 弹窗传送门挂在 document.body 上，测试之间必须卸载并清空，避免遗留 DOM 干扰按钮查询。
  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    document.body.innerHTML = ''
  })

  it('列表按运营状态显示对应操作按钮', async () => {
    await mountPanel([
      makeCompetitionRow({ id: 1, title: '草稿赛' }),
      makeCompetitionRow({ id: 2, title: '进行中赛', lifecycle_status: 'PUBLISHED' }),
      makeCompetitionRow({ id: 3, title: '已出结果赛', lifecycle_status: 'RESULT_PUBLISHED' }),
      makeCompetitionRow({ id: 4, title: '已归档赛', lifecycle_status: 'ARCHIVED' }),
    ])

    expect(rowButton('发布', '草稿赛')).toBeTruthy()
    expect(rowButton('发布结果', '进行中赛')).toBeTruthy()
    expect(rowButton('结果/奖项', '已出结果赛')).toBeTruthy()
    expect(rowButton('归档', '进行中赛')).toBeTruthy()
    const archivedRow = Array.from(document.querySelectorAll('tr')).find((row) => row.textContent?.includes('已归档赛'))
    expect(archivedRow?.textContent ?? '').not.toContain('发布结果')
  })

  it('发布结果被拒时展示后端错误且不打开结果弹窗', async () => {
    await mountPanel([makeCompetitionRow()])

    vi.mocked(ElMessageBox.confirm).mockResolvedValueOnce({} as never)
    vi.mocked(adminApi.publishCompetitionResults).mockRejectedValueOnce(
      new ApiError(422, '竞赛尚未结束，不能发布结果', { code: 'COMPETITION_NOT_ENDED' }),
    )
    rowButton('发布结果').click()
    await flushPromises()

    expect(bodyText()).toContain('竞赛尚未结束，不能发布结果')
    expect(bodyText()).not.toContain('保存奖项并重新发布')
    expect(competitionsApi.listResults).not.toHaveBeenCalled()
  })

  it('发布结果成功后打开弹窗展示排行榜并可保存奖项', async () => {
    await mountPanel([makeCompetitionRow()])

    vi.mocked(ElMessageBox.confirm).mockResolvedValueOnce({} as never)
    vi.mocked(adminApi.publishCompetitionResults).mockResolvedValueOnce(
      makePublishedRead(makeCompetitionRow()),
    )
    vi.mocked(competitionsApi.listResults).mockResolvedValue({
      competition_id: 1,
      published_by: { id: 1, username: 'admin', display_name: '管理员' },
      published_at: '2026-08-20T00:00:00Z',
      items: [
        {
          registration_id: 11,
          user: { id: 2, username: 'b', display_name: '员工B' },
          total_score: '8.3333',
          rank: 1,
          award: null,
        },
      ],
    })
    rowButton('发布结果').click()
    await flushPromises()

    expect(ElMessage.success).toHaveBeenCalledWith('竞赛结果已发布')
    expect(bodyText()).toContain('第 1 名')

    const awardInput = view()
      .findAllComponents({ name: 'ElInput' })
      .find((item) => item.find('input[placeholder="奖项名称（可空）"]').exists())
    awardInput!.vm.$emit('update:modelValue', '一等奖')
    await view().vm.$nextTick()

    vi.mocked(adminApi.publishCompetitionResults).mockResolvedValueOnce(
      makePublishedRead(makeCompetitionRow()),
    )
    rowButton('保存奖项并重新发布').click()
    await flushPromises()

    expect(adminApi.publishCompetitionResults).toHaveBeenCalledWith(1, [{ registration_id: 11, award: '一等奖' }])
    expect(ElMessage.success).toHaveBeenCalledWith('奖项已保存，结果快照已更新')
  })

  it('添加竞赛任务：成功时以规范化字段调用接口并关闭表单', async () => {
    await openTaskFormDialog([makeCompetitionRow()], [makeCompetitionTask()])

    fillTaskForm('新竞赛任务', '任务描述')
    vi.mocked(adminApi.createCompetitionTask).mockResolvedValueOnce({
      ...makeCompetitionTask({ id: 8, title: '新竞赛任务' }),
      participant_count: 0,
      submission_count: 0,
    })
    rowButton('保存').click()
    await flushPromises()

    expect(adminApi.createCompetitionTask).toHaveBeenCalledWith(1, {
      title: '新竞赛任务',
      description: '任务描述',
      deadline_at: null,
      required: true,
      sort_order: 2,
      max_score: '30.00',
      weight: '10.00',
    })
    expect(ElMessage.success).toHaveBeenCalledWith('竞赛任务已创建')
    // ElDialog 关闭后 DOM 保留（v-show 隐藏），以遮罩层可见性判断弹窗已关闭。
    expect(taskFormOverlay()?.style.display).toBe('none')
  })

  it('添加竞赛任务失败时保留输入并展示后端错误', async () => {
    await openTaskFormDialog([makeCompetitionRow()], [])

    fillTaskForm('非法任务', '任务描述')
    vi.mocked(adminApi.createCompetitionTask).mockRejectedValueOnce(
      new ApiError(422, '竞赛任务排序冲突', { code: 'COMPETITION_TASK_CONFLICT' }),
    )
    rowButton('保存').click()
    await flushPromises()

    expect(ElMessage.error).toHaveBeenCalledWith('竞赛任务排序冲突')
    expect(taskFormOverlay()?.style.display).not.toBe('none')
    const titleInput = view()
      .findAllComponents({ name: 'ElInput' })
      .filter((item) => taskFormOverlay()?.contains(item.element))
      .find((item) => item.props('type') !== 'textarea')
    expect(titleInput!.props('modelValue')).toBe('非法任务')
  })

  it('任务弹窗展示必做/选做与评审进度', async () => {
    vi.mocked(competitionsApi.listTasks).mockResolvedValue({
      items: [
        makeCompetitionTask({ title: '必做任务', required: true, current_submission_count: 2, reviewed_count: 1 }),
        makeCompetitionTask({ id: 8, title: '选做任务', required: false, current_submission_count: 0, reviewed_count: 0 }),
      ],
    })
    await mountPanel([makeCompetitionRow()])
    rowButton('任务').click()
    await flushPromises()

    const text = bodyText()
    expect(text).toContain('必做任务')
    expect(text).toContain('选做任务')
    expect(text).toContain('1 / 2')
    expect(text).toContain('0 / 0')
  })
})
