import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { postApiV1AdminSubProcessorNoticeMutation } from '../../api/generated/@tanstack/react-query.gen'
import { SUB_PROCESSOR_NOTICE_DAYS } from '../../content/dataProcessing'
import { problemDetail } from '../../lib/problem'

function isoDate(daysFromToday: number) {
  const d = new Date()
  d.setDate(d.getDate() + daysFromToday)
  return d.toISOString().slice(0, 10)
}

/**
 * Sends the sub-processor change notice the databehandleraftale promises: every school's admins,
 * at least 30 days ahead. Update SUB_PROCESSORS in content/dataProcessing.ts on the effective date.
 */
export default function BackofficeSubProcessorNoticePage() {
  const [change, setChange] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState(isoDate(SUB_PROCESSOR_NOTICE_DAYS + 1))
  const send = useMutation(postApiV1AdminSubProcessorNoticeMutation())

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if (!window.confirm('Sende varslet til administratorerne på alle skoler?')) return
    send.mutate({ body: { change, effectiveFrom } })
  }

  return (
    <div className="max-w-2xl">
      <h1 className="text-xl font-semibold text-gray-900">Varsel om underdatabehandlere</h1>
      <p className="mt-2 text-sm text-gray-600">
        Sendes som e-mail (Bcc) til administratorerne på alle skoler. Databehandleraftalen lover
        mindst {SUB_PROCESSOR_NOTICE_DAYS} dages varsel. Opdater listen over underdatabehandlere på
        datoen.
      </p>

      <form onSubmit={submit} className="mt-6 space-y-4">
        <label className="block">
          <span className="text-sm font-medium text-gray-700">Ændringen</span>
          <textarea
            value={change}
            onChange={(e) => setChange(e.target.value)}
            rows={5}
            required
            maxLength={2000}
            placeholder="Fx: Vi tilføjer Alexandra Instituttet (Danmark) til AI-forslag til skemaer."
            className="mt-1 w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
          />
        </label>
        <label className="block">
          <span className="text-sm font-medium text-gray-700">Gælder fra</span>
          <input
            type="date"
            value={effectiveFrom}
            min={isoDate(SUB_PROCESSOR_NOTICE_DAYS)}
            onChange={(e) => setEffectiveFrom(e.target.value)}
            required
            className="mt-1 block px-3 py-2 border border-gray-300 rounded-lg text-sm"
          />
        </label>
        <button
          type="submit"
          disabled={!change.trim() || send.isPending}
          className="px-4 py-2 text-sm font-medium bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50"
        >
          {send.isPending ? 'Sender…' : 'Send varsel'}
        </button>
      </form>

      {send.isSuccess && (
        <p className="mt-4 text-sm text-green-700">
          Sendt til {send.data.recipientCount} modtagere på {send.data.schoolCount} skoler.
        </p>
      )}
      {send.isError && (
        <p className="mt-4 text-sm text-red-700">
          {problemDetail(send.error) ?? 'Det lykkedes ikke.'}
        </p>
      )}
    </div>
  )
}
