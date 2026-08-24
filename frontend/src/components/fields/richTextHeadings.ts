// H2 is the shallowest level offered on purpose: the page title is the H1, so body content that
// also emitted an H1 would produce two. The sanitizer allowlist matches (see
// GanssHtmlSanitizer.cs) -- an h1 written here would be stripped on save.
export type HeadingLevel = 2 | 3 | 4 | 5 | 6

export const HEADING_LEVELS: ReadonlyArray<HeadingLevel> = [2, 3, 4, 5, 6]
