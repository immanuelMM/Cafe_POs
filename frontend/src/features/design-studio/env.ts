import { createContext, useContext } from 'react'
import type { BrandPalette } from '../../lib/color'
import type { Section } from '../branding/menuSections'

/** Live data the canvas renders from (current logo URL, catalog menu). */
export interface StudioEnv {
  logoUrl: string | null
  sections: Section[]
  palette: BrandPalette
}

export const StudioEnvContext = createContext<StudioEnv>({
  logoUrl: null,
  sections: [],
  palette: { primary: '#3b2417' },
})
export const useStudioEnv = () => useContext(StudioEnvContext)
