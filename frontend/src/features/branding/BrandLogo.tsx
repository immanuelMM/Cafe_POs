import { useLogoUrl } from './useBranding'

/** The uploaded logo, or the coffee emoji when none is set. Decorative: the brand name is rendered next to it. */
export function BrandLogo({ className = 'brand-logo' }: { className?: string }) {
  const url = useLogoUrl()
  if (url) return <img className={className} src={url} alt="" />
  return <span aria-hidden="true">☕</span>
}
