import { useQuery } from '@tanstack/react-query'
import { getApiV1ModulesOptions } from '../api/generated/@tanstack/react-query.gen'
import { useAuth } from '../auth/useAuth'

export function useSubscription() {
  const { authenticated } = useAuth()
  const { data, isLoading, isSuccess, isError } = useQuery({
    ...getApiV1ModulesOptions(),
    enabled: authenticated,
  })
  const modules: string[] = Array.isArray(data) ? (data as string[]) : []
  // A failed lookup must not lock users out of valid actions; the API still enforces modules.
  return {
    hasParentModule: isError || modules.includes('ParentModule'),
    hasBoardModule: isError || modules.includes('BoardModule'),
    isLoading,
    isSuccess,
  }
}
