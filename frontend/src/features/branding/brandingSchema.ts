import { z } from 'zod'
import { HEX_RE } from '../../lib/color'

const colorMsg = 'Use a colour like #3b2417'
const optColor = z.string().regex(HEX_RE, colorMsg).nullable()
export const brandingSchema = z
  .object({
    brandName: z.string().trim().min(1, 'Brand name is required').max(100, 'Max 100 characters'),
    tagline: z.string().trim().max(200, 'Max 200 characters'),
    primaryColor: z.string().regex(HEX_RE, colorMsg),
    secondaryColor: optColor,
    tertiaryColor: optColor,
    gradient: z.boolean(),
    gradientAngle: z.number().int().min(0).max(360),
  })
  // Same rules the server enforces (400 otherwise).
  .superRefine((v, ctx) => {
    if (v.tertiaryColor !== null && v.secondaryColor === null)
      ctx.addIssue({ code: 'custom', path: ['tertiaryColor'], message: 'Add a second colour first' })
    if (v.gradient && v.secondaryColor === null)
      ctx.addIssue({ code: 'custom', path: ['gradient'], message: 'Add a second color to use a gradient' })
  })
export type BrandingValues = z.infer<typeof brandingSchema>
