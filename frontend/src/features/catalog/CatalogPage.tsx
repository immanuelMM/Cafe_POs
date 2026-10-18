import { useState } from 'react'
import { PageHeader } from '../../components/ui'
import { CategoriesTab } from './CategoriesTab'
import { ItemsTab } from './ItemsTab'
import { ModifiersTab } from './ModifiersTab'

const TABS = [
  { id: 'items', label: 'Items' },
  { id: 'categories', label: 'Categories' },
  { id: 'modifiers', label: 'Modifier lists' },
] as const

export function CatalogPage() {
  const [tab, setTab] = useState<(typeof TABS)[number]['id']>('items')
  return (
    <section className="page">
      <PageHeader title="Catalog" />
      <div className="tabs" role="tablist">
        {TABS.map((t) => (
          <button key={t.id} role="tab" aria-selected={tab === t.id} className={`tab${tab === t.id ? ' tab-on' : ''}`} onClick={() => setTab(t.id)}>
            {t.label}
          </button>
        ))}
      </div>
      {tab === 'items' && <ItemsTab />}
      {tab === 'categories' && <CategoriesTab />}
      {tab === 'modifiers' && <ModifiersTab />}
    </section>
  )
}
