import { useRef, useState, useEffect } from 'react'
import { Modal } from '../Modal'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1CoursesOptions,
  getApiV1FilesOptions,
  getApiV1FilesQueryKey,
  getApiV1BoardFilesOptions,
  getApiV1BoardFilesQueryKey,
} from '../../api/generated/@tanstack/react-query.gen'
import {
  deleteApiV1BoardFilesById,
  deleteApiV1BoardFilesFoldersById,
  deleteApiV1FilesById,
  deleteApiV1FilesFoldersById,
  patchApiV1BoardFilesFoldersById,
  patchApiV1FilesFoldersById,
  postApiV1BoardFilesFolders,
  postApiV1FilesFolders,
} from '../../api/generated'
import { uploadFile, uploadBoardFile } from '../../api/upload'
import type { CourseDto, FolderDto } from '../../api/client'
import type { BoardFilesControllerBoardFolderDto } from '../../api/generated/types.gen'
import keycloak from '../../auth/keycloak'
import {
  FileIcon,
  FolderIcon,
  ChevronIcon,
  PencilIcon,
  TrashIcon,
  UploadIcon,
  DownloadIcon,
  EyeIcon,
  CloseIcon,
} from './fileIcons'
import { formatBytes, formatDate } from './fileHelpers'

type Variant = 'staff' | 'board'

// Normalised shapes shared between variants
interface FileRow {
  id?: string
  fileName?: string | null
  contentType?: string | null
  sizeBytes?: number
  url?: string | null
  folderId?: string | null
  uploadedBy?: string | null
  uploadedAt?: string
  courseName?: string | null
}

interface FolderRow {
  id?: string
  name?: string | null
  parentId?: string | null
  createdAt?: string
  courseName?: string | null
}

// ─── Upload modal ─────────────────────────────────────────────────────────────

interface UploadModalProps {
  variant: Variant
  courses: CourseDto[] | undefined
  currentFolderId: string | null
  defaultCourseId?: string
  onClose: () => void
  onUploaded: (courseId: string) => void
}

