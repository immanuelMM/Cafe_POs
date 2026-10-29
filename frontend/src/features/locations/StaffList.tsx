import { ErrorBox, Spinner } from '../../components/ui'
import { useLocationStaff } from './useLocations'

/** Read-only staff roster for one location (GET /locations/{id}/staff). */
export function StaffList({ locationId }: { locationId: string }) {
  const q = useLocationStaff(locationId)
  return (
    <>
      {q.isLoading && <Spinner label="Loading staff…" />}
      {q.error && <ErrorBox error={q.error} onRetry={() => void q.refetch()} />}
      {q.data && q.data.length === 0 && <p className="muted">No staff are assigned to this location.</p>}
      <ul className="row-list">
        {q.data?.map((m) => (
          <li key={m.userId} className="row-item">
            <div className="row-main">
              <strong>{m.displayName}</strong>
              <small className="muted">{m.email}</small>
            </div>
            <span className="badge badge-role">{m.role}</span>
            {!m.active && <span className="badge badge-canceled">Inactive</span>}
          </li>
        ))}
      </ul>
    </>
  )
}
