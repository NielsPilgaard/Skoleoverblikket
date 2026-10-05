import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Helmet } from 'react-helmet-async'
import { api, ApiError } from '../api/client'
import type { StaffRole } from '../api/client'
import { TimeInput } from '../components/TimeInput'
import { LessonDurationSlider } from '../components/LessonDurationSlider'
import { usePageTitle } from '../hooks/usePageTitle'
import { useAuth } from '../auth/useAuth'
import { detectGradeLevel, GRADE_LEVEL_LABELS } from '../utils/gradeLevel'
import { pluralize } from '../lib/pluralize'
import {
  getApiV1SchoolsOnboardingStatusOptions,
  getApiV1SchoolsOnboardingStatusQueryKey,
} from '../api/generated/@tanstack/react-query.gen'
import type { OnboardingStatusDto } from '../api/client'

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface WizardStep {
  id: number
  title: string
  description: string
}

const STEPS: WizardStep[] = [
  { id: 1, title: 'Skoledag', description: 'Definér varighed og pauser for en normal skoledag' },
  {
    id: 2,
    title: 'Hvordan vil du oprette resten?',
    description: 'Vælg den metode der passer dig bedst',
  },
  { id: 3, title: 'Klasser', description: 'Opret dine første klasser, f.eks. 0.a, 1.a' },
  { id: 4, title: 'Lokaler', description: 'Tilføj lokaler, f.eks. Lokale 1' },
  { id: 5, title: 'Medarbejdere', description: 'Invitér lærere og pædagoger' },
  { id: 6, title: 'Færdig', description: 'Din skole er klar til brug' },
]

// ---------------------------------------------------------------------------
// Step components
// ---------------------------------------------------------------------------

interface BreakEntry {
  startTime: string
  durationMinutes: number
}

