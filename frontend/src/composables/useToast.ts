import { toast } from 'vue-sonner'

export type ToastSeverity = 'success' | 'info' | 'warn' | 'error'

export type ToastOptions = {
  severity?: ToastSeverity
  summary?: string
  detail?: string
  /** Milliseconds the toast stays up. Omit to use sonner's default. */
  life?: number
}

const EMITTERS: Record<ToastSeverity, (title: string, opts: { description?: string; duration?: number }) => unknown> = {
  success: toast.success,
  info: toast.info,
  warn: toast.warning,
  error: toast.error,
}

export function useToast(): { add(options: ToastOptions): void } {
  return {
    add({ severity = 'info', summary, detail, life }: ToastOptions): void {
      // A caller that passes only `detail` still deserves a visible message, so promote it
      // to the title rather than rendering an empty toast with a description.
      const title = summary ?? detail ?? ''
      const description = summary ? detail : undefined
      EMITTERS[severity](title, { description, duration: life })
    },
  }
}
