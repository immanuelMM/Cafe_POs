import { Modal } from '../../components/Modal'
import { ErrorBox, Spinner } from '../../components/ui'
import type { Item } from '../../lib/types'
import { assetUrl } from '../branding/brandingApi'
import { useItems } from '../catalog/catalogApi'

/** Pick a catalog item photo to place on the canvas. */
export function ItemPhotoPicker({ onPick, onClose }: { onPick: (item: Item) => void; onClose: () => void }) {
  const items = useItems({ pageSize: 100 })
  const withPhoto = (items.data?.data ?? []).filter((i) => i.imageUrl)
  return (
    <Modal title="Insert an item photo" onClose={onClose} wide>
      {items.isLoading && <Spinner label="Loading items…" />}
      {items.error && <ErrorBox error={items.error} onRetry={() => void items.refetch()} />}
      {items.data && withPhoto.length === 0 && <p className="muted">No menu items have a photo yet. Add one in Catalog.</p>}
      <ul className="studio-photo-grid">
        {withPhoto.map((i) => (
          <li key={i.id}>
            <button
              type="button"
              className="studio-photo"
              onClick={() => {
                onPick(i)
                onClose()
              }}
            >
              <img src={assetUrl(i.imageUrl) ?? ''} alt="" loading="lazy" />
              <span>{i.name}</span>
            </button>
          </li>
        ))}
      </ul>
    </Modal>
  )
}
