export function formatDate(value: string | null | undefined): string {
  if (!value) return '-'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN')
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

// The API rejects naive datetimes, so local picker values must be converted
// to a UTC ISO string (or null to clear the field) before being submitted.
export function toUtcIsoString(value: Date | null): string | null {
  return value ? value.toISOString() : null
}
