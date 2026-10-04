import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1DataProcessingAgreementOptions,
  getApiV1DataProcessingAgreementQueryKey,
  postApiV1DataProcessingAgreementAcceptanceMutation,
} from '../api/generated/@tanstack/react-query.gen'
import { useAuth } from '../auth/useAuth'
import { DPA_VERSION } from '../content/dataProcessing'
import { problemDetail } from '../lib/problem'

/** Asks admins to accept the databehandleraftale until the school has accepted the current version. */
export default function DataProcessingAgreementBanner() {
  const { isAdmin, isSuperAdmin } = useAuth()
  const qc = useQueryClient()
  const enabled = isAdmin && !isSuperAdmin
  const { data } = useQuery({ ...getApiV1DataProcessingAgreementOptions(), enabled })

  const accept = useMutation({
    ...postApiV1DataProcessingAgreementAcceptanceMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: getApiV1DataProcessingAgreementQueryKey() }),
  })

  if (!enabled || !data || data.acceptedCurrentVersion) return null

  const updated = data.acceptedVersion != null

  return (
    <div
      className="bg-amber-50 border-b border-amber-200 text-amber-900 px-4 py-2.5 flex flex-col sm:flex-row sm:items-center justify-between gap-2 sm:gap-4 shrink-0"
      data-testid="dpa-banner"
    >
      <p className="text-sm">
        {updated
          ? 'Databehandleraftalen er opdateret. Læs den nye version og acceptér den på vegne af skolen.'
          : 'Skolen mangler at acceptere databehandleraftalen, som GDPR kræver, når vi behandler skolens data.'}{' '}
        <a
          href="/databehandleraftale"
          target="_blank"
          rel="noopener noreferrer"
          className="font-medium underline hover:text-amber-700"
        >
          Læs aftalen
        </a>
        {accept.isError && (
          <span className="block text-red-700 mt-1">
            {problemDetail(accept.error) ?? 'Det lykkedes ikke. Prøv igen.'}
          </span>
        )}
      </p>
      <button
        type="button"
        onClick={() => accept.mutate({ body: { version: DPA_VERSION } })}
        disabled={accept.isPending}
        data-testid="dpa-accept"
        className="shrink-0 px-3 py-1.5 text-xs font-semibold bg-brand-700 text-white rounded-lg hover:bg-brand-800 disabled:opacity-50 transition-colors"
      >
        {accept.isPending ? 'Accepterer…' : 'Acceptér på vegne af skolen'}
      </button>
    </div>
  )
}
