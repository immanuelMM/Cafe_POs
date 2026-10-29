import { z } from 'zod'

const num = (msg = 'Enter a number') => z.number({ invalid_type_error: msg })

export const categorySchema = z.object({
  name: z.string().trim().min(1, 'Name is required').max(100),
  sortOrder: z.number({ invalid_type_error: 'Enter a number' }).int('Whole number'),
  active: z.boolean(),
})
export type CategoryValues = z.infer<typeof categorySchema>

export const variationSchema = z.object({
  name: z.string().trim().min(1, 'Name required'),
  sku: z.string().trim().min(1, 'SKU required'),
  price: num().min(0, 'Must be 0 or more'),
})

export const itemSchema = z.object({
  categoryId: z.string().min(1, 'Choose a category'),
  name: z.string().trim().min(1, 'Name is required'),
  description: z.string().trim().optional(),
  sortOrder: num().int(),
  active: z.boolean(),
  modifierListIds: z.array(z.string()),
  variations: z.array(variationSchema),
})
export type ItemValues = z.infer<typeof itemSchema>

export const modifierListSchema = z
  .object({
    name: z.string().trim().min(1, 'Name is required'),
    minSelections: num().int().min(0),
    maxSelections: num().int().min(1, 'At least 1'),
    active: z.boolean(),
    modifiers: z
      .array(
        z.object({
          id: z.string().optional(),
          name: z.string().trim().min(1, 'Name required'),
          priceDelta: num(),
          active: z.boolean(),
        }),
      )
      .min(1, 'Add at least one modifier'),
  })
  .refine((v) => v.minSelections <= v.maxSelections, { path: ['minSelections'], message: 'Min cannot exceed max' })
export type ModifierListValues = z.infer<typeof modifierListSchema>