function StepTimeSlots({ onNext, onSkip }: { onNext: () => void; onSkip: () => void }) {
  const [lessonDuration, setLessonDuration] = useState(45)
  const [dayStart, setDayStart] = useState('08:00')
  const [dayEnd, setDayEnd] = useState('15:00')
  const [breaks, setBreaks] = useState<BreakEntry[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [savedBefore, setSavedBefore] = useState(false)

  useEffect(() => {
    api
      .get<{
        lessonDurationMinutes: number
        dayStartTime: string
        dayEndTime: string
        breaks: { startTime: string; durationMinutes: number }[]
      }>('/time-slot-template')
      .then((t) => {
        setLessonDuration(t.lessonDurationMinutes)
        setDayStart(t.dayStartTime.slice(0, 5))
        setDayEnd(t.dayEndTime.slice(0, 5))
        setBreaks(
          t.breaks.map((b) => ({
            startTime: b.startTime.slice(0, 5),
            durationMinutes: b.durationMinutes,
          }))
        )
        setSavedBefore(true)
      })
      .catch((err) => {
        if (!(err instanceof ApiError) || err.status !== 404) return
        // No saved template yet — pre-fill a typical Danish school day.
        // Breaks must land on lesson boundaries (08:00 + n*45min).
        // After break at 09:30 (15min), slots resume at 09:45 → boundaries: 09:45, 10:30, 11:15, 12:00, 12:45...
        setDayStart('08:00')
        setDayEnd('14:00')
        setLessonDuration(45)
        setBreaks([
          { startTime: '09:30', durationMinutes: 15 },
          { startTime: '12:00', durationMinutes: 30 },
        ])
      })
  }, [])

  function addBreak() {
    setBreaks((prev) => [...prev, { startTime: '10:00', durationMinutes: 15 }])
  }

  function updateBreak(i: number, field: keyof BreakEntry, value: string | number) {
    setBreaks((prev) => prev.map((b, idx) => (idx === i ? { ...b, [field]: value } : b)))
  }

  function removeBreak(i: number) {
    setBreaks((prev) => prev.filter((_, idx) => idx !== i))
  }

  async function save() {
    setSaving(true)
    setError('')
    try {
      await api.put('/time-slot-template', {
        lessonDurationMinutes: lessonDuration,
        dayStartTime: `${dayStart}:00`,
        dayEndTime: `${dayEnd}:00`,
        activeDays: 'MTWTF',
        breaks: breaks.map((b) => ({
          startTime: `${b.startTime}:00`,
          durationMinutes: b.durationMinutes,
        })),
      })
      onNext()
    } catch (e) {
      if (e instanceof ApiError) {
        try {
          const problem = JSON.parse(e.message) as { detail?: string }
          setError(problem.detail ?? 'Kunne ikke gemme skoledag. Prøv igen.')
        } catch {
          setError('Kunne ikke gemme skoledag. Prøv igen.')
        }
      } else {
        setError('Kunne ikke gemme skoledag. Prøv igen.')
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="space-y-5">
      <p className="text-sm text-gray-600">
        Sæt standard lektionslængde og skoledagens rammer. Du kan altid justere det senere.
      </p>

      <div className="grid grid-cols-2 gap-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Skoledag starter</label>
          <TimeInput label="Skoledag starter" value={dayStart} onChange={setDayStart} />
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Skoledag slutter</label>
          <TimeInput label="Skoledag slutter" value={dayEnd} onChange={setDayEnd} />
        </div>
      </div>

      <LessonDurationSlider value={lessonDuration} onChange={setLessonDuration} />

      <div>
        <div className="flex items-center justify-between mb-2">
          <label className="text-sm font-medium text-gray-700">Pauser</label>
          <button
            onClick={addBreak}
            className="flex items-center gap-1 text-sm text-brand-600 hover:text-brand-700"
          >
            <svg
              width="13"
              height="13"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
            >
              <line x1="12" y1="5" x2="12" y2="19" />
              <line x1="5" y1="12" x2="19" y2="12" />
            </svg>
            Tilføj pause
          </button>
        </div>
        {breaks.length === 0 && (
          <p className="text-sm text-gray-400 italic">Ingen faste pauser — tilføj om nødvendigt.</p>
        )}
        <div className="space-y-2">
          {breaks.map((b, i) => (
            <div key={i} className="flex items-center gap-2">
              <div className="flex-1 grid grid-cols-2 gap-2">
                <div>
                  <label className="block text-xs text-gray-500 mb-0.5">Starttidspunkt</label>
                  <TimeInput
                    label={`Pause ${i + 1}, starttidspunkt`}
                    value={b.startTime}
                    onChange={(v) => updateBreak(i, 'startTime', v)}
                  />
                </div>
                <div>
                  <label className="block text-xs text-gray-500 mb-0.5">Varighed (min)</label>
                  <input
                    type="number"
                    min={5}
                    max={60}
                    value={b.durationMinutes}
                    aria-label={`Pause ${i + 1}, varighed (min)`}
                    onChange={(e) => updateBreak(i, 'durationMinutes', Number(e.target.value))}
                    onFocus={(e) => e.target.select()}
                    className="w-full px-2 py-1.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
                  />
                </div>
              </div>
              <button
                onClick={() => removeBreak(i)}
                aria-label={`Fjern pause ${i + 1}`}
                className="mt-4 p-1.5 text-gray-400 hover:text-red-500 rounded-md hover:bg-red-50 transition-colors"
              >
                <svg
                  width="13"
                  height="13"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                >
                  <line x1="18" y1="6" x2="6" y2="18" />
                  <line x1="6" y1="6" x2="18" y2="18" />
                </svg>
              </button>
            </div>
          ))}
        </div>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}
      <div className="flex items-center gap-3 pt-2">
        <button
          onClick={save}
          disabled={saving}
          className="px-5 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 disabled:opacity-50 transition-colors"
        >
          {saving ? 'Gemmer…' : 'Gem og fortsæt'}
        </button>
        <button onClick={onSkip} className="px-4 py-2 text-sm text-gray-500 hover:text-gray-700">
          Spring over
        </button>
        {savedBefore && (
          <span className="ml-auto flex items-center gap-1 text-xs text-green-600">
            <svg
              width="12"
              height="12"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
            >
              <polyline points="20 6 9 17 4 12" />
            </svg>
            Gemt
          </span>
        )}
      </div>
    </div>
  )
}

function StepChooseMethod({ onManual, onImport }: { onManual: () => void; onImport: () => void }) {
  return (
    <div className="space-y-3">
      <button
        onClick={onImport}
        className="w-full flex items-start gap-4 p-4 border-2 border-brand-200 rounded-xl text-left hover:border-brand-500 hover:bg-brand-50 transition-colors"
      >
        <svg
          width="24"
          height="24"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          className="shrink-0 text-brand-600 mt-0.5"
        >
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
          <polyline points="17 8 12 3 7 8" />
          <line x1="12" y1="3" x2="12" y2="15" />
        </svg>
        <span>
          <span className="block text-sm font-semibold text-gray-900">
            Jeg har data i et regneark
          </span>
          <span className="block mt-0.5 text-sm text-gray-500">
            Indsæt elever, medarbejdere og lokaler direkte fra Excel eller Google Sheets — hurtigst
            hvis du allerede har listerne klar.
          </span>
        </span>
      </button>
      <button
        onClick={onManual}
        className="w-full flex items-start gap-4 p-4 border-2 border-gray-200 rounded-xl text-left hover:border-gray-300 hover:bg-gray-50 transition-colors"
      >
        <svg
          width="24"
          height="24"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          className="shrink-0 text-gray-500 mt-0.5"
        >
          <line x1="12" y1="5" x2="12" y2="19" />
          <line x1="5" y1="12" x2="19" y2="12" />
        </svg>
        <span>
          <span className="block text-sm font-semibold text-gray-900">
            Jeg opretter det manuelt
          </span>
          <span className="block mt-0.5 text-sm text-gray-500">
            Kom i gang med det samme — tilføj klasser, lokaler og medarbejdere ét ad gangen.
          </span>
        </span>
      </button>
    </div>
  )
}

function StepCreateItems({
  noun,
  plural,
  placeholder,
  apiPath,
  onNext,
  onSkip,
}: {
  noun: string
  plural: string
  placeholder: string
  apiPath: string
  onNext: () => void
  onSkip: () => void
}) {
  const [items, setItems] = useState<string[]>([''])
  const [existingNames, setExistingNames] = useState<Set<string>>(new Set())
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [savedBefore, setSavedBefore] = useState(false)

  useEffect(() => {
    api
      .get<{ name: string }[]>(apiPath)
      .then((existing) => {
        if (existing.length > 0) {
          const names = existing.map((e) => e.name)
          setItems(names)
          setExistingNames(new Set(names))
          setSavedBefore(true)
        }
      })
      .catch(() => {})
  }, [apiPath])

  function addRow() {
    setItems((prev) => [...prev, ''])
  }
  function updateRow(i: number, val: string) {
    setItems((prev) => prev.map((v, idx) => (idx === i ? val : v)))
  }
  function removeRow(i: number) {
    setItems((prev) => prev.filter((_, idx) => idx !== i))
  }

  async function save() {
    const names = items.map((n) => n.trim()).filter(Boolean)
    const newNames = names.filter((n) => !existingNames.has(n))
    if (names.length === 0) {
      onNext()
      return
    }
    setSaving(true)
    setError('')
    try {
      await Promise.all(newNames.map((name) => api.post(apiPath, { name })))
      onNext()
    } catch {
      setError(`Kunne ikke oprette ${plural.toLowerCase()}. Prøv igen.`)
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="space-y-5">
      <p className="text-sm text-gray-600">
        Tilføj de {plural.toLowerCase()}, du vil starte med. Du kan altid tilføje flere senere.
      </p>
      <div className="space-y-2">
        {items.map((val, i) => (
          <div key={i} className="flex gap-2">
            <input
              value={val}
              onChange={(e) => updateRow(i, e.target.value)}
              placeholder={placeholder}
              className="flex-1 px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault()
                  addRow()
                }
              }}
            />
            {items.length > 1 && (
              <button
                onClick={() => removeRow(i)}
                className="p-2 text-gray-400 hover:text-red-500 rounded-md hover:bg-red-50 transition-colors"
              >
                <svg
                  width="14"
                  height="14"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                >
                  <line x1="18" y1="6" x2="6" y2="18" />
                  <line x1="6" y1="6" x2="18" y2="18" />
                </svg>
              </button>
            )}
          </div>
        ))}
        <button
          onClick={addRow}
          className="flex items-center gap-1.5 text-sm text-brand-600 hover:text-brand-700 mt-1"
        >
          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.5"
          >
            <line x1="12" y1="5" x2="12" y2="19" />
            <line x1="5" y1="12" x2="19" y2="12" />
          </svg>
          Tilføj {noun.toLowerCase()}
        </button>
      </div>
      {error && <p className="text-sm text-red-600">{error}</p>}
      <div className="flex items-center gap-3 pt-2">
        <button
          onClick={save}
          disabled={saving}
          className="px-5 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 disabled:opacity-50 transition-colors"
        >
          {saving ? 'Opretter…' : `Opret og fortsæt`}
        </button>
        <button onClick={onSkip} className="px-4 py-2 text-sm text-gray-500 hover:text-gray-700">
          Spring over
        </button>
        {savedBefore && (
          <span className="ml-auto flex items-center gap-1 text-xs text-green-600">
            <svg
              width="12"
              height="12"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
            >
              <polyline points="20 6 9 17 4 12" />
            </svg>
            Gemt
          </span>
        )}
      </div>
    </div>
  )
}

