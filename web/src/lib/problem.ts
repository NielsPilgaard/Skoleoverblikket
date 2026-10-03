/** The human-readable message from an API error (RFC 7807 ProblemDetails `detail`), if any. */
export function problemDetail(err: unknown): string | null {
  if (err && typeof err === 'object' && 'detail' in err) {
    const detail = (err as { detail?: unknown }).detail
    if (typeof detail === 'string' && detail.length > 0) return detail
  }
  return null
}
