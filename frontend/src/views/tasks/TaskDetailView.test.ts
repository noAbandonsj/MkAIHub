// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ElMessage, ElMessageBox } from 'element-plus'

vi.mock('element-plus', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return {
    ...actual,
    // ElMessage/ElMessageBox 以命令式调用，替换为 spy 便于断言与控制确认流程。
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

vi.mock('@/api/tasks', () => ({
  tasksApi: {
    get: vi.fn(),
    listParticipants: vi.fn(),
    listSubmissions: vi.fn(),
    submit: vi.fn(),
    join: vi.fn(),
    leave: vi.fn(),
    complete: vi.fn(),
    close: vi.fn(),
  },
}))
vi.mock('@/api/taskSubmissions', () => ({
  taskSubmissionsApi: {
    requestRevision: vi.fn(),
    accept: vi.fn(),
    reject: vi.fn(),
    competitionReview: vi.fn(),
  },
}))
vi.mock('@/api/artifacts', () => ({
  artifactsApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    publish: vi.fn(),
    upload: vi.fn(),
    deleteFile: vi.fn(),
  },
}))
vi.mock('@/api/admin', () => ({
  adminApi: { reopenTask: vi.fn() },
}))

import { ApiError } from '@/api/client'
import { adminApi } from '@/api/admin'
import { artifactsApi } from '@/api/artifacts'
import { taskSubmissionsApi } from '@/api/taskSubmissions'
import { tasksApi } from '@/api/tasks'
import { useSessionStore } from '@/stores/session'
import ArtifactEditor from '@/components/artifact/ArtifactEditor.vue'
import type { ArtifactListItem, ArtifactRead } from '@/types/artifact'
import type { TaskParticipant, TaskRead, TaskSubmission } from '@/types/task'
import type { UserRead } from '@/types/user'
import TaskDetailView from './TaskDetailView.vue'

const creator = { id: 1, username: 'a', display_name: '员工A' }
const employeeB = { id: 2, username: 'b', display_name: '员工B' }

const users: Record<number, UserRead> = {
  1: {
    id: 1,
    username: 'a',
    display_name: '员工A',
    role: 'EMPLOYEE',
    is_active: true,
    last_login_at: null,
    created_at: '2026-08-01T00:00:00Z',
    updated_at: '2026-08-01T00:00:00Z',
  },
  2: {
    id: 2,
    username: 'b',
    display_name: '员工B',
    role: 'EMPLOYEE',
    is_active: true,
    last_login_at: null,
    created_at: '2026-08-01T00:00:00Z',
    updated_at: '2026-08-01T00:00:00Z',
  },
  99: {
    id: 99,
    username: 'admin',
    display_name: '管理员',
    role: 'SYSTEM_ADMIN',
    is_active: true,
    last_login_at: null,
    created_at: '2026-08-01T00:00:00Z',
    updated_at: '2026-08-01T00:00:00Z',
  },
}

function makeTask(overrides: Partial<TaskRead> = {}): TaskRead {
  return {
    id: 1,
    title: '闭环任务',
    creator,
    status: 'OPEN',
    competition_id: null,
    competition_title: null,
    deadline_at: null,
    completed_at: null,
    closed_at: null,
    created_at: '2026-08-01T00:00:00Z',
    updated_at: '2026-08-01T00:00:00Z',
    description: '# 任务说明',
    my_participation: null,
    participant_count: 0,
    submission_count: 0,
    ...overrides,
  }
}

function makeParticipant(overrides: Partial<TaskParticipant> = {}): TaskParticipant {
  return {
    id: 21,
    task_id: 1,
    user: employeeB,
    status: 'ACTIVE',
    joined_at: '2026-08-01T01:00:00Z',
    left_at: null,
    ...overrides,
  }
}

function makeSubmission(overrides: Partial<TaskSubmission> = {}): TaskSubmission {
  return {
    id: 11,
    task_id: 1,
    participant_id: 21,
    participant: employeeB,
    artifact: { id: 31, title: '成果展品', status: 'PUBLISHED' },
    round_no: 1,
    note: null,
    status: 'SUBMITTED',
    is_current: true,
    submitted_at: '2026-08-02T00:00:00Z',
    revision_requested_at: null,
    decided_at: null,
    decider: null,
    decision_note: null,
    task: null,
    competition_review: null,
    competition_rank: null,
    competition_award: null,
    ...overrides,
  }
}

function makeArtifact(overrides: Partial<ArtifactListItem> = {}): ArtifactListItem {
  return {
    id: 31,
    title: '我的展品',
    summary: 's',
    author: employeeB,
    status: 'PUBLISHED',
    attachment_count: 0,
    source_types: [],
    published_at: '2026-08-01T00:00:00Z',
    created_at: '2026-08-01T00:00:00Z',
    updated_at: '2026-08-01T00:00:00Z',
    ...overrides,
  }
}

function makeArtifactRead(overrides: Partial<ArtifactRead> = {}): ArtifactRead {
  return {
    ...makeArtifact({ id: 41, title: '任务中新建的展品' }),
    content_markdown: '# 任务成果',
    archived_at: null,
    files: [],
    ...overrides,
  }
}

interface Deferred<T> {
  promise: Promise<T>
  resolve: (value: T) => void
  reject: (error: unknown) => void
}
function createDeferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

let pinia: ReturnType<typeof createPinia>
let lastWrapper: ReturnType<typeof mount> | undefined

function mountView() {
  const wrapper = mount(TaskDetailView, {
    attachTo: document.body,
    global: {
      plugins: [pinia],
      stubs: {
        RouterLink: { props: ['to'], template: '<a><slot /></a>' },
      },
    },
  })
  lastWrapper = wrapper
  return wrapper
}

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  const button = wrapper.findAll('button').find((item) => item.text().includes(text))
  expect(button, `未找到按钮：${text}`).toBeTruthy()
  return button!
}

