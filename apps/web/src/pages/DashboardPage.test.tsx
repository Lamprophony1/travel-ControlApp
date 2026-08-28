import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { DashboardPage } from './DashboardPage'
afterEach(()=>vi.restoreAllMocks())
describe('Dashboard',()=>{it('muestra KPI calculados por el backend',async()=>{vi.stubGlobal('fetch',vi.fn().mockResolvedValue({ok:true,headers:new Headers({'content-type':'application/json'}),json:async()=>({kpis:[{key:'passengers',label:'Total de pasajeros',value:46,total:46,percent:100,filter:''}],categories:[],overallDistribution:{},operators:[],priorityActions:[],recentActivity:[]})}));const client=new QueryClient({defaultOptions:{queries:{retry:false}}});render(<QueryClientProvider client={client}><MemoryRouter><DashboardPage/></MemoryRouter></QueryClientProvider>);expect(await screen.findByText('Total de pasajeros')).toBeInTheDocument();expect(screen.getByText('46')).toBeInTheDocument()})})

