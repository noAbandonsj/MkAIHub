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
    get: vi.fn(),
    listTasks: vi.fn(),
    listResults: vi.fn(),
    register: vi.fn(),
    cancelRegistration: vi.fn(),
  },
}))

import { ApiError } from '@/api/client'
import { competitionsApi } from '@/api/competitions'
import { useSessionStore } from '@/stores/session'
import type { CompetitionRead } from '@/types/competition'
import type { UserRead } from '@/types/user'
import CompetitionDetailView from './CompetitionDetailView.vue'

const admin: UserRead = {
  id: 1,
  username: 'admin',
  display_name: '管理员',
  role: 'SYSTEM_ADMIN',
  is_active: true,
  last_login_at: null,
  created_at: '2026-08-01T00:00:00Z',
  updated_at: '2026-08-01T00:00:00Z',
}

function makeCompetition(overrides: Partial<CompetitionRead> = {}): CompetitionRead {
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
    rules_markdown: '# 规则',
    task_count: 2,
    registration_count: 0,
    my_registration: null,
    results_published: false,
    ...overrides,
  }
}

let pinia: ReturnType<typeof createPinia>

async function mountWithCompetition(competition: CompetitionRead) {
  vi.mocked(competitionsApi.get).mockResolvedValue(competition)
  vi.mocked(competitionsApi.listTasks).mockResolvedValue({ items: [] })
  const wrapper = mount(CompetitionDetailView, {
    attachTo: document.body,
    global: {
      plugins: [pinia],
      stubs: {
        RouterLink: { props: ['to'], template: '<a><slot /></a>' },
      },
    },
  })
  await flushPromises()
  lastWrapper = wrapper
  return wrapper
}

let lastWrapper: ReturnType<typeof mount> | undefined

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  const button = wrapper.findAll('button').find((item) => item.text().includes(text))
  expect(button, `未找到按钮：${text}`).toBeTruthy()
  return button!
}

describe('CompetitionDetailView 组件', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    pinia = createPinia()
    setActivePinia(pinia)
    useSessionStore().setCurrentUser(admin)
  })

  afterEach(() => {
    lastWrapper?.unmount()
    lastWrapper = undefined
    document.body.innerHTML = ''
  })

  it('已发布竞赛未报名时显示报名按钮', async () => {
    const wrapper = await mountWithCompetition(makeCompetition())

    const text = wrapper.text()
    expect(text).toContain('报名参赛')
    expect(text).toContain('报名后自动领取全部竞赛任务')
    expect(text).not.toContain('取消报名')
    expect(text).not.toContain('排行榜')
  })

  it('已报名后显示取消报名与我的报名状态', async () => {
    const wrapper = await mountWithCompetition(
      makeCompetition({
        registration_count: 1,
        my_registration: { id: 11, status: 'REGISTERED', registered_at: '2026-08-02T00:00:00Z', cancelled_at: null },
      }),
    )

    const text = wrapper.text()
    expect(text).toContain('取消报名')
    expect(text).not.toContain('报名参赛')
    expect(text).toContain('我的报名状态：已报名')
  })

  it('结果已发布的竞赛不能再报名，展示冻结排行榜', async () => {
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
          award: '一等奖',
        },
      ],
    })
    const wrapper = await mountWithCompetition(
      makeCompetition({
        lifecycle_status: 'RESULT_PUBLISHED',
        results_published: true,
        status: 'ENDED',
        my_registration: { id: 11, status: 'REGISTERED', registered_at: '2026-08-02T00:00:00Z', cancelled_at: null },
      }),
    )

    const text = wrapper.text()
    expect(text).not.toContain('报名参赛')
    expect(text).not.toContain('取消报名')
    expect(text).toContain('排行榜')
    expect(text).toContain('排名已冻结')
    expect(text).toContain('第 1 名')
    expect(text).toContain('一等奖')
  })

  it('草稿竞赛仅提示可见性，不显示报名入口', async () => {
    const wrapper = await mountWithCompetition(
      makeCompetition({ lifecycle_status: 'DRAFT', status: 'UPCOMING' }),
    )

    const text = wrapper.text()
    expect(text).not.toContain('报名参赛')
    expect(text).toContain('竞赛尚未发布，仅管理员可见')
  })

  it('重复报名展示后端错误并在结束后恢复按钮状态', async () => {
    const wrapper = await mountWithCompetition(makeCompetition())

    vi.mocked(competitionsApi.register).mockRejectedValueOnce(
      new ApiError(409, '已报名该竞赛，不能重复报名', { code: 'REGISTRATION_CONFLICT' }),
    )
    findButton(wrapper, '报名参赛').trigger('click')
    await flushPromises()

    expect(competitionsApi.register).toHaveBeenCalledWith(1)
    expect(ElMessage.error).toHaveBeenCalledWith('已报名该竞赛，不能重复报名')
    expect(findButton(wrapper, '报名参赛').classes()).not.toContain('is-loading')
  })

  it('报名成功后提示并刷新竞赛详情', async () => {
    const wrapper = await mountWithCompetition(makeCompetition())

    vi.mocked(competitionsApi.register).mockResolvedValueOnce(undefined as never)
    vi.mocked(competitionsApi.get).mockResolvedValueOnce(
      makeCompetition({
        registration_count: 1,
        my_registration: { id: 11, status: 'REGISTERED', registered_at: '2026-08-02T00:00:00Z', cancelled_at: null },
      }),
    )
    findButton(wrapper, '报名参赛').trigger('click')
    await flushPromises()

    expect(ElMessage.success).toHaveBeenCalledWith('报名成功，竞赛任务已自动领取')
    expect(wrapper.text()).toContain('取消报名')
  })
})
