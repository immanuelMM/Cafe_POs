import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { Modal } from '../../components/Modal'
import { ErrorBox, Field, PageHeader, Pager, Spinner } from '../../components/ui'
import { errorMessage } from '../../lib/api'
import type { AssignableRole, UserDto } from '../../lib/types'
import { useAuthStore } from '../auth/authStore'
import { useLocations } from '../locations/locationsApi'
import { ROLES, useUser, useUsers, userCalls } from './usersApi'

/** Locations the current actor may assign people to: Owner = all active, Admin = own active location. */
function useAssignableLocations() {
  const user = useAuthStore((s) => s.user)
  const owner = !!user?.isOwner
  const all = useLocations(owner)
  if (owner) return (all.data ?? []).filter((l) => l.active).map((l) => ({ id: l.id, name: l.name }))
  return user?.location ? [user.location] : []
}

function useInvalidateUsers() {
  const qc = useQueryClient()
  return () => {
    void qc.invalidateQueries({ queryKey: ['users'] })
    void qc.invalidateQueries({ queryKey: ['user'] })
    void qc.invalidateQueries({ queryKey: ['location-staff'] })
  }
}

const createSchema = z
  .object({
    email: z.string().trim().min(1, 'Email is required').email('Enter a valid email'),
    displayName: z.string().trim().min(1, 'Name is required'),
    password: z.string().min(8, 'At least 8 characters'),
    isOwner: z.boolean(),
    locationId: z.string(),
    role: z.enum(['Cashier', 'Manager', 'Admin']),
  })
  .refine((v) => v.isOwner || v.locationId !== '', { path: ['locationId'], message: 'Choose a location' })
type CreateValues = z.infer<typeof createSchema>

