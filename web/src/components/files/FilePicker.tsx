import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1FilesOptions,
  getApiV1FilesRecentOptions,
} from '../../api/generated/@tanstack/react-query.gen'
import { uploadFile } from '../../api/upload'
import {
  CameraIcon,
  CheckIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  ClockIcon,
  CloseIcon,
  FolderIcon,
  SearchIcon,
  SpinnerIcon,
  StarIcon,
  UploadIcon,
} from '../ui/icons'
import { FileIcon } from './fileIcons'
import { formatBytes, formatDate, MAX_UPLOAD_BYTES, MAX_UPLOAD_LABEL } from './fileHelpers'

const SECTION_ROWS = 3

export interface FilePickerProps {
  title?: string
  /** The lesson's subject: drives "Forslag til" and is set on uploaded files. */
  courseId?: string | null
  courseName?: string | null
  className?: string | null
  selectedFileIds: string[]
  /** Resolves to false if the choice could not be saved. */
  onToggle: (fileId: string, checked: boolean) => Promise<boolean>
  /** Saving the choice failed. */
  error?: string
  onUploadingChange?: (uploading: boolean) => void
  onClose: () => void
}

interface PickerFile {
  id?: string
  fileName?: string | null
  contentType?: string | null
  sizeBytes?: number
  courseName?: string | null
  uploadedAt?: string
}

interface QueueItem {
  key: number
  file: File
  progress: number
  state: 'uploading' | 'done' | 'failed' | 'tooBig'
  /** Set once the file is uploaded, so "Prøv igen" only retries what failed. */
  uploadedId?: string
}

function FileRow({
  file,
  secondLine,
  checked,
  onToggle,
}: {
  file: PickerFile
  secondLine: string
  checked: boolean
  onToggle: (checked: boolean) => void
}) {
  return (
    <li>
      <label
        data-testid={`file-row-${file.id}`}
        className="flex items-center gap-3 px-4 min-h-14 py-2 cursor-pointer hover:bg-gray-50 has-[:checked]:bg-brand-50"
      >
        <span className="shrink-0 text-gray-700">
          <FileIcon contentType={file.contentType ?? ''} />
        </span>
        <span className="flex-1 min-w-0">
          <span className="block truncate text-gray-900">{file.fileName}</span>
          <span className="block text-xs text-gray-700 truncate">{secondLine}</span>
        </span>
        <input
          type="checkbox"
          checked={checked}
          onChange={(e) => onToggle(e.target.checked)}
          className="w-6 h-6 shrink-0 accent-brand-600"
        />
      </label>
    </li>
  )
}

function SectionTitle({
  icon,
  children,
  divided,
}: {
  icon: React.ReactNode
  children: React.ReactNode
  divided?: boolean
}) {
  return (
    <p
      className={`px-4 pt-4 pb-1 text-xs font-bold uppercase tracking-wide text-gray-700 flex items-center gap-1.5 ${
        divided ? 'border-t border-gray-100' : ''
      }`}
    >
      {icon}
      {children}
    </p>
  )
}

function QueueRow({
  item,
  onRetry,
  onRemove,
}: {
  item: QueueItem
  onRetry: () => void
  onRemove: () => void
}) {
  return (
    <li className="rounded-lg border border-gray-200 p-3" data-testid={`file-upload-${item.state}`}>
      <div className="flex items-center gap-2">
        {item.state === 'uploading' && <SpinnerIcon className="w-4 h-4 shrink-0 text-gray-700" />}
        {item.state === 'done' && <CheckIcon className="w-5 h-5 shrink-0 text-brand-700" />}
        <span className="flex-1 min-w-0 truncate font-semibold text-gray-900">
          {item.file.name}
        </span>
        <span className="text-sm text-gray-700">{formatBytes(item.file.size)}</span>
      </div>
      {item.state === 'uploading' && (
        <progress
          value={item.progress}
          max={100}
          aria-label={`Uploader ${item.file.name}`}
          className="mt-2 block w-full h-2 rounded-full overflow-hidden [&::-webkit-progress-bar]:bg-gray-100 [&::-webkit-progress-value]:bg-brand-500 [&::-moz-progress-bar]:bg-brand-500"
        />
      )}
      {item.state === 'done' && <p className="mt-1 text-sm text-gray-700">Uploadet og valgt.</p>}
      {(item.state === 'failed' || item.state === 'tooBig') && (
        <>
          <p className="mt-1 text-sm text-red-800" role="alert">
            {item.state === 'tooBig'
              ? `Den må højst være ${MAX_UPLOAD_LABEL}. Del et link til den i stedet.`
              : item.uploadedId
                ? 'Filen er uploadet, men kom ikke med på lektionen.'
                : 'Forbindelsen blev afbrudt, så filen kom ikke med.'}
          </p>
          <div className="mt-1 flex gap-2">
            {item.state === 'failed' && (
              <button
                type="button"
                onClick={onRetry}
                data-testid="file-upload-retry"
                className="h-11 px-3 rounded-lg border border-gray-300 text-sm font-semibold hover:bg-gray-50"
              >
                Prøv igen
              </button>
            )}
            <button
              type="button"
              onClick={onRemove}
              className="h-11 px-3 rounded-lg text-sm font-semibold text-gray-800 hover:bg-gray-100"
            >
              Fjern
            </button>
          </div>
        </>
      )}
    </li>
  )
}

