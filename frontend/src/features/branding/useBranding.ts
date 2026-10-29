import { useEffect } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { Branding } from '../../api/types'
import type { components } from '../../api/schema'
import { API_ORIGIN as ORIGIN, anonClient, client, fileBody, unwrap } from '../../lib/apiClient'
import { HEX_RE, brandBackground, darken, isGradient, onBrand, type BrandPalette } from '../../lib/color'

export type BrandingDto = Branding

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

/** Branding asset URLs are relative to the API origin. */
export const API_ORIGIN = ORIGIN
export const assetUrl = (path: string | null | undefined) => (path ? `${ORIGIN}${path}` : null)

/** Business branding (anonymous: the login screen needs it before sign-in). */
export const useBranding = () =>
  useQuery({
    queryKey: ['branding'],
    queryFn: () => unwrap(anonClient.GET('/api/v1/branding')),
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

export type BrandingInput = components['schemas']['UpdateBrandingRequest']

function useBrandingMutation<T>(fn: (v: T) => Promise<BrandingDto>) {
  const qc = useQueryClient()
  return useMutation({ mutationFn: fn, onSuccess: (data) => qc.setQueryData(['branding'], data) })
}

export const useUpdateBranding = () =>
  useBrandingMutation((body: BrandingInput) => unwrap(client.PUT('/api/v1/branding', { body })))

const filename = (file: Blob | File) => (file instanceof File ? file.name : 'logo.png')
export const useUploadLogo = () =>
  useBrandingMutation((file: Blob | File) =>
    unwrap(client.PUT('/api/v1/branding/logo', fileBody(file, filename(file)))),
  )
export const useDeleteLogo = () => useBrandingMutation(() => unwrap(client.DELETE('/api/v1/branding/logo')))
export const useUploadTarpaulin = () =>
  useBrandingMutation((file: Blob | File) =>
    unwrap(client.PUT('/api/v1/branding/tarpaulin', fileBody(file, filename(file)))),
  )
export const useDeleteTarpaulin = () => useBrandingMutation(() => unwrap(client.DELETE('/api/v1/branding/tarpaulin')))
