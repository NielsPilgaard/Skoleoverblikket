import { useQuery } from '@tanstack/react-query'
import { getApiV1ModulesOptions } from '../api/generated/@tanstack/react-query.gen'
import { useAuth } from '../auth/useAuth'

export function useSubscription() {
  const { authenticated } = useAuth()
  const { data, isLoading, isSuccess, isError } = useQuery({
    ...getApiV1ModulesOptions(),
    enabled: authenticated,
  })
  const hasData = Array.isArray(data)
  const modules: string[] = hasData ? (data as string[]) : []
  // A failed lookup must not lock users out of valid actions; the API still enforces modules.
  // Cached data from an earlier success wins over a failed refetch.
  const failOpen = isError && !hasData
  return {
    hasParentModule: failOpen || modules.includes('ParentModule'),
    hasBoardModule: failOpen || modules.includes('BoardModule'),
    isLoading,
    isSuccess,
  }
}