interface ClassEntry {
  name: string
  gradeLevel: number | null
}

function StepCreateClasses({ onNext, onSkip }: { onNext: () => void; onSkip: () => void }) {
  const [items, setItems] = useState<ClassEntry[]>([{ name: '', gradeLevel: null }])
  const [existingNames, setExistingNames] = useState<Set<string>>(new Set())
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [savedBefore, setSavedBefore] = useState(false)
  const userHasEditedRef = useRef(false)

  useEffect(() => {
    api
      .get<{ name: string; gradeLevel?: number | null }[]>('/classes')
      .then((existing) => {
        if (userHasEditedRef.current) return
        if (existing.length > 0) {
          setItems(existing.map((e) => ({ name: e.name, gradeLevel: e.gradeLevel ?? null })))
          setExistingNames(new Set(existing.map((e) => e.name)))
          setSavedBefore(true)
        }
      })
      .catch(() => {})
  }, [])

  function addRow() {
    userHasEditedRef.current = true
    setItems((prev) => [...prev, { name: '', gradeLevel: null }])
  }

  function updateName(i: number, val: string) {
    userHasEditedRef.current = true
    setItems((prev) =>
      prev.map((item, idx) => {
        if (idx !== i) return item
        const detected = detectGradeLevel(val)
        return { name: val, gradeLevel: detected !== null ? detected : item.gradeLevel }
      })
    )
  }

  function updateGradeLevel(i: number, val: number | null) {
    userHasEditedRef.current = true
    setItems((prev) => prev.map((item, idx) => (idx === i ? { ...item, gradeLevel: val } : item)))
  }

  function removeRow(i: number) {
    userHasEditedRef.current = true
    setItems((prev) => prev.filter((_, idx) => idx !== i))
  }

  async function save() {
    const valid = items.filter((item) => item.name.trim())
    const newItems = valid.filter((item) => !existingNames.has(item.name.trim()))
    if (valid.length === 0) {
      onNext()
      return
    }
    setSaving(true)
    setError('')
    try {
      await Promise.all(
        newItems.map((item) =>
          api.post('/classes', { name: item.name.trim(), gradeLevel: item.gradeLevel })
        )
      )
      onNext()
    } catch {
      setError('Kunne ikke oprette klasser. Prøv igen.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="space-y-5">
      <p className="text-sm text-gray-600">
        Tilføj dine klasser. Klassetrin registreres automatisk fra navnet — du kan justere det
        manuelt.
      </p>
      <div className="space-y-3">
        {items.map((item, i) => (
          <div key={i} className="flex gap-2 items-start">
            <div className="flex-1 grid grid-cols-2 gap-2">
              <input
                value={item.name}
                onChange={(e) => updateName(i, e.target.value)}
                placeholder="fx 1.a"
                className="px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault()
                    addRow()
                  }
                }}
              />
              <select
                value={item.gradeLevel ?? ''}
                onChange={(e) =>
                  updateGradeLevel(i, e.target.value === '' ? null : Number(e.target.value))
                }
                className="px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent bg-white"
              >
                <option value="">— klassetrin —</option>
                {Object.entries(GRADE_LEVEL_LABELS).map(([val, label]) => (
                  <option key={val} value={val}>
                    {label}
                  </option>
                ))}
              </select>
            </div>
            {items.length > 1 && (
              <button
                onClick={() => removeRow(i)}
                className="mt-1 p-2 text-gray-400 hover:text-red-500 rounded-md hover:bg-red-50 transition-colors"
              >
                <svg
                  width="14"
                  height="14"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                >
                  <line x1="18" y1="6" x2="6" y2="18" />
                  <line x1="6" y1="6" x2="18" y2="18" />
                </svg>
              </button>
            )}
          </div>
        ))}
        <button
          onClick={addRow}
          className="flex items-center gap-1.5 text-sm text-brand-600 hover:text-brand-700 mt-1"
        >
          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.5"
          >
            <line x1="12" y1="5" x2="12" y2="19" />
            <line x1="5" y1="12" x2="19" y2="12" />
          </svg>
          Tilføj klasse
        </button>
      </div>
      {error && <p className="text-sm text-red-600">{error}</p>}
      <div className="flex items-center gap-3 pt-2">
        <button
          onClick={save}
          disabled={saving}
          className="px-5 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 disabled:opacity-50 transition-colors"
        >
          {saving ? 'Opretter…' : 'Opret og fortsæt'}
        </button>
        <button onClick={onSkip} className="px-4 py-2 text-sm text-gray-500 hover:text-gray-700">
          Spring over
        </button>
        {savedBefore && (
          <span className="ml-auto flex items-center gap-1 text-xs text-green-600">
            <svg
              width="12"
              height="12"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
            >
              <polyline points="20 6 9 17 4 12" />
            </svg>
            Gemt
          </span>
        )}
      </div>
    </div>
  )
}

