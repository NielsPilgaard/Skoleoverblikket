import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { getApiV1FilesOptions } from '../../api/generated/@tanstack/react-query.gen'
import { FileIcon, FolderIcon } from './fileIcons'
import { formatBytes } from './fileHelpers'

export interface FilePickerProps {
  selectedFileIds: string[]
  onToggle: (fileId: string, checked: boolean) => void
  disabled?: boolean
}

/**
 * Compact file picker for use inside modals (e.g. ugeplan slot editor).
 * Supports folder navigation with breadcrumb. Search mode flattens across all files.
 */
export function FilePicker({ selectedFileIds, onToggle, disabled = false }: FilePickerProps) {
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [folderId, setFolderId] = useState<string | undefined>(undefined)
  const [folderTrail, setFolderTrail] = useState<{ id: string; name: string }[]>([])

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 200)
    return () => clearTimeout(t)
  }, [search])

  const isSearching = debouncedSearch.length > 0

  const { data, isLoading } = useQuery(
    getApiV1FilesOptions({
      query: isSearching ? { search: debouncedSearch } : { folderId },
    })
  )
  const allFiles = data?.files ?? []
  const folders = data?.folders ?? []

  function enterFolder(id: string, name: string) {
    setSearch('')
    setDebouncedSearch('')
    setFolderTrail((prev) => [...prev, { id, name }])
    setFolderId(id)
  }

  function navigateToTrailIndex(index: number) {
    if (index < 0) {
      setFolderTrail([])
      setFolderId(undefined)
    } else {
      const next = folderTrail.slice(0, index + 1)
      setFolderTrail(next)
      setFolderId(next[next.length - 1].id)
    }
  }

  const showEmptyState = !isLoading && !search && allFiles.length === 0 && folders.length === 0

  return (
    <div className="space-y-2">
      <input
        type="search"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Søg efter filnavn eller fag…"
        className="w-full px-3 py-1.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
      />

      {showEmptyState && (
        <p className="text-xs text-gray-400 py-2">
          Ingen filer tilgængelige — gå til{' '}
          <a
            href="/filer"
            target="_blank"
            rel="noopener"
            className="text-brand-600 hover:text-brand-800 underline"
          >
            Filer
          </a>{' '}
          for at uploade.
        </p>
      )}

      {!isSearching && folderTrail.length > 0 && (
        <nav className="flex items-center gap-1 text-xs text-gray-500 flex-wrap">
          <button
            type="button"
            onClick={() => navigateToTrailIndex(-1)}
            className="hover:text-brand-600 hover:underline"
          >
            Alle filer
          </button>
          {folderTrail.map((crumb, i) => (
            <span key={crumb.id} className="flex items-center gap-1">
              <span className="text-gray-300">›</span>
              {i < folderTrail.length - 1 ? (
                <button
                  type="button"
                  onClick={() => navigateToTrailIndex(i)}
                  className="hover:text-brand-600 hover:underline"
                >
                  {crumb.name}
                </button>
              ) : (
                <span className="text-gray-700 font-medium">{crumb.name}</span>
              )}
            </span>
          ))}
        </nav>
      )}

      {isLoading ? (
        <div className="space-y-1 border border-gray-200 rounded-lg p-2">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="animate-pulse h-8 bg-gray-100 rounded" />
          ))}
        </div>
      ) : (
        <>
          {isSearching && allFiles.length === 0 && folders.length === 0 && (
            <p className="text-xs text-gray-400 py-2 text-center">
              Ingen filer eller mapper matcher søgningen.
            </p>
          )}

          {(folders.length > 0 || allFiles.length > 0) && (
            <div className="max-h-48 overflow-y-auto border border-gray-200 rounded-lg divide-y divide-gray-50">
              {folders.map((folder) => (
                <button
                  key={folder.id}
                  type="button"
                  onClick={() => enterFolder(folder.id!, folder.name!)}
                  className="flex items-center gap-2 px-3 py-2 w-full text-left hover:bg-gray-50 text-sm text-gray-700"
                >
                  <span className="text-yellow-500 shrink-0">
                    <FolderIcon size={16} />
                  </span>
                  <span className="truncate flex-1">{folder.name}</span>
                  <span className="text-gray-300 shrink-0">›</span>
                </button>
              ))}

              {allFiles.map((f) => {
                const isChecked = selectedFileIds.includes(f.id!)
                return (
                  <label
                    key={f.id}
                    className="flex items-center gap-2 px-3 py-2 hover:bg-gray-50 cursor-pointer"
                  >
                    <input
                      type="checkbox"
                      checked={isChecked}
                      disabled={disabled}
                      onChange={(e) => onToggle(f.id!, e.target.checked)}
                      className="rounded border-gray-300 text-brand-600 focus:ring-brand-500 shrink-0"
                    />
                    <span className="text-gray-400 shrink-0">
                      <FileIcon contentType={f.contentType ?? ''} />
                    </span>
                    <span className="text-sm text-gray-800 truncate flex-1">{f.fileName}</span>
                    {f.courseName && (
                      <span className="text-xs text-gray-400 shrink-0 hidden sm:block">
                        {f.courseName}
                      </span>
                    )}
                    <span className="text-xs text-gray-400 shrink-0">
                      {formatBytes(f.sizeBytes ?? 0)}
                    </span>
                  </label>
                )
              })}
            </div>
          )}
        </>
      )}
    </div>
  )
}
