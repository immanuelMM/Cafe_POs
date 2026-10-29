import { useEffect, useMemo, useRef, useState } from 'react'
import { ErrorBox, PageHeader, Spinner } from '../../components/ui'
import { errorMessage } from '../../lib/errors'
import { useAuthStore } from '../auth/authStore'
import { useSwitchLocation } from '../locations/useLocations'
import { formatMoney } from '../../lib/format'
import type { Item } from '../../api/types'
import { assetUrl } from '../branding/useBranding'
import { IconCart, IconSearch, IconX } from '../../components/icons'
import { useCategories, useItems, useModifierLists } from '../catalog/useCatalog'
import { CartPanel } from './CartPanel'
import { CategoryTabs } from './CategoryTabs'
import { ModifierSheet } from './ModifierSheet'
import { PaymentPanel } from './PaymentPanel'
import { cartCount, cartTotal, useCartStore } from './cartStore'
import './pos.css'

/** Owner "All locations" session: orders are location-scoped, so ask for a location first. */
function PickLocationPrompt() {
  const user = useAuthStore((s) => s.user)
  const sw = useSwitchLocation()
  return (
    <section className="page">
      <PageHeader title="Choose a location" />
      <div className="card form">
        <p>
          You are viewing <strong>All locations</strong>. Orders are taken at one location, so pick where you are
          working to start the POS.
        </p>
        {sw.error && (
          <div className="alert alert-error" role="alert">
            {errorMessage(sw.error)}
          </div>
        )}
        <div className="loc-picks">
          {(user?.locations ?? []).map((l) => (
            <button
              key={l.locationId}
              className="btn btn-primary loc-tile"
              disabled={sw.isPending}
              onClick={() => sw.mutate(l.locationId)}
            >
              {l.locationName}
            </button>
          ))}
        </div>
        {user && user.locations.length === 0 && (
          <p className="muted">No active locations exist yet. Create one under Locations.</p>
        )}
      </div>
    </section>
  )
}

export function PosPage() {
  const hasLocation = useAuthStore((s) => !!s.user?.location)
  return hasLocation ? <PosScreen /> : <PickLocationPrompt />
}

const priceOf = (v: { price: number; effectivePrice?: number }) => v.effectivePrice ?? v.price

