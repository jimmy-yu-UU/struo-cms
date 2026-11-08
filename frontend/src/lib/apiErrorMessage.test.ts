import { describe, expect, it } from 'vitest'
import { ApiError } from '../api/apiClient'
import { createI18n } from 'vue-i18n'
import { catalogs } from '../locales'
import {
  formatByteSize,
  lastSuperAdminMessage,
  uploadTooLargeMessage,
  writeFailureMessage,
  type LocalizedMessage,
} from './apiErrorMessage'
import { tooManyRequestsMessage } from './apiErrorMessage'

describe('tooManyRequestsMessage', () => {
  it('includes the wait when the server told us how long', () => {
    const err = new ApiError(429, 'nope', 'TOO_MANY_REQUESTS', undefined, 42)
    expect(tooManyRequestsMessage(err)).toEqual({
      key: 'errors.tooManyRequestsWithWait',
      params: { seconds: 42 },
    })
  })

  it('falls back to a wait-less sentence when Retry-After was absent', () => {
    const err = new ApiError(429, 'nope', 'TOO_MANY_REQUESTS')
    expect(tooManyRequestsMessage(err)).toEqual({ key: 'errors.tooManyRequests' })
  })
})

function render(locale: 'en' | 'zh-TW', msg: LocalizedMessage): string {
  const i18n = createI18n({ legacy: false, locale, messages: catalogs as never }) as unknown as {
    global: { t: (key: string, params: Record<string, unknown>) => string }
  }
  return i18n.global.t(msg.key, msg.params ?? {})
}

describe('formatByteSize', () => {
  it.each([
    [64, '64 B'],
    [1536, '1.5 KB'],
    [1048576, '1.0 MB'],
    [26214400, '25.0 MB'],
    [1073741824, '1.0 GB'],
  ])('writes %i bytes as %s', (bytes, text) => {
    expect(formatByteSize(bytes)).toBe(text)
  })
})

describe('lastSuperAdminMessage', () => {
  it('maps the LAST_SUPER_ADMIN code, whatever the server prose says', () => {
    const err = new ApiError(409, 'Any English prose at all.', 'LAST_SUPER_ADMIN')
    const msg = lastSuperAdminMessage(err)!
    expect(render('zh-TW', msg)).toBe('至少要保留一個啟用中的超級管理員帳號。')
    expect(render('en', msg)).toBe('At least one active account must remain a super-admin.')
  })

  it('leaves every other error to the caller', () => {
    expect(lastSuperAdminMessage(new ApiError(409, 'x', 'CONFLICT'))).toBeUndefined()
    expect(lastSuperAdminMessage(new ApiError(409, 'At least one active account must remain'))).toBeUndefined()
    expect(lastSuperAdminMessage(new Error('x'))).toBeUndefined()
  })
})

describe('writeFailureMessage', () => {
  it('words LAST_SUPER_ADMIN in the UI locale and passes any other error through', () => {
    const t = (key: string) => render('zh-TW', { key })
    expect(writeFailureMessage(new ApiError(409, 'English', 'LAST_SUPER_ADMIN'), t, 'common.saveFailed'))
      .toBe('至少要保留一個啟用中的超級管理員帳號。')
    expect(writeFailureMessage(new ApiError(409, 'Email already in use.', 'CONFLICT'), t, 'common.saveFailed'))
      .toBe('Email already in use.')
    expect(writeFailureMessage('boom', t, 'common.saveFailed')).toBe(t('common.saveFailed'))
  })
})

describe('uploadTooLargeMessage', () => {
  it('names the limit when the server sent it', () => {
    const err = new ApiError(413, 'File exceeds the maximum size of 26214400 bytes.', 'PAYLOAD_TOO_LARGE',
      [{ field: 'file', message: 'x', maxBytes: 26214400 }])
    const msg = uploadTooLargeMessage(err)!
    expect(render('zh-TW', msg)).toBe('檔案超過上限（25.0 MB）。')
    expect(render('en', msg)).toBe('The file is over the size limit (25.0 MB).')
  })

  it('answers a 413 without details, even with no JSON body at all', () => {
    for (const err of [
      new ApiError(413, 'Request failed (413)'),
      new ApiError(413, 'Request body is too large.', 'PAYLOAD_TOO_LARGE'),
    ]) {
      const msg = uploadTooLargeMessage(err)!
      expect(render('zh-TW', msg)).toBe('檔案超過上限。')
      expect(render('en', msg)).toBe('The file is over the size limit.')
    }
  })

  it('answers the PAYLOAD_TOO_LARGE code on any status and ignores other errors', () => {
    expect(uploadTooLargeMessage(new ApiError(400, 'x', 'PAYLOAD_TOO_LARGE'))).toBeDefined()
    expect(uploadTooLargeMessage(new ApiError(400, 'x', 'BAD_USER_INPUT'))).toBeUndefined()
    expect(uploadTooLargeMessage(new Error('File exceeds the maximum size of 5 bytes.'))).toBeUndefined()
  })

  it('ignores a non-numeric or non-positive limit', () => {
    const err = new ApiError(413, 'x', 'PAYLOAD_TOO_LARGE', [{ field: 'file', message: 'x', maxBytes: 0 }])
    expect(render('zh-TW', uploadTooLargeMessage(err)!)).toBe('檔案超過上限。')
  })
})
