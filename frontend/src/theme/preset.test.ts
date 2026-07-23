import { describe, it, expect } from 'vitest'
import { struoPresetConfig, StruoPreset } from './preset'

describe('StruoPreset', () => {
  it('config maps primary to the sky ramp', () => {
    expect(struoPresetConfig.semantic.primary[500]).toBe('{sky.500}')
    expect(struoPresetConfig.semantic.primary[50]).toBe('{sky.50}')
  })

  it('config maps surface to the slate ramp in light and dark', () => {
    expect(struoPresetConfig.semantic.colorScheme.light.surface[900]).toBe('{slate.900}')
    expect(struoPresetConfig.semantic.colorScheme.dark.surface[950]).toBe('{slate.950}')
  })

  it('StruoPreset is produced from definePreset', () => {
    expect(StruoPreset).toBeTruthy()
    expect(typeof StruoPreset).toBe('object')
  })

  it('light primary is sky-600 per brand-spec (Aura default 500 is too light)', () => {
    expect(struoPresetConfig.semantic.colorScheme.light.primary.color).toBe('{primary.600}')
    expect(struoPresetConfig.semantic.colorScheme.light.primary.hoverColor).toBe('{primary.700}')
  })

  it('dark primary is sky-400 per brand-spec', () => {
    expect(struoPresetConfig.semantic.colorScheme.dark.primary.color).toBe('{primary.400}')
    expect(struoPresetConfig.semantic.colorScheme.dark.primary.hoverColor).toBe('{primary.300}')
  })
})