function PosScreen() {
  const [categoryId, setCategoryId] = useState<string>('')
  const [search, setSearch] = useState('')
  const [picked, setPicked] = useState<Item | null>(null)
  const [paying, setPaying] = useState(false)
  const [searchOpen, setSearchOpen] = useState(false) // compact only: the search field replaces the category chips
  const searchRef = useRef<HTMLInputElement>(null)
  const searchShown = searchOpen || search !== ''
  useEffect(() => {
    if (searchOpen) searchRef.current?.focus()
  }, [searchOpen])

  const cats = useCategories()
  // A search looks across every category (and shows "All" selected); clearing it brings the chosen category back.
  // A category that no longer exists (deactivated, refetch) counts as All.
  const searching = search.trim() !== ''
  const activeCat = searching || !(cats.data ?? []).some((c) => c.id === categoryId) ? '' : categoryId
  const items = useItems({ categoryId: activeCat || undefined, search: search.trim() || undefined })
  const mods = useModifierLists()
  const { lines, add, setSheetOpen } = useCartStore()

  const listsFor = useMemo(() => {
    const byId = new Map((mods.data ?? []).filter((m) => m.active).map((m) => [m.id, m]))
    return (item: Item) =>
      item.modifierListIds
        .map((id) => byId.get(id))
        .filter((m): m is NonNullable<typeof m> => !!m && m.modifiers.some((x) => x.active))
  }, [mods.data])

  const onTile = (item: Item) => {
    const vars = item.variations.filter((v) => v.active)
    if (vars.length === 0) return
    if (vars.length === 1 && listsFor(item).length === 0) {
      add({
        itemName: item.name,
        variationId: vars[0].id,
        variationName: vars[0].name,
        basePrice: priceOf(vars[0]),
        modifiers: [],
        quantity: 1,
      })
    } else {
      setPicked(item)
    }
  }

  const priceLabel = (item: Item) => {
    const prices = item.variations.filter((v) => v.active).map(priceOf)
    if (!prices.length) return 'Unavailable'
    return (
      <>
        {prices.length > 1 && <span className="tile-from">from </span>}
        {formatMoney(Math.min(...prices))}
      </>
    )
  }

  const total = cartTotal(lines)

  return (
    <div className="pos">
      <section className="pos-menu" aria-label="Menu">
        <div className={`pos-tools${searchShown ? ' pos-tools-search' : ''}`}>
          <CategoryTabs
            categories={cats.data ?? []}
            value={activeCat}
            onChange={(id) => {
              setCategoryId(id)
              setSearch('')
            }}
          />
          <div className="pos-search">
            <input
              ref={searchRef}
              className="search"
              type="search"
              placeholder="Search items…"
              aria-label="Search items"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            {search && (
              <button
                type="button"
                className="btn btn-ghost btn-icon pos-search-clear"
                aria-label="Clear search"
                onClick={() => {
                  setSearch('')
                  searchRef.current?.focus()
                }}
              >
                <IconX />
              </button>
            )}
          </div>
          <button
            type="button"
            className="btn btn-icon pos-search-toggle"
            aria-label={searchShown ? 'Close search' : 'Show search'}
            aria-expanded={searchShown}
            onClick={() => {
              if (searchShown) {
                setSearch('')
                setSearchOpen(false)
              } else {
                setSearchOpen(true)
              }
            }}
          >
            {searchShown ? <IconX /> : <IconSearch />}
          </button>
        </div>

        <div className="pos-browse">
          {(items.isLoading || cats.isLoading) && <Spinner label="Loading menu…" />}
          {items.error && <ErrorBox error={items.error} onRetry={() => void items.refetch()} />}
          {cats.error && <ErrorBox error={cats.error} onRetry={() => void cats.refetch()} />}
          {items.data && items.data.data.length === 0 && <p className="muted">No items found.</p>}

          <div className="tiles">
            {items.data?.data.map((item) => {
              const unavailable = item.variations.every((v) => !v.active)
              return (
                <button
                  key={item.id}
                  className={`tile${item.imageUrl ? ' tile-photo' : ''}`}
                  disabled={unavailable}
                  onClick={() => onTile(item)}
                >
                  {item.imageUrl && (
                    <img
                      className="tile-img"
                      src={assetUrl(item.imageUrl) ?? ''}
                      alt={item.name}
                      loading="lazy"
                      draggable={false}
                    />
                  )}
                  <span className="tile-text">
                    <span className="tile-name">{item.name}</span>
                    <span className={`tile-price${unavailable ? ' tile-unavailable' : ''}`}>{priceLabel(item)}</span>
                  </span>
                </button>
              )
            })}
          </div>
          {items.data && items.data.totalCount > items.data.data.length && (
            <p className="muted">
              Showing {items.data.data.length} of {items.data.totalCount}. Use search or a category to narrow down.
            </p>
          )}
        </div>
      </section>

      <CartPanel onCharge={() => setPaying(true)} />

      <button className="cart-bar" onClick={() => setSheetOpen(true)} aria-label="Open current order">
        <span>
          <IconCart /> {cartCount(lines)} item{cartCount(lines) === 1 ? '' : 's'}
        </span>
        <strong>{formatMoney(total)}</strong>
        <span>View order ›</span>
      </button>

      {picked && (
        <ModifierSheet
          item={picked}
          lists={listsFor(picked)}
          onClose={() => setPicked(null)}
          onAdd={(line) => {
            add(line)
            setPicked(null)
          }}
        />
      )}
      {paying && (
        <PaymentPanel
          onClose={() => {
            setPaying(false)
          }}
        />
      )}
    </div>
  )
}
