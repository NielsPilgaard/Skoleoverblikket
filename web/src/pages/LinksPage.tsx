import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  deleteApiV1ExternalLinksByIdMutation,
  getApiV1ClassesOptions,
  getApiV1ExternalLinksAllOptions,
  getApiV1ExternalLinksAllQueryKey,
  getApiV1ExternalLinksOptions,
  getApiV1ExternalLinksQueryKey,
  postApiV1ExternalLinksMutation,
  putApiV1ExternalLinksByIdMutation,
  putApiV1ExternalLinksOrderMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type { ExternalLinkAdminDto } from '../api/generated/types.gen'
import { useAuth } from '../auth/useAuth'
import { ExternalLinkIcon, ExternalLinkRow, linkHost } from '../components/LinksCard'
import { Modal } from '../components/Modal'
import { usePageTitle } from '../hooks/usePageTitle'
import { problemDetail } from '../lib/problem'

const inputClass =
  'w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent'

const EMPTY_TEXT = 'Skolen har ikke tilføjet nogen links endnu.'

function useInvalidateLinks() {
  const qc = useQueryClient()
  // Returns the refetch so a mutation stays pending until the list is fresh. Otherwise the reorder
  // arrows re-enable on the old order and a quick second click can undo the first move.
  return () =>
    Promise.all([
      qc.invalidateQueries({ queryKey: getApiV1ExternalLinksAllQueryKey() }),
      qc.invalidateQueries({ queryKey: getApiV1ExternalLinksQueryKey() }),
    ])
}

