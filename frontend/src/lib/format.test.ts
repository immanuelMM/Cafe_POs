import { describe, expect, it } from 'vitest'
import { formatMoney, round2 } from './format'

describe('format', () => {
  it('rounds half away from zero to 2dp', () => {
    expect(round2(0.125)).toBe(0.13)
    expect(round2(-0.125)).toBe(-0.13)
  })

  it('formats money as PHP and treats null as zero', () => {
    expect(formatMoney(1234.5)).toContain('1,234.50')
    expect(formatMoney(null)).toContain('0.00')
  })
})
