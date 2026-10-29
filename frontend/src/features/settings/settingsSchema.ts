import { z } from 'zod'

export const locationSettingsSchema = z.object({
  name: z.string().trim().min(1, 'Name is required'),
  address: z.string().trim().optional(),
  timeZone: z.string().trim().min(1, 'Time zone is required'),
  contactPhone: z.string().trim().optional(),
  vatRatePercent: z.number({ invalid_type_error: 'Enter a number' }).min(0, 'Min 0').max(100, 'Max 100'),
})
export type LocationSettingsValues = z.infer<typeof locationSettingsSchema>
