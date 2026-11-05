// Code of the core system capability that lets a caller read and clear login locks. The catalog
// itself lives in the API (SystemCapabilities); the SPA only names the one it gates a page on.
export const LOGIN_UNLOCK = 'login.unlock'

// Longest email the API accepts for a login lock; it mirrors the length of the users.email column
// (EmailAddress.MaxLength in the API), and a longer address is answered with 400.
export const EMAIL_MAX_LENGTH = 255
