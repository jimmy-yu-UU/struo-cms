// The tool's own error type. Every failure a run can end with is one of these, so the CLI prints one
// clear line and exits 1; a stack trace means a bug in the tool. The optional details name where the
// failure happened.
export class CaptureError extends Error {
  constructor(message, { capture, file, step } = {}) {
    super(message)
    this.capture = capture
    this.file = file
    this.step = step
  }
}