function CreateUserModal({ onClose }: { onClose: () => void }) {
  const owner = useAuthStore((s) => !!s.user?.isOwner)
  const locations = useAssignableLocations()
  const invalidate = useInvalidateUsers()
  const create = useMutation({
    mutationFn: userCalls.create,
    onSuccess: () => {
      invalidate()
      onClose()
    },
  })
  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<CreateValues>({
    resolver: zodResolver(createSchema),
    defaultValues: { email: '', displayName: '', password: '', isOwner: false, locationId: locations.length === 1 ? locations[0].id : '', role: 'Cashier' },
  })
  const isOwnerFlag = watch('isOwner')
  return (
    <Modal title="New staff member" onClose={onClose}>
      <form
        className="form"
        noValidate
        onSubmit={handleSubmit((v) =>
          create.mutate({
            email: v.email,
            displayName: v.displayName,
            password: v.password,
            isOwner: owner ? v.isOwner : undefined,
            assignments: v.isOwner ? [] : [{ locationId: v.locationId, role: v.role }],
          }),
        )}
      >
        {create.error && <div className="alert alert-error" role="alert">{errorMessage(create.error)}</div>}
        <Field label="Display name" error={errors.displayName?.message}>
          <input autoComplete="off" {...register('displayName')} />
        </Field>
        <Field label="Email" error={errors.email?.message}>
          <input type="email" autoComplete="off" inputMode="email" {...register('email')} />
        </Field>
        <Field label="Temporary password" error={errors.password?.message} hint="Minimum 8 characters.">
          <input type="password" autoComplete="new-password" {...register('password')} />
        </Field>
        {owner && (
          <label className="check">
            <input type="checkbox" {...register('isOwner')} /> Owner (unrestricted, all locations)
          </label>
        )}
        {!isOwnerFlag && (
          <div className="grid-2">
            <Field label="Location" error={errors.locationId?.message}>
              <select {...register('locationId')}>
                <option value="">Select…</option>
                {locations.map((l) => (
                  <option key={l.id} value={l.id}>
                    {l.name}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="Role">
              <select {...register('role')}>
                {ROLES.map((r) => (
                  <option key={r}>{r}</option>
                ))}
              </select>
            </Field>
          </div>
        )}
        <button className="btn btn-primary" disabled={create.isPending}>
          {create.isPending ? 'Creating…' : 'Create user'}
        </button>
      </form>
    </Modal>
  )
}

const editSchema = z.object({ displayName: z.string().trim().min(1, 'Name is required'), active: z.boolean(), isOwner: z.boolean() })
type EditValues = z.infer<typeof editSchema>

function EditDetails({ u, isSelf }: { u: UserDto; isSelf: boolean }) {
  const owner = useAuthStore((s) => !!s.user?.isOwner)
  const invalidate = useInvalidateUsers()
  const [saved, setSaved] = useState(false)
  const save = useMutation({
    mutationFn: (v: EditValues) =>
      userCalls.update({ id: u.id, body: { displayName: v.displayName, active: v.active, ...(owner ? { isOwner: v.isOwner } : {}) } }),
    onSuccess: invalidate,
  })
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isDirty },
  } = useForm<EditValues>({ resolver: zodResolver(editSchema), defaultValues: { displayName: u.displayName, active: u.active, isOwner: u.isOwner } })
  return (
    <form
      className="form"
      noValidate
      onSubmit={handleSubmit((v) => {
        setSaved(false)
        save.mutate(v, {
          onSuccess: () => {
            setSaved(true)
            reset(v)
          },
        })
      })}
    >
      {save.error && <div className="alert alert-error" role="alert">{errorMessage(save.error)}</div>}
      {saved && <div className="alert alert-ok" role="status">Saved.</div>}
      <Field label="Email">
        <input value={u.email} readOnly disabled />
      </Field>
      <Field label="Display name" error={errors.displayName?.message}>
        <input {...register('displayName')} />
      </Field>
      <label className="check">
        <input type="checkbox" disabled={isSelf} {...register('active')} /> Active
      </label>
      {owner && (
        <label className="check">
          <input type="checkbox" disabled={isSelf} {...register('isOwner')} /> Owner (unrestricted, all locations)
        </label>
      )}
      {isSelf && <small className="muted">You cannot deactivate or demote yourself.</small>}
      <button className="btn btn-primary" disabled={!isDirty || save.isPending}>
        {save.isPending ? 'Saving…' : 'Save details'}
      </button>
    </form>
  )
}

function Assignments({ u }: { u: UserDto }) {
  const me = useAuthStore((s) => s.user)
  const locations = useAssignableLocations()
  const invalidate = useInvalidateUsers()
  const assign = useMutation({ mutationFn: userCalls.assign, onSuccess: invalidate })
  const unassign = useMutation({ mutationFn: userCalls.unassign, onSuccess: invalidate })
  const [locationId, setLocationId] = useState('')
  const [role, setRole] = useState<AssignableRole>('Cashier')
  const error = assign.error ?? unassign.error
  // Admins can only change assignments at their own location.
  const editable = (id: string) => !!me?.isOwner || me?.location?.id === id

  if (u.isOwner) return <p className="muted">Owners work at every location and have no assignments.</p>

  return (
    <div className="form">
      {error && <div className="alert alert-error" role="alert">{errorMessage(error)}</div>}
      {u.locations.length === 0 && <p className="muted">Not assigned to any location, so this user cannot sign in.</p>}
      <ul className="row-list">
        {u.locations.map((a) => (
          <li key={a.locationId} className="row-item">
            <div className="row-main">
              <strong>{a.locationName}</strong>
            </div>
            <select
              aria-label={`Role at ${a.locationName}`}
              className="role-select"
              value={a.role}
              disabled={!editable(a.locationId) || assign.isPending}
              onChange={(e) => assign.mutate({ id: u.id, locationId: a.locationId, role: e.target.value as AssignableRole })}
            >
              {ROLES.map((r) => (
                <option key={r}>{r}</option>
              ))}
            </select>
            <button
              className="btn btn-sm btn-danger-ghost"
              disabled={!editable(a.locationId) || unassign.isPending}
              onClick={() => window.confirm(`Remove ${u.displayName} from ${a.locationName}?`) && unassign.mutate({ id: u.id, locationId: a.locationId })}
            >
              Remove
            </button>
          </li>
        ))}
      </ul>
      <div className="assign-add">
        <Field label="Add to location">
          <select value={locationId} onChange={(e) => setLocationId(e.target.value)}>
            <option value="">Select…</option>
            {locations
              .filter((l) => !u.locations.some((a) => a.locationId === l.id))
              .map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name}
                </option>
              ))}
          </select>
        </Field>
        <Field label="Role">
          <select value={role} onChange={(e) => setRole(e.target.value as AssignableRole)}>
            {ROLES.map((r) => (
              <option key={r}>{r}</option>
            ))}
          </select>
        </Field>
        <button
          className="btn btn-primary"
          disabled={!locationId || assign.isPending}
          onClick={() => assign.mutate({ id: u.id, locationId, role }, { onSuccess: () => setLocationId('') })}
        >
          Assign
        </button>
      </div>
    </div>
  )
}

