// @vitest-environment happy-dom
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import ArtifactStatusBadge from './ArtifactStatusBadge.vue'

describe('ArtifactStatusBadge', () => {
  it.each([
    ['DRAFT', '草稿', 'is-pending'],
    ['PUBLISHED', '已发布', 'is-done'],
    ['ARCHIVED', '已归档', 'is-closed'],
  ] as const)('maps %s to the shared status badge', (status, label, toneClass) => {
    const wrapper = mount(ArtifactStatusBadge, { props: { status } })

    expect(wrapper.text()).toBe(label)
    expect(wrapper.get('.status-pill').classes()).toContain(toneClass)
  })
})
