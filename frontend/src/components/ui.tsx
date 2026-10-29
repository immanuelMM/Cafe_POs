import type { ReactNode } from 'react'
import { errorMessage } from '../lib/errors'

export function Spinner({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="spinner-wrap" role="status">
      <span className="spinner" aria-hidden="true" />
      <span>{label}</span>
    </div>
  )
}

export function ErrorBox({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  return (
    <div className="alert alert-error" role="alert">
      <span>{errorMessage(error)}</span>
      {onRetry && (
        <button type="button" className="btn btn-sm" onClick={onRetry}>
          Retry
        </button>
      )}
    </div>
  )
}

export function Field({
  label,
  error,
  children,
  hint,
}: {
  label: string
  error?: string
  hint?: string
  children: ReactNode
}) {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      {children}
      {hint && !error && <span className="field-hint">{hint}</span>}
      {error && (
        <span className="field-error" role="alert">
          {error}
        </span>
      )}
    </label>
  )
}

export function PageHeader({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="page-head">
      <h1>{title}</h1>
      <div className="page-head-actions">{children}</div>
    </div>
  )
}

/** `totalPages` is optional in the OpenAPI contract (computed server-side); a missing value means a single page. */
export function Pager({
  page,
  totalPages = 1,
  onPage,
}: {
  page: number
  totalPages?: number
  onPage: (p: number) => void
}) {
  if (totalPages <= 1) return null
  return (
    <nav className="pager" aria-label="Pagination">
      <button className="btn" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        ‹ Prev
      </button>
      <span>
        Page {page} of {totalPages}
      </span>
      <button className="btn" disabled={page >= totalPages} onClick={() => onPage(page + 1)}>
        Next ›
      </button>
    </nav>
  )
}
