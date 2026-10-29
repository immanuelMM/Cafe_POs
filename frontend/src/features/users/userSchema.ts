import { z } from 'zod'

export const createUserSchema = z
  .object({
    email: z.string().trim().min(1, 'Email is required').email('Enter a valid email'),
    displayName: z.string().trim().min(1, 'Name is required'),
    password: z.string().min(8, 'At least 8 characters'),
    isOwner: z.boolean(),
    locationId: z.string(),
    role: z.enum(['Cashier', 'Manager', 'Admin']),
  })
  .refine((v) => v.isOwner || v.locationId !== '', { path: ['locationId'], message: 'Choose a location' })
export type CreateValues = z.infer<typeof createUserSchema>

export const editUserSchema = z.object({
  displayName: z.string().trim().min(1, 'Name is required'),
  active: z.boolean(),
  isOwner: z.boolean(),
})
export type EditValues = z.infer<typeof editUserSchema>

export const existingUserSchema = z.object({
  email: z.string().trim().min(1, 'Email is required').email('Enter a valid email'),
  locationId: z.string().min(1, 'Choose a location'),
  role: z.enum(['Cashier', 'Manager', 'Admin']),
})
export type ExistingValues = z.infer<typeof existingUserSchema>
