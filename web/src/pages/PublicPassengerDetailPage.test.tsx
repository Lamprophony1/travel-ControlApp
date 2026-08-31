import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { api } from '../api'
import type { PublicPassenger } from '../types'
import { PublicPassengerDetailPage } from './PublicPassengerDetailPage'

vi.mock('../api', () => ({ api: vi.fn() }))

test('muestra aerolínea y estado sin exponer PNR ni número de ticket', async () => {
  const passenger: PublicPassenger = {
    id: 'fixture-passenger', name: 'Persona ficticia', operator: 'Operadora ficticia',
    overallStatus: 'Attention', progressPercent: 80, requirements: [], missing: [], alerts: [],
    transferConfirmed: false,
    flights: [{ airline: 'Copa Airlines', ticketStatus: 'Confirmed', hasTicketAccess: true, ticketAccessPath: '/ticket/opaque-fixture-token' }],
  }
  vi.mocked(api).mockResolvedValue(passenger)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/pasajeros/fixture-passenger']}>
        <Routes><Route path="/pasajeros/:id" element={<PublicPassengerDetailPage />} /></Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )

  expect(await screen.findByRole('heading', { name: 'Persona ficticia' })).toBeVisible()
  expect(screen.getByText('Copa Airlines')).toBeVisible()
  expect(screen.getByText('Confirmado')).toBeVisible()
  const link=screen.getByRole('link',{name:'Abrir mi ticket'})
  expect(link).toHaveAttribute('href','/ticket/opaque-fixture-token')
  expect(link).toHaveAttribute('target','_blank')
  expect(link.getAttribute('href')).not.toContain('PRIVATE-PNR-999')
  expect(screen.queryByText('PRIVATE-PNR-999')).not.toBeInTheDocument()
  expect(screen.queryByText('PRIVATE-ELECTRONIC-TICKET-999')).not.toBeInTheDocument()
})
