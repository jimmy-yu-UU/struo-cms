import { describe, it, expect } from 'vitest'
import { registry, getFieldType } from './registry'
import { ALL_FIELD_INTERFACES } from './types'
import { fieldVariants } from '@/components/ui/field'

describe('inline layout hint', () => {
  it('marks the two boolean interfaces inline', () => {
    expect(getFieldType('boolean').inline).toBe(true)
    expect(getFieldType('checkbox').inline).toBe(true)
  })

  it('leaves every other interface non-inline', () => {
    const inline = ALL_FIELD_INTERFACES.filter((i) => registry[i].inline === true)
    expect(inline.sort()).toEqual(['boolean', 'checkbox'])
  })

  it('does not mark the unknown-interface fallback inline', () => {
    expect(getFieldType('not-a-real-interface').inline).not.toBe(true)
  })
})

// The actual bug this whole batch fixed (Role's Super Admin, User's Active, Language's
// Default/Enabled rendering as a control stretched across the whole form) traces to a single rule:
// `ui/field`'s `vertical` orientation applies `[&>*]:w-full` to every direct child of a <Field>.
// ItemForm.vue works around that by rendering inline (boolean) fields with
// orientation="horizontal" instead, a variant that carries no such rule. But `ui/field/index.ts`
// is GENERATED output (regenerated via `pnpm dlx shadcn-vue@latest add field`), so nothing stops a
// future regeneration from folding the width-forcing rule into `horizontal` too — at which point
// all four reported bugs come back with a fully green suite, because every other test here only
// asserts `data-orientation="horizontal"`, a proxy for this property rather than the property
// itself. These two tests pin the real invariant directly against the generated variant function.
describe('fieldVariants orientation (guards against the width-forcing rule regressing)', () => {
  it('keeps the horizontal orientation free of the width-forcing rule that stretches inline controls', () => {
    expect(fieldVariants({ orientation: 'horizontal' })).not.toContain('[&>*]:w-full')
  })

  it('confirms the vertical orientation is the one that forces full width', () => {
    expect(fieldVariants({ orientation: 'vertical' })).toContain('[&>*]:w-full')
  })
})
