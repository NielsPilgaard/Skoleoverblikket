import { useQuery } from '@tanstack/react-query'
import { getApiV1ModulesOptions } from '../api/generated/@tanstack/react-query.gen'
import { useAuth } from '../auth/useAuth'

export function useSubscription() {
  const { authenticated } = useAuth()
  const { data, isLoading } = useQuery({ ...getApiV1ModulesOptions(), enabled: authenticated })
  const modules: string[] = Array.isArray(data) ? (data as string[]) : []
  return {
    hasParentModule: modules.includes('ParentModule'),
    hasBoardModule: modules.includes('BoardModule'),
    isLoading,
  }
}
