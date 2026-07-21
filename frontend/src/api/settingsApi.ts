import { apiClient } from './apiClient'

export type BrandingUpdate = { brandName: string; logoFileId: string | null }
export type BrandingResult = { brandName: string; brandLogoUrl: string | null }

export function updateBranding(body: BrandingUpdate): Promise<BrandingResult> {
  return apiClient.put<BrandingResult>('/settings/branding', body)
}