function EditUserModal({ id, onClose }: { id: string; onClose: () => void }) {
  const meId = useAuthStore((s) => s.user?.id)
  const q = useUser(id)
  return (
    <Modal title={q.data ? `Edit ${q.data.displayName}` : 'Edit staff member'} onClose={onClose} wide>
      {q.isLoading && <Spinner />}
      {q.error && <ErrorBox error={q.error} onRetry={() => void q.refetch()} />}
      {q.data && (
        <>
          <EditDetails key={`${q.data.id}-${q.data.displayName}-${q.data.active}-${q.data.isOwner}`} u={q.data} isSelf={q.data.id === meId} />
          <h3 className="section-title">Locations and roles</h3>
          <Assignments u={q.data} />
        </>
      )}
    </Modal>
  )
}

type Dialog = { kind: 'new' } | { kind: 'edit'; id: string } | null

export function UsersPage() {
  const me = useAuthStore((s) => s.user)
  const owner = !!me?.isOwner
  const locations = useLocations(owner)
  const [search, setSearch] = useState('')
  const [locationId, setLocationId] = useState('')
  const [page, setPage] = useState(1)
  const [dialog, setDialog] = useState<Dialog>(null)
  const invalidate = useInvalidateUsers()
  const q = useUsers({ search: search.trim(), locationId: owner ? locationId : '', page })
  const deactivate = useMutation({ mutationFn: userCalls.deactivate, onSuccess: invalidate })
  const close = () => setDialog(null)

  return (
    <section className="page">
      <PageHeader title="Staff">
        <button className="btn btn-primary" onClick={() => setDialog({ kind: 'new' })}>
          + New staff member
        </button>
      </PageHeader>
      <div className="toolbar">
        <input
          type="search"
          placeholder="Search name or email…"
          aria-label="Search staff"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value)
            setPage(1)
          }}
        />
        {owner && (
          <select
            aria-label="Filter by location"
            value={locationId}
            onChange={(e) => {
              setLocationId(e.target.value)
              setPage(1)
            }}
          >
            <option value="">All locations</option>
            {(locations.data ?? []).map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
              </option>
            ))}
          </select>
        )}
      </div>
      {!owner && me?.location && <p className="muted">Showing staff at {me.location.name}.</p>}

      {q.isLoading && <Spinner label="Loading staff…" />}
      {q.error && <ErrorBox error={q.error} onRetry={() => void q.refetch()} />}
      {deactivate.error && <ErrorBox error={deactivate.error} />}
      {q.data && q.data.data.length === 0 && <p className="muted">No staff match.</p>}

      <ul className="row-list">
        {q.data?.data.map((u) => (
          <li key={u.id} className={`row-item${u.active ? '' : ' row-inactive'}`}>
            <div className="row-main">
              <strong>{u.displayName}</strong>
              <small className="muted">{u.email}</small>
              <span className="chips">
                {u.isOwner && <span className="badge badge-role">Owner</span>}
                {u.locations.map((a) => (
                  <span key={a.locationId} className="chip">
                    {a.locationName}: {a.role}
                  </span>
                ))}
                {!u.active && <span className="badge badge-canceled">Inactive</span>}
              </span>
            </div>
            <div className="row-actions">
              <button className="btn btn-sm btn-primary" onClick={() => setDialog({ kind: 'edit', id: u.id })}>
                Edit
              </button>
              {u.active && u.id !== me?.id && (
                <button
                  className="btn btn-sm btn-danger-ghost"
                  disabled={deactivate.isPending}
                  onClick={() => window.confirm(`Deactivate ${u.displayName}? They will be signed out and unable to log in.`) && deactivate.mutate(u.id)}
                >
                  Deactivate
                </button>
              )}
            </div>
          </li>
        ))}
      </ul>
      {q.data && <Pager page={q.data.page} totalPages={q.data.totalPages} onPage={setPage} />}

      {dialog?.kind === 'new' && <CreateUserModal onClose={close} />}
      {dialog?.kind === 'edit' && <EditUserModal id={dialog.id} onClose={close} />}
    </section>
  )
}
