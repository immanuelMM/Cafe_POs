/** RFC 9457 ProblemDetails (plus extension members such as ingredient/required/available). */
export class ApiError extends Error {
  status: number
  title: string
  detail?: string
  errors?: Record<string, string[]>
  extensions: Record<string, unknown>
  constructor(status: number, body: Record<string, unknown> | null, fallback: string) {
    const title = (body?.title as string | undefined) ?? fallback
    const detail = body?.detail as string | undefined
    super(detail ?? title)
    this.status = status
    this.title = title
    this.detail = detail
    this.errors = body?.errors as Record<string, string[]> | undefined
    this.extensions = body ?? {}
  }
  /** Human readable message, including field validation errors. */
  get display(): string {
    if (this.errors) {
      const msgs = Object.values(this.errors).flat()
      if (msgs.length) return msgs.join(' ')
    }
    if (this.status === 409 && this.extensions.ingredient) {
      const e = this.extensions
      return `Not enough ${String(e.ingredient)} (need ${String(e.required)}, only ${String(e.available)} left).`
    }
    return this.detail ?? this.title
  }
}

export class NetworkError extends Error {
  constructor() {
    super('Cannot reach the server. Check your connection and try again.')
  }
}

export const errorMessage = (e: unknown) =>
  e instanceof ApiError ? e.display : e instanceof Error ? e.message : 'Something went wrong.'