/**
 * One list to attach files from: search, upload, suggestions for the subject, what you used lately,
 * and all folders. A finished upload is ticked automatically. One failed file doesn't stop the rest.
 */
export function FilePicker({
  title = 'Vedhæft filer',
  courseId,
  courseName,
  className,
  selectedFileIds,
  onToggle,
  error,
  onUploadingChange,
  onClose,
}: FilePickerProps) {
  const qc = useQueryClient()
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [folderTrail, setFolderTrail] = useState<{ id: string; name: string }[]>([])
  const [queue, setQueue] = useState<QueueItem[]>([])
  const [dragging, setDragging] = useState(false)
  const nextKey = useRef(0)

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 200)
    return () => clearTimeout(t)
  }, [search])

  const isSearching = debouncedSearch.length > 0
  const folderId = folderTrail[folderTrail.length - 1]?.id
  const inFolder = folderTrail.length > 0
  const showHome = !isSearching && !inFolder

  const browse = useQuery(
    getApiV1FilesOptions({ query: isSearching ? { search: debouncedSearch } : { folderId } })
  )
  const suggestions = useQuery({
    ...getApiV1FilesOptions({ query: { courseId: courseId ?? undefined } }),
    enabled: showHome && !!courseId,
  })
  const recent = useQuery({ ...getApiV1FilesRecentOptions(), enabled: showHome })

  const suggested = (suggestions.data?.files ?? []).slice(0, SECTION_ROWS)
  const suggestedIds = new Set(suggested.map((f) => f.id))
  const recentFiles = (recent.data ?? [])
    .filter((f) => !suggestedIds.has(f.id))
    .slice(0, SECTION_ROWS)
  const folders = browse.data?.folders ?? []
  const files = browse.data?.files ?? []
  // Cached rows stay on screen if only a background refetch failed.
  const browseFailed = browse.isError && !browse.data
  const nothingFound =
    isSearching && !browse.isLoading && !browseFailed && !files.length && !folders.length

  const uploading = queue.some((i) => i.state === 'uploading')
  useEffect(() => onUploadingChange?.(uploading), [uploading, onUploadingChange])

  const updateItem = (key: number, patch: Partial<QueueItem>) =>
    setQueue((q) => q.map((i) => (i.key === key ? { ...i, ...patch } : i)))

  async function runUpload(key: number, file: File, alreadyUploaded?: string) {
    updateItem(key, { state: 'uploading', progress: alreadyUploaded ? 100 : 0 })
    try {
      let id = alreadyUploaded
      if (!id) {
        const uploaded = await uploadFile({
          file,
          courseId: courseId ?? undefined,
          folderId,
          onProgress: (progress) => updateItem(key, { progress }),
        })
        id = uploaded.id
        updateItem(key, { progress: 100, uploadedId: id })
        void qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1Files' }] })
      }
      updateItem(key, { state: (await onToggle(id, true)) ? 'done' : 'failed' })
    } catch {
      updateItem(key, { state: 'failed' })
    }
  }

  function addFiles(list: FileList | null) {
    for (const file of Array.from(list ?? [])) {
      const key = nextKey.current++
      const tooBig = file.size > MAX_UPLOAD_BYTES
      setQueue((q) => [...q, { key, file, progress: 0, state: tooBig ? 'tooBig' : 'uploading' }])
      if (!tooBig) void runUpload(key, file)
    }
  }

  function isChecked(id?: string) {
    return !!id && selectedFileIds.includes(id)
  }

  const count = selectedFileIds.length
  const countText =
    count === 0 ? 'Ingen filer valgt' : count === 1 ? '1 fil valgt' : `${count} filer valgt`
  const uploadButton =
    'h-11 rounded-lg border border-gray-300 font-semibold text-gray-900 inline-flex items-center justify-center gap-2 cursor-pointer hover:bg-gray-50 has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-brand-500'

  return (
    <section
      data-testid="file-picker"
      aria-label={title}
      className={`flex flex-col min-h-0 flex-1 ${dragging ? 'ring-2 ring-inset ring-brand-500' : ''}`}
      onDragOver={(e) => {
        e.preventDefault()
        setDragging(true)
      }}
      onDragLeave={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node)) setDragging(false)
      }}
      onDrop={(e) => {
        e.preventDefault()
        setDragging(false)
        addFiles(e.dataTransfer.files)
      }}
    >
      <div className="p-4 pb-3 border-b border-gray-100 space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="font-display text-lg font-bold text-gray-900">{title}</h2>
          <button
            type="button"
            aria-label="Luk"
            onClick={onClose}
            disabled={uploading}
            className="w-11 h-11 -mr-2 rounded-full flex items-center justify-center text-gray-800 hover:bg-gray-100 disabled:opacity-40 disabled:cursor-not-allowed"
          >
            <CloseIcon />
          </button>
        </div>
        <label className="relative block">
          <span className="sr-only">Søg i filer</span>
          <SearchIcon className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-500" />
          <input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Søg efter filnavn eller fag"
            data-testid="file-picker-search"
            className="w-full h-11 pl-10 pr-3 rounded-lg border border-gray-300 text-base placeholder:text-gray-500 focus:outline-hidden focus:ring-2 focus:ring-brand-500"
          />
        </label>
        <div className="grid grid-cols-2 sm:grid-cols-1 gap-2">
          <label className={uploadButton}>
            <UploadIcon />
            <span className="sm:hidden">Fra telefonen</span>
            <span className="hidden sm:inline">Upload fra computeren, eller træk filer hertil</span>
            <input
              type="file"
              multiple
              className="sr-only"
              data-testid="file-picker-input"
              onChange={(e) => {
                addFiles(e.target.files)
                e.target.value = ''
              }}
            />
          </label>
          <label className={`${uploadButton} sm:hidden`}>
            <CameraIcon />
            Tag billede
            <input
              type="file"
              accept="image/*"
              capture="environment"
              className="sr-only"
              onChange={(e) => {
                addFiles(e.target.files)
                e.target.value = ''
              }}
            />
          </label>
        </div>
      </div>

      <div className="flex-1 min-h-0 overflow-y-auto">
        {error && (
          <p
            data-testid="file-picker-error"
            className="mx-4 mt-3 p-3 rounded-lg bg-red-50 text-red-800"
            role="alert"
          >
            {error}
          </p>
        )}
        {queue.length > 0 && (
          <ul className="px-4 pt-3 space-y-2" data-testid="file-upload-queue">
            {queue.map((item) => (
              <QueueRow
                key={item.key}
                item={item}
                onRetry={() => void runUpload(item.key, item.file, item.uploadedId)}
                onRemove={() => setQueue((q) => q.filter((i) => i.key !== item.key))}
              />
            ))}
          </ul>
        )}

        {showHome && suggested.length > 0 && (
          <section data-testid="file-suggestions">
            <SectionTitle icon={<StarIcon className="w-4 h-4" />}>
              Forslag til {[courseName, className].filter(Boolean).join(', ')}
            </SectionTitle>
            <ul className="divide-y divide-gray-100">
              {suggested.map((f) => (
                <FileRow
                  key={f.id}
                  file={f}
                  secondLine={`${f.courseName ?? ''} · lagt op ${formatDate(f.uploadedAt ?? '')}`}
                  checked={isChecked(f.id)}
                  onToggle={(checked) => void onToggle(f.id ?? '', checked)}
                />
              ))}
            </ul>
          </section>
        )}

        {showHome && recentFiles.length > 0 && (
          <section data-testid="file-recent">
            <SectionTitle icon={<ClockIcon className="w-4 h-4" />} divided={suggested.length > 0}>
              Senest brugt af dig
            </SectionTitle>
            <ul className="divide-y divide-gray-100">
              {recentFiles.map((f) => (
                <FileRow
                  key={f.id}
                  file={f}
                  secondLine={[f.courseName, `brugt ${formatDate(f.lastUsedAt ?? '')}`]
                    .filter(Boolean)
                    .join(' · ')}
                  checked={isChecked(f.id)}
                  onToggle={(checked) => void onToggle(f.id ?? '', checked)}
                />
              ))}
            </ul>
          </section>
        )}

        <section data-testid="file-all">
          {inFolder && !isSearching ? (
            <button
              type="button"
              onClick={() => setFolderTrail((t) => t.slice(0, -1))}
              className="w-full flex items-center gap-2 px-4 h-14 text-left font-semibold text-gray-900 hover:bg-gray-50 border-b border-gray-100"
            >
              <ChevronLeftIcon className="w-5 h-5 shrink-0" />
              <span className="truncate">{folderTrail[folderTrail.length - 1].name}</span>
            </button>
          ) : (
            <SectionTitle
              icon={<FolderIcon className="w-4 h-4" />}
              divided={showHome && (suggested.length > 0 || recentFiles.length > 0)}
            >
              {isSearching ? 'Søgeresultat' : 'Alle filer'}
            </SectionTitle>
          )}
          {browse.isLoading && (
            <p className="px-4 py-3 text-gray-700" role="status">
              Henter filer …
            </p>
          )}
          <ul className="divide-y divide-gray-100">
            {folders.map((folder) => (
              <li key={folder.id}>
                <button
                  type="button"
                  onClick={() => {
                    setSearch('')
                    setDebouncedSearch('')
                    setFolderTrail((t) => [...t, { id: folder.id ?? '', name: folder.name ?? '' }])
                  }}
                  className="w-full flex items-center gap-3 px-4 h-14 text-left hover:bg-gray-50"
                >
                  <FolderIcon className="w-6 h-6 shrink-0 text-amber-600" />
                  <span className="flex-1 min-w-0 truncate font-semibold text-gray-900">
                    {folder.name}
                  </span>
                  <ChevronRightIcon className="w-5 h-5 shrink-0 text-gray-500" />
                </button>
              </li>
            ))}
            {files.map((f) => (
              <FileRow
                key={f.id}
                file={f}
                secondLine={[f.courseName, formatBytes(f.sizeBytes ?? 0)]
                  .filter(Boolean)
                  .join(' · ')}
                checked={isChecked(f.id)}
                onToggle={(checked) => void onToggle(f.id ?? '', checked)}
              />
            ))}
          </ul>
          {nothingFound && (
            <p className="p-4 text-gray-700">
              Ingen filer hedder sådan. Tjek stavningen, eller upload filen.
            </p>
          )}
          {browseFailed && (
            <div className="p-4 text-red-800" role="alert">
              <p>Filerne kunne ikke hentes.</p>
              <button
                type="button"
                onClick={() => void browse.refetch()}
                className="mt-2 h-11 px-4 rounded-lg border border-red-300 font-semibold hover:bg-red-50"
              >
                Prøv igen
              </button>
            </div>
          )}
          {!isSearching &&
            !browse.isLoading &&
            !browseFailed &&
            !files.length &&
            !folders.length && (
              <p className="p-4 text-gray-700">Her er ingen filer endnu. Upload den første.</p>
            )}
        </section>
      </div>

      <div className="p-4 border-t border-gray-200 flex items-center gap-3">
        <span
          className="flex-1 text-sm font-semibold text-gray-900"
          aria-live="polite"
          data-testid="file-picker-count"
        >
          {countText}
        </span>
        <button
          type="button"
          onClick={onClose}
          disabled={uploading}
          data-testid="file-picker-done"
          className="h-11 px-5 rounded-lg bg-brand-600 text-white font-bold hover:bg-brand-700 disabled:bg-gray-200 disabled:text-gray-700 disabled:cursor-not-allowed"
        >
          {uploading ? 'Uploader …' : 'Færdig'}
        </button>
      </div>
    </section>
  )
}