function UploadModal({
  variant,
  courses,
  currentFolderId,
  defaultCourseId,
  onClose,
  onUploaded,
}: UploadModalProps) {
  const qc = useQueryClient()
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [editedName, setEditedName] = useState<string>('')
  const [courseId, setCourseId] = useState<string>(defaultCourseId ?? '')
  const [progress, setProgress] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const dragRef = useRef(false)
  const [isDragging, setIsDragging] = useState(false)

  const filesQueryKey = variant === 'board' ? getApiV1BoardFilesQueryKey() : getApiV1FilesQueryKey()

  const mutation = useMutation({
    mutationFn: async ({ file }: { file: File }) => {
      const ext = file.name.includes('.') ? `.${file.name.split('.').pop()!}` : ''
      if (variant === 'board') {
        return uploadBoardFile({
          file,
          fileName: editedName + ext,
          folderId: currentFolderId || undefined,
          onProgress: setProgress,
        })
      }
      return uploadFile({
        file,
        fileName: editedName + ext,
        courseId: courseId || undefined,
        folderId: currentFolderId || undefined,
        onProgress: setProgress,
      })
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: filesQueryKey })
      onUploaded(courseId)
    },
    onError: () => {
      setProgress(null)
      setError('Der opstod en fejl under upload. Prøv igen.')
    },
  })

  const isPending = mutation.isPending

  function handleDrop(e: React.DragEvent) {
    e.preventDefault()
    setIsDragging(false)
    dragRef.current = false
    const f = e.dataTransfer.files[0]
    if (f) {
      setError(null)
      setSelectedFile(f)
      setEditedName(f.name.replace(/\.[^.]+$/, ''))
    }
  }

  return (
    <Modal isOpen onClose={onClose} title="Upload fil">
      <div className="px-6 py-5 space-y-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Fil *</label>
          <button
            type="button"
            disabled={isPending}
            onClick={() => inputRef.current?.click()}
            onDragEnter={(e) => {
              e.preventDefault()
              if (!dragRef.current) {
                dragRef.current = true
                setIsDragging(true)
              }
            }}
            onDragOver={(e) => e.preventDefault()}
            onDragLeave={(e) => {
              if (!e.currentTarget.contains(e.relatedTarget as Node)) {
                dragRef.current = false
                setIsDragging(false)
              }
            }}
            onDrop={handleDrop}
            className={`w-full flex flex-col items-center justify-center border-2 border-dashed rounded-lg p-6 transition-colors ${
              isPending ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'
            } ${isDragging ? 'border-brand-400 bg-brand-50' : 'border-gray-300 hover:border-brand-400'}`}
          >
            {selectedFile ? (
              <div className="text-center w-full">
                <p className="text-sm font-medium text-gray-900 truncate">{selectedFile.name}</p>
                <p className="text-xs text-gray-500 mt-1">{formatBytes(selectedFile.size)}</p>
                {progress !== null && (
                  <div className="mt-3">
                    <div className="h-2 bg-gray-100 rounded-full overflow-hidden">
                      <div
                        className="h-full bg-brand-500 rounded-full transition-all duration-300"
                        style={{ width: `${progress}%` }}
                      />
                    </div>
                    <p className="text-xs text-gray-500 mt-1">
                      {progress < 100 ? `${progress}% uploadet…` : 'Fuldfører…'}
                    </p>
                  </div>
                )}
              </div>
            ) : (
              <div className="text-center">
                <UploadIcon size={24} />
                <p className="text-sm text-gray-500 mt-2">Klik eller træk fil hertil</p>
                <p className="text-xs text-gray-400 mt-1">
                  PDF, Word, Excel, video, lyd, billeder m.m. · Maks. 500 MB
                </p>
              </div>
            )}
          </button>
          <input
            ref={inputRef}
            type="file"
            accept="*/*"
            className="hidden"
            onChange={(e) => {
              const f = e.target.files?.[0] ?? null
              setError(null)
              setProgress(null)
              setSelectedFile(f)
              if (f) setEditedName(f.name.replace(/\.[^.]+$/, ''))
            }}
          />
        </div>

        {selectedFile && (
          <div>
            <label
              htmlFor="upload-filename"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              Filnavn
            </label>
            <input
              id="upload-filename"
              type="text"
              value={editedName}
              onChange={(e) => setEditedName(e.target.value)}
              disabled={isPending}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent disabled:opacity-50"
            />
          </div>
        )}

        {variant === 'staff' && (
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Tilknyt fag (valgfrit)
            </label>
            <select
              value={courseId}
              onChange={(e) => setCourseId(e.target.value)}
              disabled={isPending}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent disabled:opacity-50"
            >
              <option value="">Intet fag</option>
              {courses?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>
        )}

        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
      <div className="px-6 py-4 border-t border-gray-100 flex justify-end gap-3">
        <button
          onClick={onClose}
          disabled={isPending}
          className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900 disabled:opacity-50"
        >
          Annuller
        </button>
        <button
          onClick={() => {
            if (selectedFile) mutation.mutate({ file: selectedFile })
          }}
          disabled={!selectedFile || isPending}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {isPending ? (progress !== null ? `${progress}%…` : 'Forbereder…') : 'Upload'}
        </button>
      </div>
    </Modal>
  )
}

// ─── File preview modal ───────────────────────────────────────────────────────

interface FilePreviewModalProps {
  fileName: string
  contentType: string
  url: string
  onClose: () => void
}

type SheetData = { name: string; rows: string[][] }[]

