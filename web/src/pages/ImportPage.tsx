import { useState, useMemo, useCallback } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { usePageTitle } from '../hooks/usePageTitle'
import PasteGrid, { type ColumnDef, type GridRow } from '../components/PasteGrid'
import ModuleInactiveNotice from '../components/ModuleInactiveNotice'
import { useSubscription } from '../hooks/useSubscription'
import { pluralize } from '../lib/pluralize'
import { problemDetail } from '../lib/problem'
import {
  getApiV1BoardMembers,
  getApiV1Staff,
  postApiV1ImportsBoardMembers,
  postApiV1ImportsClasses,
  postApiV1ImportsRooms,
  postApiV1ImportsStaff,
  postApiV1ImportsStudentsAndParents,
  postApiV1BoardMembersInvite,
  postApiV1ParentInvitationsByParentIdResend,
  postApiV1StaffInvitationsInviteByStaffId,
} from '../api/generated/sdk.gen'
import type {
  ImportsControllerImportBoardMembersResponse,
  ImportsControllerImportClassesResponse,
  ImportsControllerImportRoomsResponse,
  ImportsControllerImportStaffResponse,
  ImportsControllerImportStudentsAndParentsResponse,
  ImportsControllerImportWarning,
} from '../api/generated/types.gen'

interface InvitableRecord {
  id: string
  name: string
  email: string
}

// ── Column definitions ────────────────────────────────────────────────────────

const STUDENT_COLUMNS: ColumnDef[] = [
  { key: 'className', label: 'Klasse', required: true, placeholder: '2A' },
  { key: 'studentName', label: 'Elevnavn', required: true, placeholder: 'Fuldt navn' },
  { key: 'parent1Name', label: 'Forælder 1 navn', placeholder: 'Fuldt navn' },
  {
    key: 'parent1Email',
    label: 'Forælder 1 e-mail',
    type: 'email',
    placeholder: 'navn@eksempel.dk',
  },
  { key: 'parent1Phone', label: 'Forælder 1 tlf.', type: 'tel', placeholder: '+45 12 34 56 78' },
  { key: 'parent1Address', label: 'Forælder 1 adresse', placeholder: 'Gade 1' },
  { key: 'parent1PostalCode', label: 'Post nr.', placeholder: '1234' },
  { key: 'parent1City', label: 'By', placeholder: 'By' },
  { key: 'parent2Name', label: 'Forælder 2 navn', placeholder: 'Fuldt navn' },
  {
    key: 'parent2Email',
    label: 'Forælder 2 e-mail',
    type: 'email',
    placeholder: 'navn@eksempel.dk',
  },
  { key: 'parent2Phone', label: 'Forælder 2 tlf.', type: 'tel', placeholder: '+45 12 34 56 78' },
  { key: 'parent2Address', label: 'Forælder 2 adresse', placeholder: 'Gade 1' },
  { key: 'parent2PostalCode', label: 'Post nr.', placeholder: '1234' },
  { key: 'parent2City', label: 'By', placeholder: 'By' },
]

const STAFF_COLUMNS: ColumnDef[] = [
  { key: 'name', label: 'Navn', required: true, placeholder: 'Fuldt navn' },
  { key: 'email', label: 'E-mail', type: 'email', placeholder: 'navn@skole.dk' },
  { key: 'phone', label: 'Telefon', type: 'tel', placeholder: '+45 12 34 56 78' },
  { key: 'role', label: 'Rolle', placeholder: 'Lærer / Pædagog / Vikar' },
  { key: 'administrator', label: 'Administrator', placeholder: 'ja / nej' },
]

const CLASS_COLUMNS: ColumnDef[] = [
  { key: 'name', label: 'Klasse', required: true, placeholder: '2A' },
  { key: 'gradeLevel', label: 'Klassetrin', type: 'number', placeholder: '2' },
  { key: 'description', label: 'Beskrivelse', placeholder: 'Valgfri beskrivelse' },
]

const ROOM_COLUMNS: ColumnDef[] = [
  { key: 'name', label: 'Navn', required: true, placeholder: 'Lokale 12' },
  { key: 'description', label: 'Beskrivelse', placeholder: 'Valgfri beskrivelse' },
  { key: 'capacity', label: 'Kapacitet', type: 'number', placeholder: '30' },
]

const BOARD_COLUMNS: ColumnDef[] = [
  { key: 'name', label: 'Navn', required: true, placeholder: 'Fuldt navn' },
  { key: 'email', label: 'E-mail', required: true, type: 'email', placeholder: 'navn@eksempel.dk' },
  { key: 'canAccessTeacherData', label: 'Adgang til lærerdata', placeholder: 'ja / nej' },
]

// ── Helpers ────────────────────────────────────────────────────────────────────

