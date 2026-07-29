export type DeleteKind = 'soft' | 'hard'

// Confirm copy for delete/purge actions — i18n via an injected translator; callers pass
// their own `t` from useI18n() so the copy renders in the active UI locale.
type Translate = (key: string) => string

export function deleteKindFor(meta: { softDelete?: boolean } | null | undefined): DeleteKind {
  return meta?.softDelete ? 'soft' : 'hard'
}

export function deleteConfirm(t: Translate, kind: DeleteKind): { header: string; message: string } {
  return kind === 'soft'
    ? { header: t('confirm.softDeleteHeader'), message: t('confirm.softDeleteMessage') }
    : { header: t('confirm.hardDeleteHeader'), message: t('confirm.hardDeleteMessage') }
}

export function purgeConfirm(t: Translate): { header: string; message: string } {
  return { header: t('confirm.purgeHeader'), message: t('confirm.purgeMessage') }
}
