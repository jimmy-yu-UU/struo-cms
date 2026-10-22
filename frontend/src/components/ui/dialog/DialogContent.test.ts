import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { Dialog, DialogContent } from '.'
import { i18n } from '../../../i18n'

const opts = { global: { stubs: { teleport: true }, renderStubDefaultSlot: true } }

describe('DialogContent close button', () => {
  it('names the close button in the UI locale', () => {
    i18n.global.locale.value = 'zh-TW'
    try {
      const w = mount({
        components: { Dialog, DialogContent },
        template: '<Dialog :open="true"><DialogContent>x</DialogContent></Dialog>',
      }, opts)
      expect(w.find('[data-slot="dialog-close"]').text()).toBe('關閉')
    } finally {
      i18n.global.locale.value = 'en'
    }
  })
})