function rowToParent(row: GridRow, prefix: '1' | '2') {
  const name = row[`parent${prefix}Name`]?.trim() || undefined
  const email = row[`parent${prefix}Email`]?.trim() || undefined
  const phone = row[`parent${prefix}Phone`]?.trim() || undefined
  const address = row[`parent${prefix}Address`]?.trim() || undefined
  const postalCode = row[`parent${prefix}PostalCode`]?.trim() || undefined
  const city = row[`parent${prefix}City`]?.trim() || undefined
  if (!name && !email && !phone && !address && !postalCode && !city) return undefined
  return { name, email, phone, address, postalCode, city }
}

function countStudentRows(rows: GridRow[]) {
  return rows.filter((r) => r.className?.trim() && r.studentName?.trim()).length
}

function countStaffRows(rows: GridRow[]) {
  return rows.filter((r) => r.name?.trim()).length
}

function countRoomRows(rows: GridRow[]) {
  return rows.filter((r) => r.name?.trim()).length
}

function countClassRows(rows: GridRow[]) {
  return rows.filter((r) => r.name?.trim()).length
}

function countBoardRows(rows: GridRow[]) {
  return rows.filter((r) => r.name?.trim() && r.email?.trim()).length
}

// Calls pass throwOnError, so a failed request throws its ProblemDetails body.
function errorMessage(e: unknown) {
  return problemDetail(e) ?? (e instanceof Error ? e.message : 'Der opstod en fejl')
}

// ── Shared components ──────────────────────────────────────────────────────────

function WarningList({ warnings }: { warnings: ImportsControllerImportWarning[] }) {
  if (!warnings.length) return null
  return (
    <div className="mt-3 bg-amber-50 border border-amber-200 rounded-lg p-3 space-y-1">
      <p className="text-xs font-semibold text-amber-700 uppercase tracking-wide mb-1">Advarsler</p>
      {warnings.map((w, i) => (
        <p key={i} className="text-sm text-amber-800">
          <span className="font-medium">Række {w.row}:</span> {w.message}
        </p>
      ))}
    </div>
  )
}

interface InvitationStepProps {
  records: InvitableRecord[]
  summaryLabel: (count: number) => string
  confirmMessage: (count: number) => string
  onSendInvite: (record: InvitableRecord) => Promise<void>
  onDone: () => void
}

