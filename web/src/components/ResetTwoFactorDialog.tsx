import { type MutationFunction, useMutation } from '@tanstack/react-query'
import { Modal } from './Modal'

interface Props {
  /** The person to reset, or null when the dialog is closed. */
  person: { id: string; name: string } | null
  /** The generated reset mutationFn for staff, parents or board members. */
  reset: MutationFunction<unknown, { path: { id: string } }> | undefined
  onClose: () => void
}

/** Confirms and runs an admin reset of someone's tofaktor, and shows the API's Danish answer. */
export function ResetTwoFactorDialog({ person, reset, onClose }: Props) {
  const mutation = useMutation({ mutationFn: reset })

  function close() {
    // Closing can't cancel a sent reset, so stay open until the answer arrives.
    if (mutation.isPending) return
    mutation.reset()
    onClose()
  }

  const problem = mutation.error as { title?: string; detail?: string } | null

  return (
    <Modal isOpen={!!person} onClose={close} title="Nulstil tofaktor?" size="sm">
      <div className="px-6 py-5 space-y-4" data-testid="reset-tfa-dialog">
        {mutation.isSuccess ? (
          <p className="text-sm text-gray-700" data-testid="reset-tfa-done">
            Tofaktor er slået fra for {person?.name}.
          </p>
        ) : mutation.isError ? (
          <div className="text-sm" data-testid="reset-tfa-error" role="alert">
            <p className="font-semibold text-red-800">
              {problem?.title ?? 'Tofaktor kunne ikke nulstilles'}
            </p>
            <p className="mt-1 text-red-700">
              {problem?.detail ?? 'Der opstod en fejl. Prøv igen.'}
            </p>
          </div>
        ) : (
          <p className="text-sm text-gray-700">
            {person?.name} kan logge ind med kun adgangskode, og bliver logget ud alle steder.
          </p>
        )}
        <div className="flex justify-end gap-3 pt-2">
          {mutation.isSuccess || mutation.isError ? (
            <button
              type="button"
              onClick={close}
              className="px-4 py-2 text-sm font-medium text-white bg-brand-600 rounded-lg hover:bg-brand-700 transition-colors"
            >
              OK
            </button>
          ) : (
            <>
              <button
                type="button"
                onClick={close}
                disabled={mutation.isPending}
                className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50"
              >
                Annuller
              </button>
              <button
                type="button"
                data-testid="reset-tfa-confirm"
                disabled={mutation.isPending}
                onClick={() => person && mutation.mutate({ path: { id: person.id } })}
                className="px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-lg hover:bg-red-700 disabled:opacity-50 transition-colors"
              >
                {mutation.isPending ? 'Nulstiller …' : 'Nulstil tofaktor'}
              </button>
            </>
          )}
        </div>
      </div>
    </Modal>
  )
}
