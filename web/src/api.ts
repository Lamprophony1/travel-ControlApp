let csrfToken: string | undefined
export function resetCsrfToken() { csrfToken = undefined }

export class ApiError extends Error {
  constructor(message: string, public status: number, public details?: unknown) { super(message) }
}

async function ensureCsrf() {
  if (csrfToken) return csrfToken
  const response = await fetch('/api/auth/csrf', { credentials: 'include', cache: 'no-store' })
  if (!response.ok) throw new ApiError('No se pudo iniciar la sesión segura.', response.status)
  csrfToken = (await response.json() as { token: string }).token
  return csrfToken
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase()
  const headers = new Headers(options.headers)
  if (method !== 'GET' && method !== 'HEAD') headers.set('X-XSRF-TOKEN', await ensureCsrf())
  if (options.body && !(options.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  const response = await fetch(path, { ...options, headers, credentials: 'include', cache: 'no-store' })
  if (response.status === 204) return undefined as T
  const contentType = response.headers.get('content-type') ?? ''
  const body = contentType.includes('json') ? await response.json() : await response.text()
  if (!response.ok) {
    const record = body as { message?: string; detail?: string; title?: string }
    throw new ApiError(record.message ?? record.detail ?? record.title ?? 'No pudimos completar la operación.', response.status, body)
  }
  return body as T
}

export const postJson = <T>(path: string, body: unknown) => api<T>(path, { method: 'POST', body: JSON.stringify(body) })
export const putJson = <T>(path: string, body: unknown) => api<T>(path, { method: 'PUT', body: JSON.stringify(body) })
