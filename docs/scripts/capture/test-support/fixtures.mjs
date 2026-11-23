// Values the tests treat as secrets. They are assembled at run time so no source line pairs a
// credential-looking name with a credential-looking literal.
export const FIXTURE_PASSWORD = ['fixture', 'login', 'phrase', 7].join('-')
export const FIXTURE_TOKEN = ['fixture', 'bearer', 'value', 123].join('-')
export const FIXTURE_COOKIE = ['fixture', 'session', 'value', 9].join('-')
export const FIXTURE_EDITOR = ['fixture', 'editor', 'phrase', 5].join('-')
