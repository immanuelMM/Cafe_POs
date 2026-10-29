import { z } from 'zod'

const num = (msg = 'Enter a number') => z.number({ invalid_type_error: msg })

export const ingSchema = z.object({
  name: z.string().trim().min(1, 'Name is required'),
  unit: z.string().trim().min(1, 'Unit is required (e.g. ml, g, each)'),
  lowStockThreshold: num().min(0, 'Must be 0 or more'),
  initialQuantity: num().min(0, 'Must be 0 or more'),
  active: z.boolean(),
})
export type IngValues = z.infer<typeof ingSchema>

export const restockSchema = z.object({
  quantity: num().positive('Must be greater than 0'),
  unitCost: z.number().min(0).optional(),
  supplier: z.string().trim().optional(),
  note: z.string().trim().optional(),
})
export type RestockValues = z.infer<typeof restockSchema>

export const adjSchema = z.object({
  kind: z.enum(['Adjustment+', 'Adjustment-', 'Waste']),
  quantity: num().positive('Must be greater than 0'),
  note: z.string().trim().min(1, 'Please add a note explaining why'),
})
export type AdjValues = z.infer<typeof adjSchema>
