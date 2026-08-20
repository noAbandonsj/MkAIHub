import { describe, expect, it } from 'vitest'

import { toUtcIsoString } from './format'

describe('toUtcIsoString', () => {
  it('converts a local Date into a UTC ISO string with the Z suffix', () => {
    const local = new Date(Date.UTC(2026, 8, 1, 12, 0, 0))
    expect(toUtcIsoString(local)).toBe('2026-09-01T12:00:00.000Z')
  })

  it('returns null to clear the field', () => {
    expect(toUtcIsoString(null)).toBeNull()
  })
})
