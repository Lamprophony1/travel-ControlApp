import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createContext, useContext } from 'react'
import { api, postJson } from '../api'
import type { User } from '../types'

interface AuthValue { user?: User; loading: boolean; setupRequired: boolean; login: (email: string, password: string, rememberMe: boolean) => Promise<void>; logout: () => Promise<void>; refresh: () => Promise<void> }
const AuthContext = createContext<AuthValue | null>(null)

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const client = useQueryClient()
  const setup = useQuery({ queryKey: ['setup'], queryFn: () => api<{required: boolean}>('/api/auth/setup-status'), staleTime: 30_000 })
  const me = useQuery({ queryKey: ['me'], queryFn: () => api<User>('/api/auth/me'), retry: false, enabled: setup.data?.required === false })
  const value: AuthValue = {
    user: me.data, loading: setup.isLoading || me.isLoading, setupRequired: setup.data?.required === true,
    login: async (email, password, rememberMe) => { await postJson('/api/auth/login', { email, password, rememberMe }); await client.invalidateQueries({ queryKey: ['me'] }) },
    logout: async () => { await postJson('/api/auth/logout', {}); client.setQueryData(['me'], undefined); await client.invalidateQueries() },
    refresh: async () => { await Promise.all([setup.refetch(), me.refetch()]) }
  }
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() { const value = useContext(AuthContext); if (!value) throw new Error('AuthProvider faltante'); return value }