function FilePreviewModal({ fileName, contentType, url, onClose }: FilePreviewModalProps) {
  const isImage = contentType.startsWith('image/')
  const isPdf = contentType.includes('pdf')
  const isCsv = contentType.includes('csv') || /\.csv$/i.test(fileName)
  const isExcel =
    !isCsv &&
    (contentType.includes('spreadsheet') ||
      contentType.includes('ms-excel') ||
      /\.(xlsx|xls)$/i.test(fileName))
  const isDocx =
    contentType.includes('wordprocessingml') ||
    contentType.includes('msword') ||
    /\.(docx|doc)$/i.test(fileName)
  const isText =
    !isCsv &&
    !isDocx &&
    (contentType.startsWith('text/') || /\.(txt|log|md|json|xml|yaml|yml)$/i.test(fileName))

  const [textContent, setTextContent] = useState<string | null>(null)
  const [sheetData, setSheetData] = useState<SheetData | null>(null)
  const [activeSheet, setActiveSheet] = useState(0)
  const [docxHtml, setDocxHtml] = useState<string | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    if (!isText) return
    fetch(url)
      .then((r) => r.text())
      .then(setTextContent)
      .catch(() => setTextContent('Kunne ikke indlæse fil.'))
  }, [url, isText])

  useEffect(() => {
    if (!isExcel && !isCsv) return
    fetch(url)
      .then((r) => r.arrayBuffer())
      .then(async (buf) => {
        const XLSX = await import('xlsx')
        const wb = XLSX.read(buf, { type: 'array' })
        const sheets: SheetData = wb.SheetNames.map((name) => {
          const ws = wb.Sheets[name]
          const rows = XLSX.utils.sheet_to_json<string[]>(ws, { header: 1, defval: '' })
          return { name, rows }
        })
        setSheetData(sheets)
        setActiveSheet(0)
      })
      .catch(() => setLoadError('Kunne ikke indlæse filen.'))
  }, [url, isExcel, isCsv])

  useEffect(() => {
    if (!isDocx) return
    fetch(url)
      .then((r) => r.arrayBuffer())
      .then(async (buf) => {
        const mammoth = await import('mammoth')
        const result = await mammoth.convertToHtml({ arrayBuffer: buf })
        setDocxHtml(result.value)
      })
      .catch(() => setLoadError('Kunne ikke indlæse dokumentet.'))
  }, [url, isDocx])

  if (isPdf) {
    window.open(url, '_blank', 'noopener,noreferrer')
    onClose()
    return null
  }

  function handleDownload() {
    fetch(url)
      .then((r) => r.blob())
      .then((blob) => {
        const a = document.createElement('a')
        a.href = URL.createObjectURL(blob)
        a.download = fileName
        a.click()
        URL.revokeObjectURL(a.href)
      })
  }

  const activeSheetRows = sheetData?.[activeSheet]?.rows ?? null

  return (
    <button
      type="button"
      tabIndex={-1}
      aria-label="Luk"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-modal="true"
        className="bg-white rounded-2xl shadow-xl w-[95vw] h-[92vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="px-6 py-4 border-b border-gray-100 flex items-center justify-between gap-4 shrink-0">
          <span className="font-medium text-gray-900 truncate">{fileName}</span>
          <button
            onClick={onClose}
            className="shrink-0 p-1.5 text-gray-400 hover:text-gray-700 rounded-md hover:bg-gray-100 transition-colors"
            title="Luk"
          >
            <CloseIcon />
          </button>
        </div>
        <div className="flex-1 flex flex-col min-h-0 overflow-hidden">
          {isImage && (
            <div className="flex-1 overflow-auto flex items-center justify-center p-4 min-h-0">
              <img
                src={url}
                alt={fileName}
                className="max-h-full max-w-full object-contain mx-auto"
              />
            </div>
          )}
          {isText && (
            <div className="flex-1 overflow-auto p-6 min-h-0">
              {textContent === null ? (
                <p className="text-sm text-gray-400">Indlæser…</p>
              ) : (
                <pre className="text-sm text-gray-800 whitespace-pre-wrap font-mono">
                  {textContent}
                </pre>
              )}
            </div>
          )}
          {(isExcel || isCsv) && (
            <div className="flex-1 flex flex-col min-h-0">
              {loadError ? (
                <div className="flex-1 flex items-center justify-center">
                  <p className="text-sm text-red-500">{loadError}</p>
                </div>
              ) : sheetData === null ? (
                <div className="flex-1 flex items-center justify-center">
                  <p className="text-sm text-gray-400">Indlæser…</p>
                </div>
              ) : (
                <>
                  {sheetData.length > 1 && (
                    <div className="flex gap-1 px-4 pt-3 pb-0 shrink-0 border-b border-gray-100 overflow-x-auto">
                      {sheetData.map((sheet, i) => (
                        <button
                          key={sheet.name}
                          onClick={() => setActiveSheet(i)}
                          className={`px-3 py-1.5 text-xs font-medium rounded-t-md whitespace-nowrap transition-colors ${
                            i === activeSheet
                              ? 'bg-white border border-b-white border-gray-200 text-gray-900 -mb-px'
                              : 'text-gray-500 hover:text-gray-700 hover:bg-gray-50'
                          }`}
                        >
                          {sheet.name}
                        </button>
                      ))}
                    </div>
                  )}
                  <div className="flex-1 overflow-auto">
                    <table className="text-xs border-collapse w-full">
                      <tbody>
                        {(activeSheetRows ?? []).map((row, ri) => (
                          <tr key={ri} className={ri === 0 ? 'bg-gray-50' : 'hover:bg-gray-50/50'}>
                            {row.map((cell, ci) => {
                              const Tag = ri === 0 ? 'th' : 'td'
                              return (
                                <Tag
                                  key={ci}
                                  className="border border-gray-200 px-2 py-1 text-left text-gray-800 whitespace-nowrap max-w-[240px] overflow-hidden text-ellipsis"
                                >
                                  {String(cell)}
                                </Tag>
                              )
                            })}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </>
              )}
            </div>
          )}
          {isDocx && (
            <div className="flex-1 overflow-auto p-6 min-h-0">
              {loadError ? (
                <p className="text-sm text-red-500">{loadError}</p>
              ) : docxHtml === null ? (
                <p className="text-sm text-gray-400">Indlæser…</p>
              ) : (
                <iframe
                  srcDoc={`<!DOCTYPE html><html><head><meta charset="utf-8"><style>body{font-family:sans-serif;font-size:14px;line-height:1.6;color:#1f2937;padding:0;margin:0}p{margin:0 0 0.75em}table{border-collapse:collapse;width:100%}td,th{border:1px solid #e5e7eb;padding:4px 8px}</style></head><body>${docxHtml}</body></html>`}
                  className="flex-1 w-full h-full border-0"
                  sandbox="allow-same-origin"
                  title={fileName}
                />
              )}
            </div>
          )}
          {!isImage && !isText && !isExcel && !isCsv && !isDocx && (
            <div className="flex-1 flex flex-col items-center justify-center gap-3">
              <p className="text-gray-700 font-medium">{fileName}</p>
              <p className="text-sm text-gray-400">{contentType || 'Ukendt filtype'}</p>
              <button
                onClick={handleDownload}
                className="inline-flex items-center gap-2 px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
              >
                Hent fil
              </button>
            </div>
          )}
        </div>
      </div>
    </button>
  )
}

// ─── Create folder modal ──────────────────────────────────────────────────────

interface CreateFolderModalProps {
  variant: Variant
  parentId: string | null
  courses: CourseDto[] | undefined
  defaultCourseId?: string
  onClose: () => void
  onCreated: (folder: FolderDto | BoardFilesControllerBoardFolderDto) => void
}

function CreateFolderModal({
  variant,
  parentId,
  courses,
  defaultCourseId,
  onClose,
  onCreated,
}: CreateFolderModalProps) {
  const [name, setName] = useState('')
  const [courseId, setCourseId] = useState<string>(defaultCourseId ?? '')
  const [error, setError] = useState<string | null>(null)

  const filesQueryKey = variant === 'board' ? getApiV1BoardFilesQueryKey() : getApiV1FilesQueryKey()
  const qc = useQueryClient()

  const mutation = useMutation({
    mutationFn: (args: { name: string; parentId?: string; courseId?: string }) =>
      variant === 'board'
        ? postApiV1BoardFilesFolders({ body: { name: args.name, parentId: args.parentId } }).then(
            (r) => r.data
          )
        : postApiV1FilesFolders({
            body: { name: args.name, parentId: args.parentId, courseId: args.courseId },
          }).then((r) => r.data),
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: filesQueryKey })
      if (data) onCreated(data as FolderDto | BoardFilesControllerBoardFolderDto)
    },
    onError: () => setError('Kunne ikke oprette mappen. Prøv igen.'),
  })

  function submit() {
    const trimmed = name.trim()
    if (!trimmed) {
      setError('Mappenavn må ikke være tomt.')
      return
    }
    setError(null)
    mutation.mutate({
      name: trimmed,
      parentId: parentId || undefined,
      courseId: courseId || undefined,
    })
  }

  return (
    <Modal isOpen onClose={onClose} title="Opret mappe" size="sm">
      <div className="px-6 py-5">
        <label className="block text-sm font-medium text-gray-700 mb-1">Mappenavn *</label>
        <input
          autoFocus
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') submit()
          }}
          placeholder="F.eks. Matematik, Uge 10…"
          disabled={mutation.isPending}
          className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent disabled:opacity-50"
        />
        {error && <p className="text-sm text-red-600 mt-2">{error}</p>}
        {variant === 'staff' && (
          <div className="mt-4">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Tilknyt fag (valgfrit)
            </label>
            <select
              value={courseId}
              onChange={(e) => setCourseId(e.target.value)}
              disabled={mutation.isPending}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent disabled:opacity-50"
            >
              <option value="">Intet fag</option>
              {courses?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>
        )}
      </div>
      <div className="px-6 py-4 border-t border-gray-100 flex justify-end gap-3">
        <button
          onClick={onClose}
          disabled={mutation.isPending}
          className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900 disabled:opacity-50"
        >
          Annuller
        </button>
        <button
          onClick={submit}
          disabled={mutation.isPending}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {mutation.isPending ? 'Opretter…' : 'Opret'}
        </button>
      </div>
    </Modal>
  )
}

