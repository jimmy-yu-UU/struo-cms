import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

// The `cn` helper every vendored shadcn-vue component imports. clsx flattens the
// conditional class inputs; twMerge then resolves same-group Tailwind conflicts so
// a caller's `class` prop reliably beats a component's own default utility.
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs))
}
