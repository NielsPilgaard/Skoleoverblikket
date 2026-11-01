import keycloak, { refreshToken } from '../auth/keycloak'
import { actingAsHeaders } from '../auth/actingAs'
import { API_BASE, ApiError } from './client'
import { apiFetch } from './outage'

export interface UploadOptions {
  file: File
  fileName?: string
  courseId?: string
  folderId?: string
  onProgress?: (pct: number) => void
}

export interface UploadedFile {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  url: string
  folderId?: string | null
}

export interface BoardUploadOptions {
  file: File
  fileName?: string
  folderId?: string
  onProgress?: (pct: number) => void
}

async function presignConfirmUpload(
  file: File,
  presignUrl: string,
  confirmUrl: string,
  presignBody: Record<string, unknown>,
  onProgress?: (pct: number) => void
): Promise<UploadedFile> {
  await refreshToken(30)

  const presignRes = await apiFetch(presignUrl, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(keycloak.token ? { Authorization: `Bearer ${keycloak.token}` } : {}),
      ...actingAsHeaders(),
    },
    body: JSON.stringify(presignBody),
  })

  if (!presignRes.ok) {
    const text = await presignRes.text().catch(() => presignRes.statusText)
    throw new ApiError(presignRes.status, text)
  }

  const { uploadUrl, confirmToken } = (await presignRes.json()) as {
    fileId: string
    uploadUrl: string
    confirmToken: string
  }

  // Upload directly to S3 via XHR so we get progress events
  await new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PUT', uploadUrl)
    xhr.setRequestHeader('Content-Type', file.type || 'application/octet-stream')

    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable && onProgress) {
        onProgress(Math.round((e.loaded / e.total) * 90)) // reserve 10% for confirm step
      }
    }

    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve()
      } else {
        reject(new Error(`S3 upload fejlede: ${xhr.status} ${xhr.statusText}`))
      }
    }

    xhr.onerror = () => reject(new Error('Netværksfejl under upload til S3.'))
    xhr.send(file)
  })

  onProgress?.(95)

  await refreshToken(30)
  const confirmRes = await apiFetch(confirmUrl, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(keycloak.token ? { Authorization: `Bearer ${keycloak.token}` } : {}),
      ...actingAsHeaders(),
    },
    body: JSON.stringify({ confirmToken }),
  })

  if (!confirmRes.ok) {
    const text = await confirmRes.text().catch(() => confirmRes.statusText)
    throw new ApiError(confirmRes.status, text)
  }

  onProgress?.(100)
  return confirmRes.json() as Promise<UploadedFile>
}

/**
 * Uploads a file using the presigned URL flow:
 * 1. POST /files/presign  → get S3 upload URL + confirm token
 * 2. PUT directly to S3   → progress events fire here
 * 3. POST /files/confirm  → register the file in the DB
 */
export async function uploadFile({
  file,
  fileName,
  courseId,
  folderId,
  onProgress,
}: UploadOptions): Promise<UploadedFile> {
  return presignConfirmUpload(
    file,
    `${API_BASE}/files/presign`,
    `${API_BASE}/files/confirm`,
    {
      fileName: fileName ?? file.name,
      fileSizeBytes: file.size,
      courseId: courseId || null,
      folderId: folderId || null,
    },
    onProgress
  )
}

export async function uploadBoardFile({
  file,
  fileName,
  folderId,
  onProgress,
}: BoardUploadOptions): Promise<UploadedFile> {
  return presignConfirmUpload(
    file,
    `${API_BASE}/board-files/presign`,
    `${API_BASE}/board-files/confirm`,
    {
      fileName: fileName ?? file.name,
      fileSizeBytes: file.size,
      folderId: folderId || null,
    },
    onProgress
  )
}