// ─── Inline folder rename ─────────────────────────────────────────────────────

interface InlineRenameProps {
  variant: Variant
  folder: FolderRow
  onDone: () => void
}

function InlineRename({ variant, folder, onDone }: InlineRenameProps) {
  const qc = useQueryClient()
  const [value, setValue] = useState(folder.name ?? '')

  const filesQueryKey = variant === 'board' ? getApiV1BoardFilesQueryKey() : getApiV1FilesQueryKey()
  const mutation = useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) =>
      variant === 'board'
        ? patchApiV1BoardFilesFoldersById({ path: { id }, body: { name }, throwOnError: true })
        : patchApiV1FilesFoldersById({ path: { id }, body: { name }, throwOnError: true }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: filesQueryKey })
      onDone()
    },
  })

  function submit() {
    const trimmed = value.trim()
    if (!trimmed || trimmed === folder.name) {
      onDone()
      return
    }
    mutation.mutate({ id: folder.id!, name: trimmed })
  }

  return (
    <input
      autoFocus
      type="text"
      value={value}
      onChange={(e) => setValue(e.target.value)}
      onBlur={submit}
      onKeyDown={(e) => {
        if (e.key === 'Enter') submit()
        if (e.key === 'Escape') onDone()
      }}
      disabled={mutation.isPending}
      className="px-2 py-0.5 border border-brand-400 rounded text-sm font-medium text-gray-900 focus:outline-none focus:ring-2 focus:ring-brand-500 w-40 disabled:opacity-50"
      onClick={(e) => e.stopPropagation()}
    />
  )
}

