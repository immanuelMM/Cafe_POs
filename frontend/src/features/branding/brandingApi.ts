import { useEffect } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { API_BASE, api } from '../../lib/api'
import { HEX_RE, brandBackground, darken, isGradient, onBrand, type BrandPalette } from '../../lib/color'

export interface BrandingDto {
  brandName: string
  tagline: string | null
  primaryColor: string
  secondaryColor: string | null
  tertiaryColor: string | null
  gradient: boolean
  gradientAngle: number
  logoUrl: string | null
  tarpaulinUrl: string | null
  tarpaulinContentType: string | null
}

export const DEFAULT_BRAND_NAME = 'Brew & Bean'
export const DEFAULT_BRAND_COLOR = '#3b2417'
export const DEFAULT_GRADIENT_ANGLE = 135

/** The colour/gradient part of the branding as a palette (null while unloaded or if the primary colour is invalid). */
export const toPalette = (b: BrandingDto | undefined): BrandPalette | null =>
  b && HEX_RE.test(b.primaryColor)
    ? {
        primary: b.primaryColor,
        secondary: b.secondaryColor,
        tertiary: b.tertiaryColor,
        gradient: b.gradient,
        angle: b.gradientAngle,
      }
    : null

/** Palette for the current branding, falling back to the default brand colour while loading. */
export const useBrandPalette = (): BrandPalette => toPalette(useBranding().data) ?? { primary: DEFAULT_BRAND_COLOR }

const BRAND_VARS = [
  '--brand',
  '--brand-2',
  '--on-brand',
  '--brand-secondary',
  '--brand-tertiary',
  '--brand-bg',
  '--brand-highlight',
  '--login-bg',
]

/** API origin (API_BASE without the trailing /api/v1); branding asset URLs are relative to it. */
export const API_ORIGIN = API_BASE.replace(/\/api\/v1\/?$/, '')
export const assetUrl = (path: string | null | undefined) => (path ? `${API_ORIGIN}${path}` : null)

/** Business branding (anonymous: the login screen needs it before sign-in). */
export const useBranding = () =>
  useQuery({
    queryKey: ['branding'],
    queryFn: () => api<BrandingDto>('/branding', { anonymous: true }),
    staleTime: 5 * 60_000,
  })

/** The brand name to render; falls back to the default while loading or if the API is unreachable. */
export function useBrandName(): string {
  const q = useBranding()
  const name = q.data?.brandName ?? DEFAULT_BRAND_NAME
  useEffect(() => {
    document.title = `${name} POS`
  }, [name])
  return name
}

/** Absolute logo URL, or null (callers fall back to the coffee emoji). */
export const useLogoUrl = () => assetUrl(useBranding().data?.logoUrl)

/** Applies the brand colour (+ derived tokens) and favicon to the document. Mount once, near the app root. */
export function useApplyBranding() {
  const { data } = useBranding()
  const palette = toPalette(data)
  const primary = palette?.primary ?? null
  const secondary = palette?.secondary ?? null
  const tertiary = palette?.tertiary ?? null
  const gradient = palette?.gradient ?? false
  const angle = palette?.angle ?? DEFAULT_GRADIENT_ANGLE
  const logo = assetUrl(data?.logoUrl)
  useEffect(() => {
    const root = document.documentElement.style
    if (!primary) {
      for (const k of BRAND_VARS) root.removeProperty(k)
      return
    }
    const p: BrandPalette = { primary, secondary, tertiary, gradient, angle }
    const bg = brandBackground(p)
    root.setProperty('--brand', primary)
    root.setProperty('--brand-2', darken(primary, 0.2))
    root.setProperty('--on-brand', onBrand(p))
    root.setProperty('--brand-bg', bg)
    const set = (k: string, v: string | null) => (v ? root.setProperty(k, v) : root.removeProperty(k))
    set('--brand-secondary', secondary)
    set('--brand-tertiary', secondary ? tertiary : null)
    // Subtle accents (active nav underline) use the secondary colour only when it is not already part of a gradient.
    set('--brand-highlight', secondary && !isGradient(p) ? secondary : null)
    set('--login-bg', isGradient(p) ? bg : null)
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', primary)
  }, [primary, secondary, tertiary, gradient, angle])
  useEffect(() => {
    if (!logo) return
    let link = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
    if (!link) {
      link = document.createElement('link')
      link.rel = 'icon'
      document.head.appendChild(link)
    }
    const prev = link.href
    link.href = logo
    return () => {
      link.href = prev
    }
  }, [logo])
}

export interface BrandingInput {
  brandName: string
  tagline: string | null
  primaryColor: string
  secondaryColor: string | null
  tertiaryColor: string | null
  gradient: boolean
  gradientAngle: number
}

function useBrandingMutation<T>(fn: (v: T) => Promise<BrandingDto>) {
  const qc = useQueryClient()
  return useMutation({ mutationFn: fn, onSuccess: (data) => qc.setQueryData(['branding'], data) })
}

export const useUpdateBranding = () =>
  useBrandingMutation((body: BrandingInput) => api<BrandingDto>('/branding', { method: 'PUT', body }))

const upload = (path: string) => (file: Blob | File) => {
  const fd = new FormData()
  fd.append('file', file, file instanceof File ? file.name : 'logo.png')
  return api<BrandingDto>(path, { method: 'PUT', body: fd })
}
export const useUploadLogo = () => useBrandingMutation(upload('/branding/logo'))
export const useDeleteLogo = () => useBrandingMutation(() => api<BrandingDto>('/branding/logo', { method: 'DELETE' }))
export const useUploadTarpaulin = () => useBrandingMutation(upload('/branding/tarpaulin'))
export const useDeleteTarpaulin = () =>
  useBrandingMutation(() => api<BrandingDto>('/branding/tarpaulin', { method: 'DELETE' }))