const ROLE_OPTIONS: { value: StaffRole; label: string; hint: string }[] = [
  { value: 'Teacher', label: 'Lærer', hint: 'Underviser klasser' },
  { value: 'Aide', label: 'Pædagog', hint: 'Støtter undervisningen' },
  { value: 'Substitute', label: 'Vikar', hint: 'Vikarierer ved fravær' },
]

interface StaffEntry {
  name: string
  email: string
  role: StaffRole
}

function StepInviteStaff({ onNext, onSkip }: { onNext: () => void; onSkip: () => void }) {
  const [entries, setEntries] = useState<StaffEntry[]>([{ name: '', email: '', role: 'Teacher' }])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [results, setResults] = useState<{ name: string; email: string; ok: boolean }[]>([])

  function addRow() {
    setEntries((prev) => [...prev, { name: '', email: '', role: 'Teacher' }])
  }

  function updateEntry(i: number, field: keyof StaffEntry, value: string) {
    setEntries((prev) => prev.map((e, idx) => (idx === i ? { ...e, [field]: value } : e)))
  }

  function removeRow(i: number) {
    setEntries((prev) => prev.filter((_, idx) => idx !== i))
  }

  async function invite() {
    const valid = entries.filter((e) => e.name.trim() && e.email.trim().includes('@'))
    if (valid.length === 0) {
      onNext()
      return
    }
    setSaving(true)
    setError('')
    try {
      const outcome: { name: string; email: string; ok: boolean }[] = []
      for (const entry of valid) {
        try {
          const staff = await api.post<{ id: string }>('/staff', {
            name: entry.name.trim(),
            email: entry.email.trim(),
            role: entry.role,
          })
          await api.post(`/staff-invitations/invite/${staff.id}`, {})
          outcome.push({ name: entry.name.trim(), email: entry.email.trim(), ok: true })
        } catch {
          outcome.push({ name: entry.name.trim(), email: entry.email.trim(), ok: false })
        }
      }
      setResults(outcome)
    } catch {
      setError('Der opstod en fejl. Prøv igen.')
    } finally {
      setSaving(false)
    }
  }

  if (results.length > 0) {
    const succeeded = results.filter((r) => r.ok).length
    return (
      <div className="space-y-5">
        <div className="bg-green-50 border border-green-200 rounded-xl p-4">
          <p className="text-sm font-medium text-green-800">
            {pluralize(succeeded, 'invitation', 'invitationer')} sendt
          </p>
          <ul className="mt-2 space-y-1">
            {results.map((r) => (
              <li key={r.email} className="flex items-center gap-2 text-sm">
                {r.ok ? (
                  <svg
                    width="14"
                    height="14"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.5"
                    className="text-green-600 shrink-0"
                  >
                    <polyline points="20 6 9 17 4 12" />
                  </svg>
                ) : (
                  <svg
                    width="14"
                    height="14"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.5"
                    className="text-red-500 shrink-0"
                  >
                    <line x1="18" y1="6" x2="6" y2="18" />
                    <line x1="6" y1="6" x2="18" y2="18" />
                  </svg>
                )}
                <span className={r.ok ? 'text-gray-700' : 'text-red-600'}>
                  {r.name} — {r.email}
                </span>
              </li>
            ))}
          </ul>
        </div>
        <button
          onClick={onNext}
          className="px-5 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
        >
          Fortsæt
        </button>
      </div>
    )
  }

  return (
    <div className="space-y-5">
      <p className="text-sm text-gray-600">
        Tilføj de medarbejdere, du vil invitere. Du kan altid invitere flere fra medarbejdersiden.
      </p>

      <div className="space-y-3">
        {entries.map((entry, i) => (
          <div key={i} className="border border-gray-200 rounded-xl p-3 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-gray-500">Medarbejder {i + 1}</span>
              {entries.length > 1 && (
                <button
                  onClick={() => removeRow(i)}
                  className="p-1 text-gray-400 hover:text-red-500 rounded hover:bg-red-50 transition-colors"
                >
                  <svg
                    width="13"
                    height="13"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                  >
                    <line x1="18" y1="6" x2="6" y2="18" />
                    <line x1="6" y1="6" x2="18" y2="18" />
                  </svg>
                </button>
              )}
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="block text-xs text-gray-500 mb-0.5">Fulde navn</label>
                <input
                  value={entry.name}
                  onChange={(e) => updateEntry(i, 'name', e.target.value)}
                  placeholder="Anne Jensen"
                  className="w-full px-2.5 py-1.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
                />
              </div>
              <div>
                <label className="block text-xs text-gray-500 mb-0.5">E-mail</label>
                <input
                  type="email"
                  value={entry.email}
                  onChange={(e) => updateEntry(i, 'email', e.target.value)}
                  placeholder="anne@skole.dk"
                  className="w-full px-2.5 py-1.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
                />
              </div>
            </div>
            <div>
              <label className="block text-xs text-gray-500 mb-1">Rolle</label>
              <div className="flex gap-2">
                {ROLE_OPTIONS.map((r) => (
                  <button
                    key={r.value}
                    type="button"
                    onClick={() => updateEntry(i, 'role', r.value)}
                    className={`flex-1 px-2 py-1.5 rounded-lg border text-xs font-medium transition-colors ${
                      entry.role === r.value
                        ? 'border-brand-500 bg-brand-50 text-brand-700 ring-1 ring-brand-500'
                        : 'border-gray-200 text-gray-600 hover:border-gray-300 hover:bg-gray-50'
                    }`}
                  >
                    {r.label}
                  </button>
                ))}
              </div>
            </div>
          </div>
        ))}
        <button
          onClick={addRow}
          className="flex items-center gap-1.5 text-sm text-brand-600 hover:text-brand-700"
        >
          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.5"
          >
            <line x1="12" y1="5" x2="12" y2="19" />
            <line x1="5" y1="12" x2="19" y2="12" />
          </svg>
          Tilføj medarbejder
        </button>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}
      <div className="flex gap-3 pt-2">
        <button
          onClick={invite}
          disabled={saving}
          className="px-5 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 disabled:opacity-50 transition-colors"
        >
          {saving ? 'Sender…' : 'Send invitationer'}
        </button>
        <button onClick={onSkip} className="px-4 py-2 text-sm text-gray-500 hover:text-gray-700">
          Spring over
        </button>
      </div>
    </div>
  )
}