// ─── Breadcrumb ───────────────────────────────────────────────────────────────

interface BreadcrumbProps {
  rootLabel: string
  trail: { id: string; name: string }[]
  onNavigate: (folderId: string | null) => void
}

function Breadcrumb({ rootLabel, trail, onNavigate }: BreadcrumbProps) {
  return (
    <nav className="flex items-center gap-1 text-sm flex-wrap">
      <button
        onClick={() => onNavigate(null)}
        className="text-brand-600 hover:text-brand-800 font-medium transition-colors"
        data-testid="breadcrumb-root"
      >
        {rootLabel}
      </button>
      {trail.map((crumb) => (
        <span key={crumb.id} className="flex items-center gap-1">
          <span className="text-gray-400">
            <ChevronIcon />
          </span>
          <button
            onClick={() => onNavigate(crumb.id)}
            className="text-brand-600 hover:text-brand-800 font-medium transition-colors"
            data-testid={`breadcrumb-${crumb.id}`}
          >
            {crumb.name}
          </button>
        </span>
      ))}
    </nav>
  )
}

// ─── FileSystemBrowser ────────────────────────────────────────────────────────

export interface FileSystemBrowserProps {
  /** Show full page header with title, upload button, and folder controls. Default: true */
  showHeader?: boolean
  /** 'staff' uses the /files endpoints with course support; 'board' uses /board-files. Default: 'staff' */
  variant?: Variant
}