async function mountWithTask(task: TaskRead, options: { submissions?: TaskSubmission[]; participants?: TaskParticipant[]; artifacts?: ArtifactListItem[] } = {}) {
  vi.mocked(tasksApi.get).mockResolvedValue(task)
  vi.mocked(tasksApi.listParticipants).mockResolvedValue({ items: options.participants ?? [] })
  vi.mocked(tasksApi.listSubmissions).mockResolvedValue({
    items: options.submissions ?? [],
    page: 1,
    page_size: 50,
    total: (options.submissions ?? []).length,
  })
  vi.mocked(artifactsApi.list).mockResolvedValue({ items: options.artifacts ?? [], page: 1, page_size: 100, total: (options.artifacts ?? []).length })
  const wrapper = mountView()
  await flushPromises()
  return wrapper
}

describe('TaskDetailView 组件', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    pinia = createPinia()
    setActivePinia(pinia)
    const session = useSessionStore()
    session.setCurrentUser(users[2])
  })

  afterEach(() => {
    lastWrapper?.unmount()
    lastWrapper = undefined
    document.body.innerHTML = ''
  })

  it('未参与的员工只看到领取入口，看不到提交与验收区', async () => {
    const wrapper = await mountWithTask(makeTask())

    const text = wrapper.text()
    expect(text).toContain('领取任务')
    expect(text).not.toContain('成果提交')
    expect(text).not.toContain('成果验收')
    expect(text).not.toContain('标记完成')
  })

  it('活跃参与人看到提交表单与退出参与，不再显示领取', async () => {
    const participation = makeParticipant()
    const wrapper = await mountWithTask(
      makeTask({ my_participation: participation, participant_count: 1 }),
      { participants: [participation], artifacts: [makeArtifact()] },
    )

    const text = wrapper.text()
    expect(text).toContain('退出参与')
    expect(text).toContain('成果提交')
    expect(text).toContain('选择我的已发布展品')
    expect(text).not.toContain('领取任务')
    expect(text).not.toContain('成果验收')
  })

  it('竞赛报名自动领取任务，任务页不允许单独领取或退出', async () => {
    const participation = makeParticipant()
    const wrapper = await mountWithTask(
      makeTask({
        competition_id: 5,
        competition_title: '提示词大赛',
        my_participation: participation,
        participant_count: 1,
      }),
      { participants: [participation], artifacts: [makeArtifact()] },
    )

    const text = wrapper.text()
    expect(text).toContain('竞赛任务由报名自动领取')
    expect(text).not.toContain('领取任务')
    expect(text).not.toContain('退出参与')
    expect(text).toContain('成果提交')
  })

  it('任务页弹窗新建展品后自动选中，不跳转独立页面', async () => {
    const participation = makeParticipant()
    const wrapper = await mountWithTask(
      makeTask({ my_participation: participation }),
      { participants: [participation], artifacts: [] },
    )

    findButton(wrapper, '新建展品').trigger('click')
    await wrapper.vm.$nextTick()
    const editor = wrapper.findComponent(ArtifactEditor)
    expect(editor.exists()).toBe(true)
    expect(editor.props('allowDraft')).toBe(false)

    editor.vm.$emit('saved', makeArtifactRead(), true)
    await wrapper.vm.$nextTick()

    expect(wrapper.findComponent({ name: 'ElSelect' }).props('modelValue')).toBe(41)
    expect(ElMessage.success).toHaveBeenCalledWith('展品已创建并发布，已自动选中')
    expect(wrapper.findComponent(ArtifactEditor).exists()).toBe(false)
  })

  it('发布人看到验收区与任务管理按钮', async () => {
    const session = useSessionStore()
    session.setCurrentUser(users[1])
    const wrapper = await mountWithTask(
      makeTask(),
      { submissions: [makeSubmission()] },
    )

    const text = wrapper.text()
    expect(text).toContain('成果验收')
    expect(text).toContain('退回修改')
    expect(text).toContain('验收通过')
    expect(text).toContain('不采用')
    expect(text).toContain('标记完成')
    expect(text).toContain('编辑任务')
  })

  it('管理员可验收与关闭任务，但看不到他人的编辑与标记完成', async () => {
    const session = useSessionStore()
    session.setCurrentUser(users[99])
    const wrapper = await mountWithTask(
      makeTask(),
      { submissions: [makeSubmission()] },
    )

    const text = wrapper.text()
    expect(text).toContain('成果验收')
    expect(text).toContain('验收通过')
    expect(text).toContain('关闭任务')
    expect(text).not.toContain('标记完成')
    expect(text).not.toContain('编辑任务')
  })

  it('竞赛任务不显示标记完成与验收区，管理员看到竞赛评审区', async () => {
    const session = useSessionStore()
    session.setCurrentUser(users[99])
    const wrapper = await mountWithTask(
      makeTask({ competition_id: 5, competition_title: '提示词大赛' }),
      { submissions: [makeSubmission()] },
    )

    const text = wrapper.text()
    expect(text).toContain('所属竞赛')
    expect(text).toContain('竞赛评审')
    expect(text).toContain('提交评审')
    expect(text).not.toContain('标记完成')
    expect(text).not.toContain('成果验收')
  })

  it('已完成任务为只读态，不显示参与和提交入口', async () => {
    const wrapper = await mountWithTask(
      makeTask({ status: 'COMPLETED', completed_at: '2026-08-03T00:00:00Z' }),
    )

    const text = wrapper.text()
    expect(text).toContain('任务已完成，内容只读')
    expect(text).not.toContain('领取任务')
    expect(text).not.toContain('退出参与')
    expect(text).not.toContain('标记完成')
    expect(text).not.toContain('关闭任务')
  })

  it('提交成果：提交中禁用，失败保留输入并展示后端错误', async () => {
    const participation = makeParticipant()
    const wrapper = await mountWithTask(
      makeTask({ my_participation: participation }),
      { participants: [participation], artifacts: [makeArtifact()] },
    )

    const select = wrapper.findComponent({ name: 'ElSelect' })
    select.vm.$emit('update:modelValue', 31)
    await wrapper.vm.$nextTick()
    const noteInput = wrapper
      .findAllComponents({ name: 'ElInput' })
      .find((item) => item.props('type') === 'textarea')
    noteInput!.vm.$emit('update:modelValue', '第一版说明')
    await wrapper.vm.$nextTick()

    const deferred = createDeferred<TaskSubmission>()
    vi.mocked(tasksApi.submit).mockReturnValueOnce(deferred.promise)
    findButton(wrapper, '提交成果').trigger('click')
    await wrapper.vm.$nextTick()

    expect(findButton(wrapper, '提交成果').classes()).toContain('is-loading')

    deferred.reject(new ApiError(409, '成果状态不允许提交', { code: 'SUBMISSION_STATE_CONFLICT' }))
    await flushPromises()

    expect(ElMessage.error).toHaveBeenCalledWith('成果状态不允许提交')
    expect(select.props('modelValue')).toBe(31)
    expect(noteInput!.props('modelValue')).toBe('第一版说明')
    expect(findButton(wrapper, '提交成果').classes()).not.toContain('is-loading')
  })

  it('提交成果成功后清空表单并重新加载', async () => {
    const participation = makeParticipant()
    const wrapper = await mountWithTask(
      makeTask({ my_participation: participation }),
      { participants: [participation], artifacts: [makeArtifact()] },
    )

    const select = wrapper.findComponent({ name: 'ElSelect' })
    select.vm.$emit('update:modelValue', 31)
    await wrapper.vm.$nextTick()
    vi.mocked(tasksApi.submit).mockResolvedValueOnce(makeSubmission())
    findButton(wrapper, '提交成果').trigger('click')
    await flushPromises()

    expect(tasksApi.submit).toHaveBeenCalledWith(1, { artifact_id: 31, note: null })
    expect(ElMessage.success).toHaveBeenCalledWith('成果已提交')
    expect(wrapper.findComponent({ name: 'ElSelect' }).props('modelValue')).toBe(null)
  })

  it('未选择展品时提示且不调用提交接口', async () => {
    const participation = makeParticipant()
    const wrapper = await mountWithTask(
      makeTask({ my_participation: participation }),
      { participants: [participation], artifacts: [makeArtifact()] },
    )

    findButton(wrapper, '提交成果').trigger('click')
    await flushPromises()

    expect(ElMessage.warning).toHaveBeenCalledWith('请先选择一条要提交的已发布展品')
    expect(tasksApi.submit).not.toHaveBeenCalled()
  })

  it('退回修改要求填写意见并调用退回接口', async () => {
    const session = useSessionStore()
    session.setCurrentUser(users[1])
    const wrapper = await mountWithTask(
      makeTask(),
      { submissions: [makeSubmission()] },
    )

    vi.mocked(ElMessageBox.prompt).mockResolvedValueOnce({ value: ' 请补充说明 ' } as never)
    findButton(wrapper, '退回修改').trigger('click')
    await flushPromises()

    expect(taskSubmissionsApi.requestRevision).toHaveBeenCalledWith(11, '请补充说明')
  })

  it('取消意见输入时不调用退回接口', async () => {
    const session = useSessionStore()
    session.setCurrentUser(users[1])
    const wrapper = await mountWithTask(
      makeTask(),
      { submissions: [makeSubmission()] },
    )

    vi.mocked(ElMessageBox.prompt).mockRejectedValueOnce('cancel')
    findButton(wrapper, '退回修改').trigger('click')
    await flushPromises()

    expect(taskSubmissionsApi.requestRevision).not.toHaveBeenCalled()
  })

  it('竞赛评审：评分为空提示，填写后调用评审接口', async () => {
    const session = useSessionStore()
    session.setCurrentUser(users[99])
    const wrapper = await mountWithTask(
      makeTask({ competition_id: 5, competition_title: '提示词大赛' }),
      { submissions: [makeSubmission()] },
    )

    findButton(wrapper, '提交评审').trigger('click')
    await flushPromises()
    expect(ElMessage.warning).toHaveBeenCalledWith('请填写评审分数')
    expect(taskSubmissionsApi.competitionReview).not.toHaveBeenCalled()

    const scoreInput = wrapper.find('.task-review-score')
    expect(scoreInput.exists()).toBe(true)
    const scoreComponent = wrapper.findAllComponents({ name: 'ElInput' }).find((item) => item.classes().includes('task-review-score'))
    scoreComponent!.vm.$emit('update:modelValue', '25')
    await wrapper.vm.$nextTick()
    vi.mocked(taskSubmissionsApi.competitionReview).mockResolvedValueOnce(makeSubmission({ competition_review: { raw_score: '25.00', comment: null, reviewed_at: '2026-08-03T00:00:00Z', reviewer: users[99] } }))
    findButton(wrapper, '提交评审').trigger('click')
    await flushPromises()

    expect(taskSubmissionsApi.competitionReview).toHaveBeenCalledWith(11, '25', '')
  })

  it('关闭任务由管理员执行成功后重载', async () => {
    const session = useSessionStore()
    session.setCurrentUser(users[99])
    const wrapper = await mountWithTask(makeTask())

    vi.mocked(ElMessageBox.confirm).mockResolvedValueOnce({} as never)
    vi.mocked(tasksApi.close).mockResolvedValueOnce(
      makeTask({ status: 'CLOSED', closed_at: '2026-08-03T00:00:00Z' }),
    )
    findButton(wrapper, '关闭任务').trigger('click')
    await flushPromises()

    expect(tasksApi.close).toHaveBeenCalledWith(1)
    expect(ElMessage.success).toHaveBeenCalledWith('任务已关闭')
  })

  it('管理员可重开已关闭任务', async () => {
    const session = useSessionStore()
    session.setCurrentUser(users[99])
    const wrapper = await mountWithTask(
      makeTask({ status: 'CLOSED', closed_at: '2026-08-03T00:00:00Z' }),
    )

    vi.mocked(ElMessageBox.confirm).mockResolvedValueOnce({} as never)
    vi.mocked(adminApi.reopenTask).mockResolvedValueOnce(makeTask({ status: 'OPEN' }))
    findButton(wrapper, '重开任务').trigger('click')
    await flushPromises()

    expect(adminApi.reopenTask).toHaveBeenCalledWith(1)
    expect(ElMessage.success).toHaveBeenCalledWith('任务已重开')
  })
})