function LinkModal({ initial, onClose }: { initial?: ExternalLinkAdminDto; onClose: () => void }) {
  const invalidate = useInvalidateLinks()
  const [title, setTitle] = useState(initial?.title ?? '')
  const [url, setUrl] = useState(initial?.url ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [parents, setParents] = useState(initial?.visibleToParents ?? true)
  const [staff, setStaff] = useState(initial?.visibleToStaff ?? false)
  const [board, setBoard] = useState(initial?.visibleToBoard ?? false)
  const [restrict, setRestrict] = useState(initial?.restrictToClasses ?? false)
  const [classIds, setClassIds] = useState<string[]>(initial?.classes?.map((c) => c.id) ?? [])

  const { data: classes } = useQuery({ ...getApiV1ClassesOptions(), enabled: parents })

  const onSuccess = () => {
    invalidate()
    onClose()
  }
  const create = useMutation({ ...postApiV1ExternalLinksMutation(), onSuccess })
  const update = useMutation({ ...putApiV1ExternalLinksByIdMutation(), onSuccess })
  const isPending = create.isPending || update.isPending
  const error = create.error ?? update.error

  const noAudience = !parents && !staff && !board
  const noClasses = parents && restrict && classIds.length === 0
  const canSave = title.trim() !== '' && url.trim() !== '' && !noAudience && !noClasses

  function toggleClass(id: string) {
    setClassIds((prev) => (prev.includes(id) ? prev.filter((c) => c !== id) : [...prev, id]))
  }

  function handleSave() {
    if (!canSave || isPending) return
    const body = {
      title: title.trim(),
      url: url.trim(),
      description: description.trim() || null,
      visibleToParents: parents,
      visibleToStaff: staff,
      visibleToBoard: board,
      classIds: parents && restrict ? classIds : [],
    }
    if (initial) {
      update.mutate({ path: { id: initial.id }, body })
    } else {
      create.mutate({ body })
    }
  }

  return (
    <Modal isOpen onClose={onClose} title={initial ? 'Rediger link' : 'Tilføj link'}>
      <div className="px-6 py-5 space-y-4 max-h-[70vh] overflow-y-auto">
        <div>
          <label htmlFor="link-title" className="block text-sm font-medium text-gray-700 mb-1">
            Titel *
          </label>
          <input
            id="link-title"
            data-testid="link-title-input"
            value={title}
            maxLength={100}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="fx Aula"
            className={inputClass}
          />
        </div>
        <div>
          <label htmlFor="link-url" className="block text-sm font-medium text-gray-700 mb-1">
            Webadresse *
          </label>
          <input
            id="link-url"
            data-testid="link-url-input"
            value={url}
            maxLength={2000}
            inputMode="url"
            autoCapitalize="none"
            onChange={(e) => setUrl(e.target.value)}
            placeholder="fx aula.dk"
            className={inputClass}
          />
        </div>
        <div>
          <label
            htmlFor="link-description"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Beskrivelse
          </label>
          <input
            id="link-description"
            data-testid="link-description-input"
            value={description}
            maxLength={300}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="fx Meld fravær før kl. 8"
            className={inputClass}
          />
        </div>

        <fieldset>
          <legend className="block text-sm font-medium text-gray-700 mb-2">
            Hvem kan se linket?
          </legend>
          <div className="space-y-2">
            {(
              [
                ['Forældre', parents, setParents, 'link-audience-parents'],
                ['Medarbejdere', staff, setStaff, 'link-audience-staff'],
                ['Bestyrelse', board, setBoard, 'link-audience-board'],
              ] as const
            ).map(([label, checked, setChecked, testId]) => (
              <label key={label} className="flex items-center gap-2 text-sm text-gray-700">
                <input
                  type="checkbox"
                  data-testid={testId}
                  checked={checked}
                  onChange={(e) => setChecked(e.target.checked)}
                  className="w-4 h-4 rounded border-gray-300 text-brand-600 focus:ring-brand-500"
                />
                {label}
              </label>
            ))}
          </div>
          {noAudience && <p className="mt-2 text-sm text-amber-700">Vælg mindst én.</p>}
        </fieldset>

        {parents && (
          <fieldset>
            <legend className="block text-sm font-medium text-gray-700 mb-2">
              Hvilke forældre?
            </legend>
            <div className="space-y-2">
              <label className="flex items-center gap-2 text-sm text-gray-700">
                <input
                  type="radio"
                  name="link-classes"
                  data-testid="link-classes-all"
                  checked={!restrict}
                  onChange={() => setRestrict(false)}
                  className="w-4 h-4 border-gray-300 text-brand-600 focus:ring-brand-500"
                />
                Hele skolen
              </label>
              <label className="flex items-center gap-2 text-sm text-gray-700">
                <input
                  type="radio"
                  name="link-classes"
                  data-testid="link-classes-some"
                  checked={restrict}
                  onChange={() => setRestrict(true)}
                  className="w-4 h-4 border-gray-300 text-brand-600 focus:ring-brand-500"
                />
                Kun forældre i bestemte klasser
              </label>
            </div>
            {restrict && (
              <div className="mt-3 grid grid-cols-2 sm:grid-cols-3 gap-2">
                {classes?.map((c) => (
                  <label
                    key={c.id}
                    className="flex items-center gap-2 px-3 py-2 rounded-lg border border-gray-200 text-sm text-gray-700"
                  >
                    <input
                      type="checkbox"
                      data-testid={`link-class-${c.id}`}
                      checked={classIds.includes(c.id!)}
                      onChange={() => toggleClass(c.id!)}
                      className="w-4 h-4 shrink-0 rounded border-gray-300 text-brand-600 focus:ring-brand-500"
                    />
                    <span className="min-w-0 break-words">{c.name}</span>
                  </label>
                ))}
              </div>
            )}
            {noClasses && <p className="mt-2 text-sm text-amber-700">Vælg mindst én klasse.</p>}
          </fieldset>
        )}

        {error && (
          <p className="text-sm text-red-600" data-testid="link-save-error">
            {problemDetail(error) ?? 'Der opstod en fejl. Prøv igen.'}
          </p>
        )}
      </div>
      <div className="px-6 py-4 border-t border-gray-100 flex justify-end gap-3">
        <button
          type="button"
          onClick={onClose}
          className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900"
        >
          Annuller
        </button>
        <button
          type="button"
          data-testid="link-save"
          onClick={handleSave}
          disabled={!canSave || isPending}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {isPending ? 'Gemmer...' : 'Gem'}
        </button>
      </div>
    </Modal>
  )
}

function Chip({ children, warning = false }: { children: React.ReactNode; warning?: boolean }) {
  return (
    <span
      className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${
        warning ? 'bg-amber-100 text-amber-800' : 'bg-gray-100 text-gray-600'
      }`}
    >
      {children}
    </span>
  )
}

function AudienceChips({ link }: { link: ExternalLinkAdminDto }) {
  const classes = link.classes ?? []
  return (
    <span className="flex flex-wrap gap-1.5 mt-1.5">
      {link.visibleToParents && (
        <Chip>
          Forældre
          {link.restrictToClasses && classes.length > 0
            ? `: ${classes.map((c) => c.name).join(', ')}`
            : ''}
        </Chip>
      )}
      {link.visibleToParents && link.restrictToClasses && classes.length === 0 && (
        <Chip warning>Ingen klasser</Chip>
      )}
      {link.visibleToStaff && <Chip>Medarbejdere</Chip>}
      {link.visibleToBoard && <Chip>Bestyrelse</Chip>}
    </span>
  )
}

const iconButton =
  'p-2 text-gray-400 rounded-md transition-colors disabled:opacity-30 disabled:cursor-not-allowed'

function AdminLinks() {
  const invalidate = useInvalidateLinks()
  const [editing, setEditing] = useState<ExternalLinkAdminDto | 'new' | null>(null)
  const { data: links, isLoading, isError, refetch } = useQuery(getApiV1ExternalLinksAllOptions())

  const remove = useMutation({ ...deleteApiV1ExternalLinksByIdMutation(), onSettled: invalidate })
  const reorder = useMutation({
    ...putApiV1ExternalLinksOrderMutation(),
    onSettled: invalidate,
  })

  function move(index: number, delta: -1 | 1) {
    if (!links) return
    const ids = links.map((l) => l.id)
    const target = index + delta
    ;[ids[index], ids[target]] = [ids[target], ids[index]]
    reorder.mutate({ body: { orderedIds: ids } })
  }

  return (
    <>
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="font-display text-2xl font-semibold text-gray-900">Links</h1>
          <p className="mt-1 text-sm text-gray-500">
            Links til fx Aula, fraværsblanket eller klassens Facebook-gruppe
          </p>
        </div>
        <button
          type="button"
          data-testid="link-add"
          onClick={() => setEditing('new')}
          className="shrink-0 px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
        >
          Tilføj link
        </button>
      </div>

      {isError && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-5 flex items-center justify-between">
          <p className="text-red-700 text-sm font-medium">Kunne ikke hente links</p>
          <button
            onClick={() => refetch()}
            className="text-sm px-3 py-1.5 bg-red-100 text-red-700 rounded-lg"
          >
            Prøv igen
          </button>
        </div>
      )}
      {remove.isError && (
        <p className="text-sm text-red-600">
          {problemDetail(remove.error) ?? 'Linket kunne ikke slettes. Prøv igen.'}
        </p>
      )}
      {reorder.isError && (
        <p className="text-sm text-red-600">
          {problemDetail(reorder.error) ?? 'Rækkefølgen kunne ikke gemmes. Prøv igen.'}
        </p>
      )}

      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        {isLoading ? (
          <div className="px-5 py-8 animate-pulse">
            <div className="h-4 w-40 bg-gray-200 rounded" />
          </div>
        ) : !links || links.length === 0 ? (
          <p className="px-5 py-10 text-sm text-gray-400 text-center" data-testid="links-empty">
            {EMPTY_TEXT}
          </p>
        ) : (
          <ul className="divide-y divide-gray-50" data-testid="links-list">
            {links.map((link, index) => (
              <li key={link.id} className="flex items-start gap-3 px-5 py-3" data-testid="link-row">
                <span className="flex items-center justify-center w-8 h-8 rounded-lg bg-brand-50 text-brand-600 shrink-0">
                  <ExternalLinkIcon />
                </span>
                <span className="min-w-0 flex-1">
                  <a
                    href={link.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="block text-sm font-medium text-gray-800 hover:text-brand-700"
                  >
                    {link.title}
                  </a>
                  {link.description && (
                    <span className="block text-sm text-gray-500">{link.description}</span>
                  )}
                  <span className="block text-xs text-gray-400 truncate">{linkHost(link.url)}</span>
                  <AudienceChips link={link} />
                </span>
                <span className="flex flex-wrap justify-end items-center gap-0.5 shrink-0">
                  <button
                    type="button"
                    data-testid="link-move-up"
                    aria-label="Flyt op"
                    title="Flyt op"
                    disabled={index === 0 || reorder.isPending}
                    onClick={() => move(index, -1)}
                    className={`${iconButton} hover:text-gray-700 hover:bg-gray-100`}
                  >
                    ↑
                  </button>
                  <button
                    type="button"
                    data-testid="link-move-down"
                    aria-label="Flyt ned"
                    title="Flyt ned"
                    disabled={index === links.length - 1 || reorder.isPending}
                    onClick={() => move(index, 1)}
                    className={`${iconButton} hover:text-gray-700 hover:bg-gray-100`}
                  >
                    ↓
                  </button>
                  <button
                    type="button"
                    data-testid="link-edit"
                    onClick={() => setEditing(link)}
                    className="px-2 py-1.5 text-sm text-gray-600 rounded-md hover:bg-gray-100 hover:text-gray-900"
                  >
                    Rediger
                  </button>
                  <button
                    type="button"
                    data-testid="link-delete"
                    onClick={() => {
                      if (confirm(`Slet linket "${link.title}"?`))
                        remove.mutate({ path: { id: link.id } })
                    }}
                    className="px-2 py-1.5 text-sm text-red-600 rounded-md hover:bg-red-50"
                  >
                    Slet
                  </button>
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>

      {editing && (
        <LinkModal
          initial={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
        />
      )}
    </>
  )
}

function ReaderLinks() {
  const { data: links, isLoading, isError, refetch } = useQuery(getApiV1ExternalLinksOptions())

  return (
    <>
      <div>
        <h1 className="font-display text-2xl font-semibold text-gray-900">Links</h1>
        <p className="mt-1 text-sm text-gray-500">Skolens links samlet ét sted</p>
      </div>

      {isError && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-5 flex items-center justify-between">
          <p className="text-red-700 text-sm font-medium">Kunne ikke hente links</p>
          <button
            onClick={() => refetch()}
            className="text-sm px-3 py-1.5 bg-red-100 text-red-700 rounded-lg"
          >
            Prøv igen
          </button>
        </div>
      )}

      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        {isLoading ? (
          <div className="px-5 py-8 animate-pulse">
            <div className="h-4 w-40 bg-gray-200 rounded" />
          </div>
        ) : !links || links.length === 0 ? (
          <p className="px-5 py-10 text-sm text-gray-400 text-center" data-testid="links-empty">
            {EMPTY_TEXT}
          </p>
        ) : (
          <ul className="divide-y divide-gray-50" data-testid="links-list">
            {links.map((link) => (
              <li key={link.id}>
                <ExternalLinkRow link={link} />
              </li>
            ))}
          </ul>
        )}
      </div>
    </>
  )
}

/** Links for everyone. Admins (acting as staff) manage the list on the same page. */
export default function LinksPage() {
  usePageTitle('Links')
  const { isAdmin } = useAuth()

  return (
    <div className="p-4 sm:p-6 lg:p-8 max-w-3xl mx-auto space-y-6">
      {isAdmin ? <AdminLinks /> : <ReaderLinks />}
    </div>
  )
}
