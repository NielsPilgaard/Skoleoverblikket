import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1ParentsMeOptions,
  getApiV1ParentsMeQueryKey,
  patchApiV1ParentsMeContactMutation,
} from '../../api/generated/@tanstack/react-query.gen'
import { usePageTitle } from '../../hooks/usePageTitle'
import {
  isValidPhone,
  isValidPostalCode,
  normalizePhone,
  PHONE_ERROR_MESSAGE,
  POSTAL_CODE_ERROR_MESSAGE,
} from '../../lib/validation'

export default function ParentProfilePage() {
  usePageTitle('Min profil')
  const qc = useQueryClient()

  const { data: parent, isLoading, isError, refetch } = useQuery(getApiV1ParentsMeOptions())

  const [name, setName] = useState('')
  const [phone, setPhone] = useState('')
  const [address, setAddress] = useState('')
  const [postalCode, setPostalCode] = useState('')
  const [city, setCity] = useState('')
  const [shareContactInfo, setShareContactInfo] = useState(false)

  useEffect(() => {
    if (!parent) return
    setName(parent.name ?? '')
    setPhone(parent.phone ?? '')
    setAddress(parent.address ?? '')
    setPostalCode(parent.postalCode ?? '')
    setCity(parent.city ?? '')
    setShareContactInfo(parent.shareContactInfo ?? false)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [parent?.id])

  const [saved, setSaved] = useState(false)
  const savedTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    return () => {
      if (savedTimeoutRef.current) clearTimeout(savedTimeoutRef.current)
    }
  }, [])

  const updateMutation = useMutation({
    ...patchApiV1ParentsMeContactMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1ParentsMeQueryKey() })
      setSaved(true)
      if (savedTimeoutRef.current) clearTimeout(savedTimeoutRef.current)
      savedTimeoutRef.current = setTimeout(() => {
        setSaved(false)
        savedTimeoutRef.current = null
      }, 4000)
    },
  })

  const phoneValid = isValidPhone(phone)
  const postalCodeValid = isValidPostalCode(postalCode)

  function handleSave() {
    if (!name.trim() || !phoneValid || !postalCodeValid || updateMutation.isPending) return
    setSaved(false)
    updateMutation.mutate({
      body: {
        name: name.trim(),
        phone: phone.trim() ? normalizePhone(phone.trim()) : null,
        address: address.trim() || null,
        postalCode: postalCode.trim() || null,
        city: city.trim() || null,
        shareContactInfo,
      },
    })
  }

  if (isLoading) {
    return <div className="p-4 md:p-6 text-sm text-gray-500">Indlæser profil...</div>
  }

  if (isError) {
    return (
      <div className="p-4 md:p-6 max-w-lg space-y-3">
        <p className="text-sm text-red-600">Kunne ikke hente din profil.</p>
        <button
          type="button"
          onClick={() => refetch()}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
        >
          Prøv igen
        </button>
      </div>
    )
  }

  return (
    <div className="p-4 md:p-6 max-w-lg space-y-6">
      <h1 className="text-xl font-semibold text-gray-900">Min profil</h1>

      <div className="bg-white rounded-xl border border-gray-200 p-5 space-y-4">
        <div>
          <label
            htmlFor="parent-profile-name"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Navn *
          </label>
          <input
            id="parent-profile-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <label
            htmlFor="parent-profile-phone"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Telefon
          </label>
          <input
            id="parent-profile-phone"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            type="tel"
            placeholder="+45 12 34 56 78"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
          {phone.trim() !== '' && !phoneValid && (
            <p className="text-xs text-red-600 mt-1">{PHONE_ERROR_MESSAGE}</p>
          )}
        </div>
        <div>
          <label
            htmlFor="parent-profile-address"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Adresse
          </label>
          <input
            id="parent-profile-address"
            value={address}
            onChange={(e) => setAddress(e.target.value)}
            placeholder="Vejnavn og nummer"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div className="flex gap-3">
          <div className="w-24">
            <label
              htmlFor="parent-profile-postal-code"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              Postnr.
            </label>
            <input
              id="parent-profile-postal-code"
              value={postalCode}
              onChange={(e) => setPostalCode(e.target.value)}
              placeholder="0000"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
            {postalCode.trim() !== '' && !postalCodeValid && (
              <p className="text-xs text-red-600 mt-1">{POSTAL_CODE_ERROR_MESSAGE}</p>
            )}
          </div>
          <div className="flex-1">
            <label
              htmlFor="parent-profile-city"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              By
            </label>
            <input
              id="parent-profile-city"
              value={city}
              onChange={(e) => setCity(e.target.value)}
              placeholder="By"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
          </div>
        </div>
        <label className="flex items-start gap-3 cursor-pointer">
          <input
            type="checkbox"
            checked={shareContactInfo}
            onChange={(e) => setShareContactInfo(e.target.checked)}
            className="mt-0.5 rounded border-gray-300 text-brand-600 focus:ring-brand-500"
          />
          <span className="text-sm text-gray-700">
            Del mine kontaktoplysninger med andre forældre i mit barns klasse
          </span>
        </label>

        {updateMutation.isError && (
          <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
        )}
        {saved && <p className="text-sm text-green-600">Ændringer gemt.</p>}

        <div className="flex justify-end">
          <button
            type="button"
            onClick={handleSave}
            disabled={!name.trim() || !phoneValid || !postalCodeValid || updateMutation.isPending}
            className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {updateMutation.isPending ? 'Gemmer...' : 'Gem'}
          </button>
        </div>
      </div>
    </div>
  )
}