function InvitationStep({
  records,
  summaryLabel,
  confirmMessage,
  onSendInvite,
  onDone,
}: InvitationStepProps) {
  const [filter, setFilter] = useState('')
  const [selected, setSelected] = useState<Set<string>>(() => new Set(records.map((r) => r.id)))
  const [showConfirm, setShowConfirm] = useState(false)
  const [isSending, setIsSending] = useState(false)
  const [sentIds, setSentIds] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)

  const filtered = useMemo(() => {
    const q = filter.trim().toLowerCase()
    return records.filter(
      (r) =>
        !sentIds.has(r.id) &&
        (!q || r.name.toLowerCase().includes(q) || r.email.toLowerCase().includes(q))
    )
  }, [records, filter, sentIds])

  const selectedCount = useMemo(
    () => filtered.filter((r) => selected.has(r.id)).length,
    [filtered, selected]
  )

  const allFilteredSelected = filtered.length > 0 && filtered.every((r) => selected.has(r.id))

  function toggleAll() {
    if (allFilteredSelected) {
      setSelected((prev) => {
        const next = new Set(prev)
        for (const r of filtered) next.delete(r.id)
        return next
      })
    } else {
      setSelected((prev) => {
        const next = new Set(prev)
        for (const r of filtered) next.add(r.id)
        return next
      })
    }
  }

  function toggleOne(id: string) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  async function handleSend() {
    setShowConfirm(false)
    setIsSending(true)
    setError(null)
    const toSend = filtered.filter((r) => selected.has(r.id))
    const newlySent = new Set<string>()
    try {
      for (const record of toSend) {
        await onSendInvite(record)
        newlySent.add(record.id)
      }
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setSentIds((prev) => new Set([...prev, ...newlySent]))
      setSelected((prev) => {
        const next = new Set(prev)
        for (const id of newlySent) next.delete(id)
        return next
      })
      setIsSending(false)
    }
  }

  const remaining = records.filter((r) => !sentIds.has(r.id))

  if (!remaining.length) {
    return (
      <div className="bg-green-50 border border-green-200 rounded-lg px-4 py-3 text-sm text-green-800">
        Alle invitationer er sendt.{' '}
        <button type="button" onClick={onDone} className="underline font-medium">
          Importér mere
        </button>
      </div>
    )
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <p className="text-sm text-gray-700 font-medium">{summaryLabel(selectedCount)}</p>
        <div className="flex gap-2">
          <button
            type="button"
            onClick={onDone}
            className="px-3 py-1.5 text-sm text-gray-600 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
          >
            Spring over
          </button>
          <button
            type="button"
            onClick={() => setShowConfirm(true)}
            disabled={selectedCount === 0 || isSending}
            className="px-4 py-1.5 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {isSending ? 'Sender…' : 'Send invitationer'}
          </button>
        </div>
      </div>

      <input
        type="search"
        placeholder="Søg på navn eller e-mail…"
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
        className="w-full px-3 py-2 text-sm border border-gray-300 rounded-lg focus:outline-none focus:border-brand-400"
      />

      <div className="border border-gray-200 rounded-lg overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-gray-50 border-b border-gray-200">
              <th className="px-3 py-2 w-8">
                <input
                  type="checkbox"
                  checked={allFilteredSelected}
                  onChange={toggleAll}
                  aria-label="Vælg alle"
                />
              </th>
              <th className="px-3 py-2 text-left text-xs font-semibold text-gray-600 uppercase tracking-wide">
                Navn
              </th>
              <th className="px-3 py-2 text-left text-xs font-semibold text-gray-600 uppercase tracking-wide">
                E-mail
              </th>
            </tr>
          </thead>
          <tbody>
            {filtered.map((r) => (
              <tr
                key={r.id}
                className="border-b border-gray-100 last:border-0 hover:bg-gray-50 cursor-pointer"
                onClick={() => toggleOne(r.id)}
              >
                <td className="px-3 py-2 w-8">
                  <input
                    type="checkbox"
                    checked={selected.has(r.id)}
                    onChange={() => toggleOne(r.id)}
                    onClick={(e) => e.stopPropagation()}
                  />
                </td>
                <td className="px-3 py-2 text-gray-900">{r.name}</td>
                <td className="px-3 py-2 text-gray-500">{r.email}</td>
              </tr>
            ))}
            {filtered.length === 0 && (
              <tr>
                <td colSpan={3} className="px-3 py-4 text-center text-sm text-gray-400">
                  Ingen resultater
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800">
          {error}
        </div>
      )}

      {showConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-xl shadow-xl p-6 max-w-sm w-full mx-4 space-y-4">
            <p className="text-sm text-gray-800">{confirmMessage(selectedCount)}</p>
            <div className="flex gap-3 justify-end">
              <button
                type="button"
                onClick={() => setShowConfirm(false)}
                className="px-4 py-2 text-sm border border-gray-300 rounded-lg hover:bg-gray-50"
              >
                Annuller
              </button>
              <button
                type="button"
                onClick={handleSend}
                className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700"
              >
                Bekræft
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

// ── Tab 1: Elever & forældre ──────────────────────────────────────────────────

function StudentsTab() {
  const [rows, setRows] = useState<GridRow[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [result, setResult] = useState<ImportsControllerImportStudentsAndParentsResponse | null>(
    null
  )
  const [error, setError] = useState<string | null>(null)
  const [uninvitedParents, setUninvitedParents] = useState<InvitableRecord[] | null>(null)

  const validCount = useMemo(() => countStudentRows(rows), [rows])

  async function handleImport() {
    const payload = rows
      .filter((r) => r.className?.trim() && r.studentName?.trim())
      .map((r) => ({
        className: r.className.trim(),
        studentName: r.studentName.trim(),
        parent1: rowToParent(r, '1'),
        parent2: rowToParent(r, '2'),
      }))

    if (!payload.length) return
    setIsLoading(true)
    setError(null)
    setResult(null)
    setUninvitedParents(null)
    try {
      const res = await postApiV1ImportsStudentsAndParents({
        body: { rows: payload },
        throwOnError: true,
      })
      setResult(res.data!)

      const uninvited = (res.data!.uninvitedParents ?? []).map((p) => ({
        id: p.id!,
        name: p.name!,
        email: p.email!,
      }))
      if (uninvited.length > 0) {
        setUninvitedParents(uninvited)
      }
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setIsLoading(false)
    }
  }

  const sendParentInvite = useCallback(async (record: InvitableRecord) => {
    await postApiV1ParentInvitationsByParentIdResend({
      path: { parentId: record.id },
      throwOnError: true,
    })
  }, [])

  return (
    <div className="space-y-4">
      <div className="bg-blue-50 border border-blue-200 rounded-lg px-4 py-3 text-sm text-blue-800">
        Kopiér data fra Excel eller Google Sheets og indsæt i gitteret nedenfor. Brug Tab-tasten til
        at navigere mellem celler.
      </div>

      {uninvitedParents ? (
        <>
          <div className="bg-green-50 border border-green-200 rounded-lg px-4 py-3 space-y-1">
            <p className="font-semibold text-green-800 text-sm">Import fuldført</p>
            {result && (
              <ul className="text-sm text-green-800 list-disc list-inside space-y-0.5">
                <li>{pluralize(result.classesCreated, 'klasse', 'klasser')} oprettet</li>
                <li>{pluralize(result.studentsCreated, 'elev', 'elever')} oprettet</li>
                {(result.studentsSkipped ?? 0) > 0 && (
                  <li>
                    {pluralize(result.studentsSkipped, 'elev', 'elever')} sprunget over (fandtes
                    allerede)
                  </li>
                )}
                <li>{pluralize(result.parentsCreated, 'forælder', 'forældre')} oprettet</li>
                {(result.parentsUpdated ?? 0) > 0 && (
                  <li>{pluralize(result.parentsUpdated, 'forælder', 'forældre')} opdateret</li>
                )}
                <li>{result.parentStudentLinksCreated} forældre knyttet til elever</li>
              </ul>
            )}
            {result && <WarningList warnings={result.warnings ?? []} />}
          </div>

          <div className="border border-gray-200 rounded-xl p-4 space-y-3">
            <div>
              <p className="font-semibold text-gray-900 text-sm">Send invitationer til forældre</p>
              <p className="text-xs text-gray-500 mt-0.5">
                Forældre uden aktiv konto kan modtage en invitation via e-mail.
              </p>
            </div>
            <InvitationStep
              records={uninvitedParents}
              summaryLabel={(n) => `${n} forældre vil modtage en invitation`}
              confirmMessage={(n) => `Er du sikker? ${n} forældre modtager en invitation.`}
              onSendInvite={sendParentInvite}
              onDone={() => {
                setUninvitedParents(null)
                setResult(null)
                setRows([])
              }}
            />
          </div>
        </>
      ) : (
        <>
          <div className="flex items-center justify-between">
            <p className="text-sm text-gray-600">
              {validCount > 0 ? (
                <span className="font-medium text-gray-900">
                  {pluralize(validCount, 'elev', 'elever')} klar til import
                </span>
              ) : (
                'Ingen rækker klar — udfyld mindst klasse og elevnavn'
              )}
            </p>
            <button
              type="button"
              onClick={handleImport}
              disabled={!validCount || isLoading}
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {isLoading ? 'Importerer…' : 'Importér'}
            </button>
          </div>

          <PasteGrid columns={STUDENT_COLUMNS} rows={rows} onChange={setRows} />

          {error && (
            <div className="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800">
              {error}
            </div>
          )}

          {result && !uninvitedParents && (
            <div className="bg-green-50 border border-green-200 rounded-lg px-4 py-3 space-y-1">
              <p className="font-semibold text-green-800 text-sm">Import fuldført</p>
              <ul className="text-sm text-green-800 list-disc list-inside space-y-0.5">
                <li>{pluralize(result.classesCreated, 'klasse', 'klasser')} oprettet</li>
                <li>{pluralize(result.studentsCreated, 'elev', 'elever')} oprettet</li>
                {(result.studentsSkipped ?? 0) > 0 && (
                  <li>
                    {pluralize(result.studentsSkipped, 'elev', 'elever')} sprunget over (fandtes
                    allerede)
                  </li>
                )}
                <li>{pluralize(result.parentsCreated, 'forælder', 'forældre')} oprettet</li>
                {(result.parentsUpdated ?? 0) > 0 && (
                  <li>{pluralize(result.parentsUpdated, 'forælder', 'forældre')} opdateret</li>
                )}
                <li>{result.parentStudentLinksCreated} forældre knyttet til elever</li>
              </ul>
              <WarningList warnings={result.warnings ?? []} />
            </div>
          )}
        </>
      )}
    </div>
  )
}

// ── Tab 2: Personale ──────────────────────────────────────────────────────────

function StaffTab() {
  const [rows, setRows] = useState<GridRow[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [result, setResult] = useState<ImportsControllerImportStaffResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [uninvitedStaff, setUninvitedStaff] = useState<InvitableRecord[] | null>(null)

  const validCount = useMemo(() => countStaffRows(rows), [rows])

  async function handleImport() {
    const payload = rows
      .filter((r) => r.name?.trim())
      .map((r) => ({
        name: r.name.trim(),
        email: r.email?.trim() || null,
        phone: r.phone?.trim() || null,
        role: r.role?.trim() || null,
        administrator: r.administrator?.trim() || null,
      }))

    if (!payload.length) return
    setIsLoading(true)
    setError(null)
    setResult(null)
    setUninvitedStaff(null)
    try {
      const res = await postApiV1ImportsStaff({ body: { rows: payload }, throwOnError: true })
      setResult(res.data!)

      if ((res.data!.staffCreated ?? 0) > 0 || (res.data!.staffUpdated ?? 0) > 0) {
        const importedEmails = new Set(
          payload
            .map((r) => r.email)
            .filter((e): e is string => !!e)
            .map((e) => e.toLowerCase())
        )
        const staffRes = await getApiV1Staff({ throwOnError: true })
        const uninvited = (staffRes.data ?? [])
          .filter((s) => !s.keycloakSubject && s.email && importedEmails.has(s.email.toLowerCase()))
          .map((s) => ({ id: s.id!, name: s.name!, email: s.email! }))
        if (uninvited.length > 0) {
          setUninvitedStaff(uninvited)
        }
      }
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setIsLoading(false)
    }
  }

  const sendStaffInvite = useCallback(async (record: InvitableRecord) => {
    await postApiV1StaffInvitationsInviteByStaffId({
      path: { staffId: record.id },
      throwOnError: true,
    })
  }, [])

  return (
    <div className="space-y-4">
      <div className="bg-blue-50 border border-blue-200 rounded-lg px-4 py-3 text-sm text-blue-800">
        Kopiér medarbejderdata fra Excel eller Google Sheets og indsæt nedenfor. Roller accepteres
        som: <strong>Lærer</strong>, <strong>Pædagog</strong>, <strong>Vikar</strong>.
      </div>

      {uninvitedStaff ? (
        <>
          <div className="bg-green-50 border border-green-200 rounded-lg px-4 py-3 space-y-1">
            <p className="font-semibold text-green-800 text-sm">Import fuldført</p>
            {result && (
              <ul className="text-sm text-green-800 list-disc list-inside space-y-0.5">
                <li>{pluralize(result.staffCreated, 'medarbejder', 'medarbejdere')} oprettet</li>
                {(result.staffUpdated ?? 0) > 0 && (
                  <li>{pluralize(result.staffUpdated, 'medarbejder', 'medarbejdere')} opdateret</li>
                )}
                {(result.staffSkipped ?? 0) > 0 && (
                  <li>
                    {pluralize(result.staffSkipped, 'medarbejder', 'medarbejdere')} sprunget over
                  </li>
                )}
              </ul>
            )}
            {result && <WarningList warnings={result.warnings ?? []} />}
          </div>

          <div className="border border-gray-200 rounded-xl p-4 space-y-3">
            <div>
              <p className="font-semibold text-gray-900 text-sm">
                Send invitationer til medarbejdere
              </p>
              <p className="text-xs text-gray-500 mt-0.5">
                Medarbejdere uden aktiv konto kan modtage en invitation via e-mail.
              </p>
            </div>
            <InvitationStep
              records={uninvitedStaff}
              summaryLabel={(n) => `${n} medarbejdere vil modtage en invitation`}
              confirmMessage={(n) => `Er du sikker? ${n} medarbejdere modtager en invitation.`}
              onSendInvite={sendStaffInvite}
              onDone={() => {
                setUninvitedStaff(null)
                setResult(null)
                setRows([])
              }}
            />
          </div>
        </>
      ) : (
        <>
          <div className="flex items-center justify-between">
            <p className="text-sm text-gray-600">
              {validCount > 0 ? (
                <span className="font-medium text-gray-900">
                  {pluralize(validCount, 'medarbejder', 'medarbejdere')} klar til import
                </span>
              ) : (
                'Ingen rækker klar — udfyld mindst navn'
              )}
            </p>
            <button
              type="button"
              onClick={handleImport}
              disabled={!validCount || isLoading}
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {isLoading ? 'Importerer…' : 'Importér'}
            </button>
          </div>

          <PasteGrid columns={STAFF_COLUMNS} rows={rows} onChange={setRows} />

          {error && (
            <div className="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800">
              {error}
            </div>
          )}

          {result && !uninvitedStaff && (
            <div className="bg-green-50 border border-green-200 rounded-lg px-4 py-3 space-y-1">
              <p className="font-semibold text-green-800 text-sm">Import fuldført</p>
              <ul className="text-sm text-green-800 list-disc list-inside space-y-0.5">
                <li>{pluralize(result.staffCreated, 'medarbejder', 'medarbejdere')} oprettet</li>
                {(result.staffUpdated ?? 0) > 0 && (
                  <li>{pluralize(result.staffUpdated, 'medarbejder', 'medarbejdere')} opdateret</li>
                )}
                {(result.staffSkipped ?? 0) > 0 && (
                  <li>
                    {pluralize(result.staffSkipped, 'medarbejder', 'medarbejdere')} sprunget over
                  </li>
                )}
              </ul>
              <WarningList warnings={result.warnings ?? []} />
            </div>
          )}
        </>
      )}
    </div>
  )
}

// ── Tab: Klasser ───────────────────────────────────────────────────────────────

function ClassesTab() {
  const [rows, setRows] = useState<GridRow[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [result, setResult] = useState<ImportsControllerImportClassesResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  const validCount = useMemo(() => countClassRows(rows), [rows])

  async function handleImport() {
    const payload = rows
      .filter((r) => r.name?.trim())
      .map((r) => ({
        name: r.name.trim(),
        description: r.description?.trim() || null,
        gradeLevel: r.gradeLevel?.trim() || null,
      }))

    if (!payload.length) return
    setIsLoading(true)
    setError(null)
    setResult(null)
    try {
      const res = await postApiV1ImportsClasses({ body: { rows: payload }, throwOnError: true })
      setResult(res.data!)
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="space-y-4">
      <div className="bg-amber-50 border border-amber-200 rounded-lg px-4 py-3 text-sm text-amber-800">
        <strong>Kun nødvendigt hvis du ikke importerer elever.</strong> Klasser oprettes automatisk
        når du importerer elever under "Elever & forældre". Brug kun denne fane hvis du vil oprette
        klasser, før du har en elevliste klar.
      </div>

      <div className="flex items-center justify-between">
        <p className="text-sm text-gray-600">
          {validCount > 0 ? (
            <span className="font-medium text-gray-900">
              {pluralize(validCount, 'klasse', 'klasser')} klar til import
            </span>
          ) : (
            'Ingen rækker klar — udfyld mindst klassenavn'
          )}
        </p>
        <button
          type="button"
          onClick={handleImport}
          disabled={!validCount || isLoading}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {isLoading ? 'Importerer…' : 'Importér'}
        </button>
      </div>

      <PasteGrid columns={CLASS_COLUMNS} rows={rows} onChange={setRows} />

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800">
          {error}
        </div>
      )}

      {result && (
        <div className="bg-green-50 border border-green-200 rounded-lg px-4 py-3 space-y-1">
          <p className="font-semibold text-green-800 text-sm">Import fuldført</p>
          <ul className="text-sm text-green-800 list-disc list-inside space-y-0.5">
            <li>{pluralize(result.classesCreated, 'klasse', 'klasser')} oprettet</li>
            {(result.classesUpdated ?? 0) > 0 && (
              <li>{pluralize(result.classesUpdated, 'klasse', 'klasser')} opdateret</li>
            )}
            {(result.classesSkipped ?? 0) > 0 && (
              <li>{pluralize(result.classesSkipped, 'klasse', 'klasser')} sprunget over</li>
            )}
          </ul>
          <WarningList warnings={result.warnings ?? []} />
        </div>
      )}
    </div>
  )
}

// ── Tab 3: Lokaler ────────────────────────────────────────────────────────────

function RoomsTab() {
  const [rows, setRows] = useState<GridRow[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [result, setResult] = useState<ImportsControllerImportRoomsResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  const validCount = useMemo(() => countRoomRows(rows), [rows])

  async function handleImport() {
    const payload = rows
      .filter((r) => r.name?.trim())
      .map((r) => ({
        name: r.name.trim(),
        description: r.description?.trim() || null,
        capacity: r.capacity?.trim() || null,
      }))

    if (!payload.length) return
    setIsLoading(true)
    setError(null)
    setResult(null)
    try {
      const res = await postApiV1ImportsRooms({ body: { rows: payload }, throwOnError: true })
      setResult(res.data!)
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="space-y-4">
      <div className="bg-blue-50 border border-blue-200 rounded-lg px-4 py-3 text-sm text-blue-800">
        Kopiér lokaledata fra Excel eller Google Sheets og indsæt nedenfor.
      </div>

      <div className="flex items-center justify-between">
        <p className="text-sm text-gray-600">
          {validCount > 0 ? (
            <span className="font-medium text-gray-900">
              {pluralize(validCount, 'lokale', 'lokaler')} klar til import
            </span>
          ) : (
            'Ingen rækker klar — udfyld mindst navn'
          )}
        </p>
        <button
          type="button"
          onClick={handleImport}
          disabled={!validCount || isLoading}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {isLoading ? 'Importerer…' : 'Importér'}
        </button>
      </div>

      <PasteGrid columns={ROOM_COLUMNS} rows={rows} onChange={setRows} />

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800">
          {error}
        </div>
      )}

      {result && (
        <div className="bg-green-50 border border-green-200 rounded-lg px-4 py-3 space-y-1">
          <p className="font-semibold text-green-800 text-sm">Import fuldført</p>
          <ul className="text-sm text-green-800 list-disc list-inside space-y-0.5">
            <li>{pluralize(result.roomsCreated, 'lokale', 'lokaler')} oprettet</li>
            {(result.roomsUpdated ?? 0) > 0 && (
              <li>{pluralize(result.roomsUpdated, 'lokale', 'lokaler')} opdateret</li>
            )}
            {(result.roomsSkipped ?? 0) > 0 && (
              <li>{pluralize(result.roomsSkipped, 'lokale', 'lokaler')} sprunget over</li>
            )}
          </ul>
          <WarningList warnings={result.warnings ?? []} />
        </div>
      )}
    </div>
  )
}

// ── Tab 4: Bestyrelsesmedlemmer ───────────────────────────────────────────────

function BoardMembersTab() {
  const [rows, setRows] = useState<GridRow[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [result, setResult] = useState<ImportsControllerImportBoardMembersResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [uninvitedMembers, setUninvitedMembers] = useState<InvitableRecord[] | null>(null)

  const validCount = useMemo(() => countBoardRows(rows), [rows])

  async function handleImport() {
    const payload = rows
      .filter((r) => r.name?.trim() && r.email?.trim())
      .map((r) => ({
        name: r.name.trim(),
        email: r.email.trim(),
        canAccessTeacherData: r.canAccessTeacherData?.trim() || null,
      }))

    if (!payload.length) return
    setIsLoading(true)
    setError(null)
    setResult(null)
    setUninvitedMembers(null)
    try {
      const res = await postApiV1ImportsBoardMembers({
        body: { rows: payload },
        throwOnError: true,
      })
      setResult(res.data!)

      if ((res.data!.boardMembersCreated ?? 0) > 0 || (res.data!.boardMembersUpdated ?? 0) > 0) {
        const importedEmails = new Set(payload.map((r) => r.email.toLowerCase()))
        const membersRes = await getApiV1BoardMembers({ throwOnError: true })
        const uninvited = (membersRes.data ?? [])
          .filter((m) => !m.hasAccount && m.email && importedEmails.has(m.email.toLowerCase()))
          .map((m) => ({ id: m.id!, name: m.name!, email: m.email! }))
        if (uninvited.length > 0) {
          setUninvitedMembers(uninvited)
        }
      }
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setIsLoading(false)
    }
  }

  const sendBoardInvite = useCallback(async (record: InvitableRecord) => {
    await postApiV1BoardMembersInvite({
      body: { name: record.name, email: record.email },
      throwOnError: true,
    })
  }, [])

  return (
    <div className="space-y-4">
      <div className="bg-blue-50 border border-blue-200 rounded-lg px-4 py-3 text-sm text-blue-800">
        Kopiér bestyrelsesmedlemsdata fra Excel eller Google Sheets og indsæt nedenfor. Navn og
        e-mail er påkrævet.
      </div>

      {uninvitedMembers ? (
        <>
          <div className="bg-green-50 border border-green-200 rounded-lg px-4 py-3 space-y-1">
            <p className="font-semibold text-green-800 text-sm">Import fuldført</p>
            {result && (
              <ul className="text-sm text-green-800 list-disc list-inside space-y-0.5">
                <li>
                  {pluralize(
                    result.boardMembersCreated,
                    'bestyrelsesmedlem',
                    'bestyrelsesmedlemmer'
                  )}{' '}
                  oprettet
                </li>
                {(result.boardMembersUpdated ?? 0) > 0 && (
                  <li>
                    {pluralize(
                      result.boardMembersUpdated,
                      'bestyrelsesmedlem',
                      'bestyrelsesmedlemmer'
                    )}{' '}
                    opdateret
                  </li>
                )}
                {(result.boardMembersSkipped ?? 0) > 0 && (
                  <li>{result.boardMembersSkipped} rækker sprunget over</li>
                )}
              </ul>
            )}
            {result && <WarningList warnings={result.warnings ?? []} />}
          </div>

          <div className="border border-gray-200 rounded-xl p-4 space-y-3">
            <div>
              <p className="font-semibold text-gray-900 text-sm">
                Send invitationer til bestyrelsesmedlemmer
              </p>
              <p className="text-xs text-gray-500 mt-0.5">
                Bestyrelsesmedlemmer uden aktiv konto kan modtage en invitation via e-mail.
              </p>
            </div>
            <InvitationStep
              records={uninvitedMembers}
              summaryLabel={(n) => `${n} bestyrelsesmedlemmer vil modtage en invitation`}
              confirmMessage={(n) =>
                `Er du sikker? ${n} bestyrelsesmedlemmer modtager en invitation.`
              }
              onSendInvite={sendBoardInvite}
              onDone={() => {
                setUninvitedMembers(null)
                setResult(null)
                setRows([])
              }}
            />
          </div>
        </>
      ) : (
        <>
          <div className="flex items-center justify-between">
            <p className="text-sm text-gray-600">
              {validCount > 0 ? (
                <span className="font-medium text-gray-900">
                  {pluralize(validCount, 'bestyrelsesmedlem', 'bestyrelsesmedlemmer')} klar til
                  import
                </span>
              ) : (
                'Ingen rækker klar — udfyld mindst navn og e-mail'
              )}
            </p>
            <button
              type="button"
              onClick={handleImport}
              disabled={!validCount || isLoading}
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {isLoading ? 'Importerer…' : 'Importér'}
            </button>
          </div>

          <PasteGrid columns={BOARD_COLUMNS} rows={rows} onChange={setRows} />

          {error && (
            <div className="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800">
              {error}
            </div>
          )}

          {result && !uninvitedMembers && (
            <div className="bg-green-50 border border-green-200 rounded-lg px-4 py-3 space-y-1">
              <p className="font-semibold text-green-800 text-sm">Import fuldført</p>
              <ul className="text-sm text-green-800 list-disc list-inside space-y-0.5">
                <li>
                  {pluralize(
                    result.boardMembersCreated,
                    'bestyrelsesmedlem',
                    'bestyrelsesmedlemmer'
                  )}{' '}
                  oprettet
                </li>
                {(result.boardMembersUpdated ?? 0) > 0 && (
                  <li>
                    {pluralize(
                      result.boardMembersUpdated,
                      'bestyrelsesmedlem',
                      'bestyrelsesmedlemmer'
                    )}{' '}
                    opdateret
                  </li>
                )}
                {(result.boardMembersSkipped ?? 0) > 0 && (
                  <li>{result.boardMembersSkipped} rækker sprunget over</li>
                )}
              </ul>
              <WarningList warnings={result.warnings ?? []} />
            </div>
          )}
        </>
      )}
    </div>
  )
}

// ── Main page ──────────────────────────────────────────────────────────────────

type TabKey = 'students' | 'classes' | 'staff' | 'rooms' | 'board'

const TABS: { key: TabKey; label: string }[] = [
  { key: 'students', label: 'Elever & forældre' },
  { key: 'classes', label: 'Klasser (valgfri)' },
  { key: 'staff', label: 'Personale' },
  { key: 'rooms', label: 'Lokaler' },
  { key: 'board', label: 'Bestyrelsesmedlemmer' },
]

export default function ImportPage() {
  usePageTitle('Importer data')
  const [activeTab, setActiveTab] = useState<TabKey>('students')
  const [searchParams] = useSearchParams()
  const fraOpsaetning = searchParams.get('fraOpsaetning') === '1'
  const { hasParentModule, hasBoardModule, isSuccess: modulesLoaded } = useSubscription()

  return (
    <div className="p-6 lg:p-8 max-w-7xl mx-auto space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="font-display text-2xl font-semibold text-gray-900">Importer data</h1>
          <p className="mt-1 text-sm text-gray-500">
            Indsæt data fra Excel eller Google Sheets direkte i gitteret
          </p>
        </div>
        {fraOpsaetning && (
          <Link
            to="/setup"
            className="shrink-0 px-4 py-2 text-sm font-medium text-brand-700 border border-brand-200 rounded-lg hover:bg-brand-50 transition-colors whitespace-nowrap"
          >
            Færdig — tilbage til opsætning
          </Link>
        )}
      </div>

      {/* Tab bar */}
      <div className="border-b border-gray-200">
        <nav className="flex gap-1 -mb-px overflow-x-auto">
          {TABS.map((tab) => (
            <button
              key={tab.key}
              type="button"
              onClick={() => setActiveTab(tab.key)}
              className={`px-4 py-2.5 text-sm font-medium whitespace-nowrap border-b-2 transition-colors ${
                activeTab === tab.key
                  ? 'border-brand-600 text-brand-700'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      {/* Tab content */}
      <div>
        {activeTab === 'students' &&
          (modulesLoaded && !hasParentModule ? (
            <ModuleInactiveNotice
              testId="import-students-module-inactive"
              text="Forældremodulet er ikke aktivt, så du kan ikke importere elever og forældre. Klasser kan du stadig importere under fanen Klasser."
            />
          ) : (
            <StudentsTab />
          ))}
        {activeTab === 'classes' && <ClassesTab />}
        {activeTab === 'staff' && <StaffTab />}
        {activeTab === 'rooms' && <RoomsTab />}
        {activeTab === 'board' &&
          (modulesLoaded && !hasBoardModule ? (
            <ModuleInactiveNotice
              testId="import-board-module-inactive"
              text="Bestyrelsesmodulet er ikke aktivt, så du kan ikke importere bestyrelsesmedlemmer."
            />
          ) : (
            <BoardMembersTab />
          ))}
      </div>
    </div>
  )
}
