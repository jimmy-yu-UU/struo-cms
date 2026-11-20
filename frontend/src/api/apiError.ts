// `maxBytes` is set on the detail of a PAYLOAD_TOO_LARGE error that names the size cap.
export type ValidationDetail = { field: string; message: string; locale?: string; maxBytes?: number }

// The shape of the server's error envelope body.
export type ApiErrorBody = {
  code?: string
  message: string
  details?: ValidationDetail[]
  traceId?: string
}

// The Error thrown by apiClient on any non-2xx response.
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly details?: ValidationDetail[]
  // Seconds to wait, lifted from the standard Retry-After response header when the rejecting rate
  // limiter supplies one (the window limiters this API ships with do; OnRejected also has a bare
  // fallback branch for limiters that do not, so this can be absent on a 429 too). Kept off the
  // error envelope on purpose: ErrorBody.details is a {field,message} list, so a scalar delay has
  // no honest place in it.
  readonly retryAfterSeconds?: number
  // The id the server logged for this request; quote it to find the entry in the server log.
  readonly traceId?: string
  constructor(
    status: number,
    message: string,
    code?: string,
    details?: ValidationDetail[],
    retryAfterSeconds?: number,
    traceId?: string,
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.details = details
    this.retryAfterSeconds = retryAfterSeconds
    this.traceId = traceId
  }
}
