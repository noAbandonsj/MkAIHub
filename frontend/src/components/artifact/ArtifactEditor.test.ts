// @vitest-environment happy-dom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'

vi.mock('@/api/artifacts', () => ({
  artifactsApi: {
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    publish: vi.fn(),
    upload: vi.fn(),
    deleteFile: vi.fn(),
  },
}))

import { artifactsApi } from '@/api/artifacts'
import type { ArtifactRead } from '@/types/artifact'
import ArtifactEditor from './ArtifactEditor.vue'

function artifact(status: 'DRAFT' | 'PUBLISHED'): ArtifactRead {
  return {
    id: 41,
    title: '任务成果展品',
    summary: '用于竞赛任务提交',
    content_markdown: '# 成果',
    author: { id: 2, username: 'b', display_name: '员工B' },
    status,
    attachment_count: 0,
    source_types: [],
    published_at: status === 'PUBLISHED' ? '2026-08-27T01:00:00Z' : null,
    archived_at: null,
    files: [],
    created_at: '2026-08-27T01:00:00Z',
    updated_at: '2026-08-27T01:00:00Z',
  }
}

describe('ArtifactEditor 组件', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('任务模式只允许创建并发布，并把发布后的展品交回任务页', async () => {
    vi.mocked(artifactsApi.create).mockResolvedValueOnce(artifact('DRAFT'))
    vi.mocked(artifactsApi.publish).mockResolvedValueOnce(artifact('PUBLISHED'))
    const wrapper = mount(ArtifactEditor, {
      props: { mode: 'create', allowDraft: false },
      global: { plugins: [createPinia()] },
    })

    await wrapper.find('input[placeholder="例如：客服提示词优化实践"]').setValue('任务成果展品')
    await wrapper.find('textarea[placeholder="用几句话说明内容价值和适用场景"]').setValue('用于竞赛任务提交')
    await wrapper.find('textarea[placeholder="# 背景\n\n## 使用方法"]').setValue('# 成果')

    expect(wrapper.text()).not.toContain('保存草稿')
    await wrapper.get('button.el-button--primary').trigger('click')
    await flushPromises()

    expect(artifactsApi.create).toHaveBeenCalledWith({
      title: '任务成果展品',
      summary: '用于竞赛任务提交',
      content_markdown: '# 成果',
      file_ids: [],
    })
    expect(artifactsApi.publish).toHaveBeenCalledWith(41)
    expect(wrapper.emitted('saved')?.[0]).toEqual([artifact('PUBLISHED'), true])
  })
})