function StepDone({ onFinish }: { onFinish: () => void }) {
  return (
    <div className="space-y-5 text-center py-4">
      <div className="w-16 h-16 bg-green-100 rounded-full flex items-center justify-center mx-auto">
        <svg
          width="32"
          height="32"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.5"
          className="text-green-600"
        >
          <polyline points="20 6 9 17 4 12" />
        </svg>
      </div>
      <div>
        <h3 className="text-lg font-semibold text-gray-900">Din skole er sat op!</h3>
        <p className="mt-1 text-sm text-gray-500">
          Du er klar til at begynde at bygge skemaer. Du kan altid ændre indstillingerne under
          Indstillinger.
        </p>
      </div>
      <button
        onClick={onFinish}
        className="px-6 py-2.5 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
      >
        Gå til oversigt
      </button>
    </div>
  )
}

// ---------------------------------------------------------------------------
// Progress indicator
// ---------------------------------------------------------------------------

function ProgressBar({ current, total }: { current: number; total: number }) {
  const pct = Math.round(((current - 1) / (total - 1)) * 100)
  return (
    <div className="space-y-1">
      <div className="flex justify-between text-xs text-gray-500">
        <span>
          Trin {current} af {total}
        </span>
        <span>{pct}% færdig</span>
      </div>
      <div className="h-1.5 bg-gray-100 rounded-full overflow-hidden">
        <div
          className="h-full bg-brand-600 rounded-full transition-all duration-300"
          style={{ width: `${pct}%` }}
        />
      </div>
    </div>
  )
}

