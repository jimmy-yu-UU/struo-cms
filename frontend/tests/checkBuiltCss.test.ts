// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { checkBuiltCss, findNotAllowedRules } from '../scripts/checkBuiltCss.ts'

const ESCAPED = String.raw`.disabled\:cursor-not-allowed:disabled`
const SONNER = '[data-sonner-toast][data-styled=true] [data-disabled=true]'
const ASSETS = 'dist/assets'

describe('findNotAllowedRules', () => {
  it('finds a minified declaration', () => {
    expect(findNotAllowedRules('.a{cursor:not-allowed}')).toEqual(['.a'])
  })

  it('finds spaced, upper-case and important declarations', () => {
    const css = '.a { cursor : not-allowed } .b{CURSOR: Not-Allowed !important} .c{cursor:not-allowed;color:red}'
    expect(findNotAllowedRules(css)).toEqual(['.a', '.b', '.c'])
  })

  it('reports the rule inside a media query or layer', () => {
    expect(findNotAllowedRules('@layer utilities{@media (min-width:1px){.x:disabled{cursor:not-allowed}}}')).toEqual([
      '.x:disabled',
    ])
  })

  it('ignores other cursors and the word in a selector or another property', () => {
    const css = '.cursor-not-allowed{cursor:default}.b{cursor:pointer}.c{content:"cursor:no"}.d{--x:not-allowed}'
    expect(findNotAllowedRules(css)).toEqual([])
  })

  it('ignores an empty or rule-less string', () => {
    expect(findNotAllowedRules('')).toEqual([])
  })
})

describe('checkBuiltCss', () => {
  it('fails when there is no stylesheet, naming the directory to build', () => {
    const errors = checkBuiltCss([], ASSETS)
    expect(errors).toHaveLength(1)
    expect(errors[0]).toContain(ASSETS)
    expect(errors[0]).toContain('vite build')
  })

  it('passes a stylesheet without the cursor', () => {
    expect(checkBuiltCss([{ name: 'index.css', css: '.a{cursor:pointer}' }], ASSETS)).toEqual([])
  })

  it('names the file and the selector of every offending rule', () => {
    const errors = checkBuiltCss(
      [
        { name: 'ok.css', css: '.a{color:red}' },
        { name: 'bad.css', css: `${ESCAPED}{cursor:not-allowed}` },
      ],
      ASSETS,
    )
    expect(errors).toHaveLength(1)
    expect(errors[0]).toContain('bad.css')
    expect(errors[0]).toContain(ESCAPED)
    expect(errors[0]).toContain('tokens.css')
  })

  it('accepts the third-party toast rule that tokens.css overrides, in any spacing', () => {
    const css = `${SONNER}{cursor:not-allowed}`
    expect(checkBuiltCss([{ name: 'vendor.css', css }], ASSETS)).toEqual([])
    expect(checkBuiltCss([{ name: 'vendor.css', css: css.replace('] [', ']   [') }], ASSETS)).toEqual([])
  })
})
