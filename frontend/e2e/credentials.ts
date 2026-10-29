/** The e2e login password. The template ships no bootstrap password, so the suite cannot guess one. */
export function e2ePassword(): string {
  const password = process.env.E2E_PASSWORD
  if (!password) {
    throw new Error(
      'E2E_PASSWORD is not set. Set it to the password of the seeded bootstrap admin ' +
        '(Auth__BootstrapAdmin__Password).',
    )
  }
  return password
}