// ---------------------------------------------------------------------------
// Main wizard
// ---------------------------------------------------------------------------

function firstIncompleteStep(status: OnboardingStatusDto): number {
  if ((status.classCount ?? 0) === 0) return 1
  if ((status.roomCount ?? 0) === 0) return 4
  if ((status.staffCount ?? 0) === 0) return 5
  return 6
}

export default function SchoolSetupWizardPage() {
  usePageTitle('Opsætning')
  const navigate = useNavigate()
  const qc = useQueryClient()
  const userNavigated = useRef(false)
  const [step, setStep] = useState(1)
  const { authenticated } = useAuth()

  const { data: onboardingStatus } = useQuery({
    ...getApiV1SchoolsOnboardingStatusOptions(),
    enabled: authenticated,
    retry: false,
  })

  useEffect(() => {
    if (onboardingStatus && !userNavigated.current) {
      setStep(firstIncompleteStep(onboardingStatus))
    }
  }, [onboardingStatus])

  useEffect(() => {
    if (!authenticated) {
      import('../auth/keycloak').then(({ default: keycloak }) => keycloak.login())
    }
  }, [authenticated])

  if (!authenticated) return null

  const advance = () => {
    userNavigated.current = true
    setStep((s) => Math.min(s + 1, STEPS.length))
  }
  const skip = () => {
    userNavigated.current = true
    setStep((s) => Math.min(s + 1, STEPS.length))
  }

  function finish() {
    qc.invalidateQueries({ queryKey: getApiV1SchoolsOnboardingStatusQueryKey() })
    navigate('/dashboard')
  }

  const current = STEPS[step - 1]

  return (
    <div className="min-h-screen bg-brand-50 flex items-center justify-center p-4">
      <Helmet>
        <meta name="robots" content="noindex,nofollow" />
      </Helmet>
      <div className="bg-white rounded-2xl shadow-lg w-full max-w-lg">
        {/* Header */}
        <div className="px-8 pt-8 pb-5 border-b border-gray-100">
          <div className="flex items-center justify-between mb-4">
            <span className="font-display text-xl font-semibold text-brand-800">
              Skoleoverblikket
            </span>
            <button onClick={finish} className="text-xs text-gray-400 hover:text-gray-600">
              Gem og afslut
            </button>
          </div>
          <ProgressBar current={step} total={STEPS.length} />
        </div>

        {/* Step header */}
        <div className="px-8 pt-6 pb-2">
          <h1 className="font-display text-lg font-semibold text-gray-900">{current.title}</h1>
          <p className="mt-0.5 text-sm text-gray-500">{current.description}</p>
        </div>

        {/* Step body */}
        <div className="px-8 pb-8 pt-4">
          {step === 1 && <StepTimeSlots onNext={advance} onSkip={skip} />}
          {step === 2 && (
            <StepChooseMethod
              onManual={advance}
              onImport={() => navigate('/import?fraOpsaetning=1')}
            />
          )}
          {step === 3 && <StepCreateClasses onNext={advance} onSkip={skip} />}
          {step === 4 && (
            <StepCreateItems
              noun="Lokale"
              plural="Lokaler"
              placeholder="f.eks. Lokale 1"
              apiPath="/rooms"
              onNext={advance}
              onSkip={skip}
            />
          )}
          {step === 5 && <StepInviteStaff onNext={advance} onSkip={skip} />}
          {step === 6 && <StepDone onFinish={finish} />}
        </div>

        {/* Step dots */}
        <div className="px-8 pb-6 flex justify-center gap-1.5">
          {STEPS.map((s) => (
            <button
              key={s.id}
              onClick={() => setStep(s.id)}
              className={`w-2 h-2 rounded-full transition-colors ${
                s.id === step ? 'bg-brand-600' : s.id < step ? 'bg-brand-300' : 'bg-gray-200'
              }`}
              aria-label={`Gå til trin ${s.id}: ${s.title}`}
            />
          ))}
        </div>
      </div>
    </div>
  )
}
