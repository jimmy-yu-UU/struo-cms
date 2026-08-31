import { describe, it, expect } from 'vitest'
import { fieldVariants } from '@/components/ui/field'

// The bug this guards: (Role's Super Admin, User's Active, Language's
// Default/Enabled rendering as a control stretched across the whole form) traces to a single rule:
// `ui/field`'s `vertical` orientation applies `[&>*]:w-full` to every direct child of a <Field>.
// BooleanField.vue now works around that by wrapping its Switch in a plain div, so that wrapper
// (not the switch itself) absorbs the rule (see fieldComponents.test.ts's own wrapper assertion).
// But `ui/field/index.ts` is GENERATED output (regenerated via `pnpm dlx shadcn-vue@latest add
// field`), so nothing stops a future regeneration from dropping the rule from `vertical`, or
// respelling it — at which point BooleanField's wrapper becomes either dead weight or silently
// stops protecting anything, with a fully green suite otherwise. This test pins the real
// invariant directly against the generated variant function, not a proxy for it.
describe('fieldVariants vertical orientation (guards the rule BooleanField\'s wrapper exists to absorb)', () => {
  it('keeps forcing full width on every direct child in the vertical (default) orientation', () => {
    expect(fieldVariants({ orientation: 'vertical' })).toContain('[&>*]:w-full')
  })
})
