import type { Category, Item } from '../../api/types'

export interface Section {
  category: Category
  items: Item[]
}

/** Items grouped by category (in category sort order), hiding inactive items/variations unless asked. */
export function buildSections(categories: Category[], items: Item[], showInactive: boolean): Section[] {
  return [...categories]
    .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name))
    .map((category) => ({
      category,
      items: items
        .filter((i) => i.categoryId === category.id && (showInactive || i.active))
        .map((i) => ({ ...i, variations: i.variations.filter((v) => showInactive || v.active) }))
        .filter((i) => i.variations.length > 0)
        .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name)),
    }))
    .filter((s) => (showInactive || s.category.active) && s.items.length > 0)
}
