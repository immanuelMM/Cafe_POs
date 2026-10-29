import { useSearchParams } from 'react-router'
import { ErrorBox, Spinner } from '../../components/ui'
import { useDesign } from './useDesign'
import { StudioEditor } from './StudioEditor'
import { useStudio } from './store'
import type { DesignKind } from './types'
import '../branding/branding.css'
import './design-studio.css'

const KINDS: { kind: DesignKind; label: string }[] = [
  { kind: 'logo', label: 'Logo' },
  { kind: 'banner', label: 'Banner' },
]

/** /admin/design-studio?kind=logo|banner : free-form logo and banner editor (lazy-loaded route). */
export default function DesignStudioPage() {
  const [params, setParams] = useSearchParams()
  const kind: DesignKind = params.get('kind') === 'banner' ? 'banner' : 'logo'
  const design = useDesign(kind)

  const switchKind = (next: DesignKind) => {
    if (next === kind) return
    if (
      useStudio.getState().dirty &&
      !window.confirm('You have unsaved changes (a local draft is kept in this browser). Switch anyway?')
    )
      return
    setParams({ kind: next }, { replace: true })
  }

  const tabs = (
    <div className="tabs studio-kinds" role="tablist" aria-label="Design type">
      {KINDS.map((k) => (
        <button
          key={k.kind}
          type="button"
          role="tab"
          aria-selected={kind === k.kind}
          className={kind === k.kind ? 'tab tab-on' : 'tab'}
          onClick={() => switchKind(k.kind)}
        >
          {k.label}
        </button>
      ))}
    </div>
  )

  if (design.isLoading) return <Spinner label="Loading your design…" />
  if (design.error)
    return (
      <section className="page">
        {tabs}
        <ErrorBox error={design.error} onRetry={() => void design.refetch()} />
      </section>
    )
  return <StudioEditor key={kind} kind={kind} saved={design.data ?? null} tabs={tabs} />
}
