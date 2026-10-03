import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1AbsenceLeaveRequestsOptions,
  postApiV1AbsenceByIdApproveMutation,
  postApiV1AbsenceByIdRejectMutation,
} from '../../api/generated/@tanstack/react-query.gen'
import { formatDateRange } from '../../lib/absence'
import { problemDetail } from '../../lib/problem'
import { CategoryBadge } from './AbsenceRegisterTab'

/** Admin: parents' requests for ekstraordinær frihed, pending first, then the last 30 days' decisions. */
export function LeaveRequestsTab() {
  const qc = useQueryClient()
  const { data: requests = [], isLoading } = useQuery(getApiV1AbsenceLeaveRequestsOptions())

  const onSuccess = () => {
    qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1AbsenceLeaveRequests' }] })
    qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1Absence' }] })
    qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1StatsDashboard' }] })
  }
  const approve = useMutation({ ...postApiV1AbsenceByIdApproveMutation(), onSuccess })
  const reject = useMutation({ ...postApiV1AbsenceByIdRejectMutation(), onSuccess })
  const error = approve.error ?? reject.error

  const pending = requests.filter((r) => r.leaveStatus === 'Pending')
  const decided = requests.filter((r) => r.leaveStatus !== 'Pending')

  return (
    <div className="space-y-6">
      {error && (
        <p className="text-sm text-red-600">{problemDetail(error) ?? 'Handlingen mislykkedes.'}</p>
      )}
      {isLoading && <p className="text-sm text-gray-400">Indlæser…</p>}

      <section>
        <h2 className="text-sm font-semibold text-gray-900 mb-2">Afventer</h2>
        {!isLoading && pending.length === 0 && (
          <p className="text-sm text-gray-500">Ingen anmodninger om fri venter.</p>
        )}
        <ul className="space-y-2">
          {pending.map((r) => (
            <li
              key={r.id}
              className="bg-white border border-gray-200 rounded-xl px-4 py-3 flex flex-wrap items-center justify-between gap-3"
              data-testid={`leave-request-${r.id}`}
            >
              <div>
                <p className="text-sm font-medium text-gray-900">
                  {r.studentName} <span className="text-gray-500 font-normal">· {r.className}</span>
                </p>
                <p className="text-sm text-gray-600 mt-0.5">
                  {formatDateRange(r.date, r.endDate)}
                  {r.reason ? ` · ${r.reason}` : ''}
                </p>
              </div>
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => approve.mutate({ path: { id: r.id } })}
                  disabled={approve.isPending || reject.isPending}
                  data-testid={`leave-approve-${r.id}`}
                  className="px-3 py-1.5 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50"
                >
                  Godkend
                </button>
                <button
                  type="button"
                  onClick={() => reject.mutate({ path: { id: r.id } })}
                  disabled={approve.isPending || reject.isPending}
                  data-testid={`leave-reject-${r.id}`}
                  className="px-3 py-1.5 text-sm font-medium border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50 disabled:opacity-50"
                >
                  Afvis
                </button>
              </div>
            </li>
          ))}
        </ul>
      </section>

      {decided.length > 0 && (
        <section>
          <h2 className="text-sm font-semibold text-gray-900 mb-2">Behandlet de seneste 30 dage</h2>
          <ul className="space-y-2">
            {decided.map((r) => (
              <li
                key={r.id}
                className="bg-white border border-gray-200 rounded-xl px-4 py-3 flex flex-wrap items-center justify-between gap-3"
              >
                <div>
                  <p className="text-sm text-gray-900">
                    {r.studentName} <span className="text-gray-500">· {r.className}</span>
                  </p>
                  <p className="text-sm text-gray-600 mt-0.5">
                    {formatDateRange(r.date, r.endDate)}
                  </p>
                </div>
                <CategoryBadge record={r} />
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}