export function FileSystemBrowser({
  showHeader = true,
  variant = 'staff',
}: FileSystemBrowserProps) {
  const qc = useQueryClient()
  const isAdmin = keycloak.hasRealmRole('admin')

  const [filterCourseId, setFilterCourseId] = useState<string>('')
  const [showUpload, setShowUpload] = useState(false)
  const [showCreateFolder, setShowCreateFolder] = useState(false)
  const [previewFile, setPreviewFile] = useState<{
    fileName: string
    contentType: string
    url: string
  } | null>(null)
  const [renamingFolderId, setRenamingFolderId] = useState<string | null>(null)
  const [folderTrail, setFolderTrail] = useState<{ id: string; name: string }[]>([])

  const currentFolderId = folderTrail.length > 0 ? folderTrail[folderTrail.length - 1].id : null

  const { data: courses } = useQuery({ ...getApiV1CoursesOptions(), enabled: variant === 'staff' })

  const staffQuery = useQuery(
    getApiV1FilesOptions({
      query: {
        ...(filterCourseId ? { courseId: filterCourseId } : {}),
        ...(currentFolderId ? { folderId: currentFolderId } : {}),
      },
    })
  )

  const boardQuery = useQuery(
    getApiV1BoardFilesOptions({
      query: {
        ...(currentFolderId ? { folderId: currentFolderId } : {}),
      },
    })
  )

  const { data, isLoading, isError, refetch } = variant === 'board' ? boardQuery : staffQuery

  const filesQueryKey = variant === 'board' ? getApiV1BoardFilesQueryKey() : getApiV1FilesQueryKey()

  const deleteMutation = useMutation({
    mutationFn: (id: string) =>
      variant === 'board'
        ? deleteApiV1BoardFilesById({ path: { id }, throwOnError: true })
        : deleteApiV1FilesById({ path: { id }, throwOnError: true }),
    onSuccess: () => qc.invalidateQueries({ queryKey: filesQueryKey }),
  })

  const deleteFolderMutation = useMutation({
    mutationFn: (id: string) =>
      variant === 'board'
        ? deleteApiV1BoardFilesFoldersById({ path: { id }, throwOnError: true })
        : deleteApiV1FilesFoldersById({ path: { id }, throwOnError: true }),
    onSuccess: () => qc.invalidateQueries({ queryKey: filesQueryKey }),
  })

  const files: FileRow[] = data?.files ?? []
  const folders: FolderRow[] = data?.folders ?? []
  const isEmpty = !isLoading && files.length === 0 && folders.length === 0

  const rootLabel = variant === 'board' ? 'Bestyrelsesdokumenter' : 'Filer'
  const headerTitle = variant === 'board' ? 'Bestyrelsesdokumenter' : 'Filer'
  const headerDesc =
    variant === 'board'
      ? 'Dokumenter og filer til bestyrelsen'
      : 'Filer og mapper tilknyttet skolen og dens fag'

  function navigateInto(folder: FolderRow) {
    setFolderTrail((prev) => [...prev, { id: folder.id!, name: folder.name! }])
  }

  function navigateTo(folderId: string | null) {
    if (folderId === null) {
      setFolderTrail([])
    } else {
      const idx = folderTrail.findIndex((f) => f.id === folderId)
      if (idx !== -1) setFolderTrail(folderTrail.slice(0, idx + 1))
    }
  }

  function handleFolderCreated() {
    qc.invalidateQueries({ queryKey: filesQueryKey })
    setShowCreateFolder(false)
  }

  function handleDeleteFolder(folder: FolderRow) {
    if (confirm(`Slet mappen "${folder.name ?? 'mappe'}"? Indholdet i mappen slettes også.`)) {
      deleteFolderMutation.mutate(folder.id!)
    }
  }

  return (
    <div className="space-y-6">
      {showHeader && (
        <div className="flex items-center justify-between gap-4 flex-wrap">
          <div className="min-w-0">
            <h1 className="font-display text-2xl font-semibold text-gray-900">{headerTitle}</h1>
            <p className="mt-1 text-sm text-gray-500">{headerDesc}</p>
          </div>
          <div className="flex items-center gap-2 flex-wrap">
            {isAdmin && (
              <button
                onClick={() => setShowCreateFolder(true)}
                className="flex items-center gap-2 px-4 py-2 bg-white border border-gray-300 text-gray-700 text-sm font-medium rounded-lg hover:bg-gray-50 transition-colors"
                data-testid="create-folder-btn"
              >
                <FolderIcon size={16} />
                Ny mappe
              </button>
            )}
            <button
              onClick={() => setShowUpload(true)}
              className="flex items-center gap-2 px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
            >
              <UploadIcon size={16} />
              Upload fil
            </button>
          </div>
        </div>
      )}

      {folderTrail.length > 0 && (
        <Breadcrumb rootLabel={rootLabel} trail={folderTrail} onNavigate={navigateTo} />
      )}

      {variant === 'staff' && (
        <div className="flex items-center gap-2">
          <label className="text-sm text-gray-600">Filtrer efter fag:</label>
          <select
            value={filterCourseId}
            onChange={(e) => setFilterCourseId(e.target.value)}
            className="px-3 py-1.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          >
            <option value="">Alle fag</option>
            {courses?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
          {filterCourseId && (
            <button
              onClick={() => setFilterCourseId('')}
              className="text-xs text-brand-600 hover:text-brand-800"
            >
              Ryd filter
            </button>
          )}
        </div>
      )}

      {isError && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-5 flex items-center justify-between">
          <p className="text-red-700 text-sm font-medium">Kunne ikke hente filer</p>
          <button
            onClick={() => refetch()}
            className="text-sm px-3 py-1.5 bg-red-100 text-red-700 rounded-lg"
          >
            Prøv igen
          </button>
        </div>
      )}

      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-100 bg-gray-50">
              <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide">
                Navn
              </th>
              {variant === 'staff' && (
                <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide hidden md:table-cell">
                  Fag
                </th>
              )}
              <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide hidden sm:table-cell">
                Dato
              </th>
              <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide hidden lg:table-cell">
                Af
              </th>
              <th className="px-5 py-3 text-right text-xs font-semibold text-gray-500 uppercase tracking-wide">
                Handlinger
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-50">
            {isLoading &&
              Array.from({ length: 4 }).map((_, i) => (
                <tr key={i} className="animate-pulse">
                  <td className="px-5 py-3">
                    <div className="h-4 w-40 bg-gray-200 rounded" />
                  </td>
                  {variant === 'staff' && (
                    <td className="px-5 py-3 hidden md:table-cell">
                      <div className="h-4 w-24 bg-gray-100 rounded" />
                    </td>
                  )}
                  <td className="px-5 py-3 hidden sm:table-cell">
                    <div className="h-4 w-20 bg-gray-100 rounded" />
                  </td>
                  <td className="px-5 py-3 hidden lg:table-cell">
                    <div className="h-4 w-24 bg-gray-100 rounded" />
                  </td>
                  <td className="px-5 py-3" />
                </tr>
              ))}

            {folders.map((folder) => (
              <tr
                key={`folder-${folder.id}`}
                className="hover:bg-amber-50 transition-colors cursor-pointer"
                data-testid={`folder-row-${folder.id}`}
                onClick={() => navigateInto(folder)}
              >
                <td className="px-5 py-3">
                  <div className="flex items-center gap-2">
                    <span className="text-amber-400 shrink-0">
                      <FolderIcon size={16} />
                    </span>
                    <div className="min-w-0 flex items-center gap-2">
                      {renamingFolderId === folder.id ? (
                        <InlineRename
                          variant={variant}
                          folder={folder}
                          onDone={() => setRenamingFolderId(null)}
                        />
                      ) : (
                        <span className="font-medium text-gray-900 truncate">{folder.name}</span>
                      )}
                    </div>
                  </div>
                </td>
                {variant === 'staff' && (
                  <td className="px-5 py-3 text-gray-500 hidden md:table-cell">
                    {folder.courseName ?? <span className="text-gray-300">—</span>}
                  </td>
                )}
                <td className="px-5 py-3 text-gray-500 hidden sm:table-cell">
                  {folder.createdAt ? formatDate(folder.createdAt) : '—'}
                </td>
                <td className="px-5 py-3 hidden lg:table-cell" />
                <td className="px-5 py-3 text-right">
                  {isAdmin && (
                    <div className="flex items-center justify-end gap-1">
                      <button
                        onClick={(e) => {
                          e.stopPropagation()
                          setRenamingFolderId(folder.id!)
                        }}
                        className="p-1.5 text-gray-400 hover:text-brand-600 rounded-md hover:bg-brand-50 transition-colors"
                        title="Omdøb mappe"
                        data-testid={`rename-folder-${folder.id}`}
                      >
                        <PencilIcon />
                      </button>
                      <button
                        onClick={(e) => {
                          e.stopPropagation()
                          handleDeleteFolder(folder)
                        }}
                        className="p-1.5 text-gray-400 hover:text-red-600 rounded-md hover:bg-red-50 transition-colors"
                        title="Slet mappe"
                        data-testid={`delete-folder-${folder.id}`}
                      >
                        <TrashIcon />
                      </button>
                    </div>
                  )}
                </td>
              </tr>
            ))}

            {files.map((f) => (
              <tr
                key={f.id}
                className="hover:bg-gray-50 transition-colors"
                data-testid={`file-row-${f.id}`}
              >
                <td className="px-5 py-3">
                  <div className="flex items-center gap-2">
                    <span className="text-gray-400 shrink-0">
                      <FileIcon contentType={f.contentType ?? ''} />
                    </span>
                    <div className="min-w-0">
                      <button
                        onClick={() =>
                          setPreviewFile({
                            fileName: f.fileName ?? '',
                            contentType: f.contentType ?? '',
                            url: f.url ?? '',
                          })
                        }
                        className="font-medium text-gray-900 truncate block text-left hover:text-brand-600 transition-colors"
                      >
                        {f.fileName}
                      </button>
                      <span className="text-xs text-gray-400">{formatBytes(f.sizeBytes ?? 0)}</span>
                    </div>
                  </div>
                </td>
                {variant === 'staff' && (
                  <td className="px-5 py-3 text-gray-500 hidden md:table-cell">
                    {f.courseName ?? <span className="text-gray-300">—</span>}
                  </td>
                )}
                <td className="px-5 py-3 text-gray-500 hidden sm:table-cell">
                  {formatDate(f.uploadedAt ?? new Date().toISOString())}
                </td>
                <td className="px-5 py-3 text-gray-500 hidden lg:table-cell">
                  {f.uploadedBy ?? '—'}
                </td>
                <td className="px-5 py-3 text-right">
                  <div className="flex items-center justify-end gap-1">
                    <button
                      onClick={() =>
                        setPreviewFile({
                          fileName: f.fileName ?? '',
                          contentType: f.contentType ?? '',
                          url: f.url ?? '',
                        })
                      }
                      className="p-1.5 text-gray-400 hover:text-brand-600 rounded-md hover:bg-brand-50 transition-colors"
                      title="Forhåndsvis"
                    >
                      <EyeIcon />
                    </button>
                    <button
                      data-testid={`download-${f.id}`}
                      onClick={() => {
                        if (!f.url || !f.fileName) return
                        const url = f.url
                        const name = f.fileName
                        fetch(url)
                          .then((r) => r.blob())
                          .then((blob) => {
                            const a = document.createElement('a')
                            a.href = URL.createObjectURL(blob)
                            a.download = name
                            a.click()
                            URL.revokeObjectURL(a.href)
                          })
                      }}
                      className="p-1.5 text-gray-400 hover:text-gray-700 rounded-md hover:bg-gray-100 transition-colors"
                      title="Hent fil"
                    >
                      <DownloadIcon />
                    </button>
                    {isAdmin && (
                      <button
                        data-testid={`delete-${f.id}`}
                        onClick={() => {
                          if (f.id && confirm(`Slet filen "${f.fileName ?? 'fil'}"?`)) {
                            deleteMutation.mutate(f.id)
                          }
                        }}
                        className="p-1.5 text-gray-400 hover:text-red-600 rounded-md hover:bg-red-50 transition-colors"
                        title="Slet fil"
                      >
                        <TrashIcon />
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))}

            {isEmpty && (
              <tr>
                <td colSpan={variant === 'staff' ? 5 : 4} className="px-5 py-12 text-center">
                  <p className="text-gray-400 text-sm">
                    {currentFolderId ? 'Mappen er tom' : 'Ingen filer her endnu'}
                  </p>
                  <p className="text-gray-300 text-xs mt-1">
                    {currentFolderId
                      ? 'Upload filer til denne mappe via "Upload fil"'
                      : 'Klik "Upload fil" for at komme i gang'}
                  </p>
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {previewFile && <FilePreviewModal {...previewFile} onClose={() => setPreviewFile(null)} />}

      {showUpload && (
        <UploadModal
          variant={variant}
          courses={courses ?? []}
          currentFolderId={currentFolderId}
          defaultCourseId={filterCourseId || undefined}
          onClose={() => setShowUpload(false)}
          onUploaded={(courseId) => {
            setShowUpload(false)
            if (variant === 'staff') setFilterCourseId(courseId)
          }}
        />
      )}

      {showCreateFolder && (
        <CreateFolderModal
          variant={variant}
          parentId={currentFolderId}
          courses={courses ?? []}
          defaultCourseId={filterCourseId || undefined}
          onClose={() => setShowCreateFolder(false)}
          onCreated={handleFolderCreated}
        />
      )}
    </div>
  )
}
