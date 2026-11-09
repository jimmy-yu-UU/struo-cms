// Mirrors the backend's RichTextCleaner.IsBlankHtml: markup is blank when it has no image or rule and
// nothing but whitespace (non-breaking spaces included) is left once the tags are removed.
const MEDIA_TAG = /<(?:img|hr)/i
const ANY_TAG = /<[^<>]*>/g
const NBSP = /&nbsp;/gi

/** True for a string that is empty, whitespace, or rich text markup carrying no content. */
export function isBlankMarkup(value: string): boolean {
  if (MEDIA_TAG.test(value)) return false
  return value.replace(ANY_TAG, '').replace(NBSP, ' ').trim().length === 0
}
