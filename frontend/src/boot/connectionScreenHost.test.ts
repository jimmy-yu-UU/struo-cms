import { describe, it, expect, afterEach } from 'vitest'
import { createApp } from 'vue'
import { createI18n } from 'vue-i18n'
import { createConnectionScreenHost } from './connectionScreenHost'
import en from '@/locales/en'

const flush = () => new Promise((r) => setTimeout(r, 0))

function setup() {
  const container = document.createElement('div')
  document.body.appendChild(container)
  const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })
  const app = createApp({ render: () => null })
  app.use(i18n)
  return { container, host: createConnectionScreenHost(app._context, container), i18n }
}

afterEach(() => { document.body.innerHTML = '' })

describe('createConnectionScreenHost', () => {
  it('keeps one screen mounted across retries and resolves each Retry click', async () => {
    const { container, host } = setup()
    const first = host.waitForRetry()
    await flush()
    const main = container.querySelector('main')
    expect(main).not.toBeNull()

    container.querySelector('button')!.click()
    await first
    host.setBusy(true)
    await flush()
    expect(container.querySelector('button')!.disabled).toBe(true)

    host.setBusy(false)
    const second = host.waitForRetry()
    await flush()
    expect(container.querySelector('main')).toBe(main)
    container.querySelector('button')!.click()
    await second
  })

  it('removes the screen on close and leaves the shared i18n instance usable', async () => {
    const { container, host, i18n } = setup()
    void host.waitForRetry()
    await flush()
    host.close()
    expect(container.querySelector('main')).toBeNull()
    expect(i18n.global.t('common.retry')).toBe(en.common.retry)
  })

  it('does nothing on close when the screen was never shown', () => {
    const { container, host } = setup()
    expect(() => host.close()).not.toThrow()
    expect(container.children).toHaveLength(0)
  })
})
