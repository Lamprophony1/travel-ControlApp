import { describe, expect, it } from 'vitest'
import type { PublicDashboard, PublicMissingCounts } from '../types'
import { publicPendingAction } from './publicDashboardNavigation'

const empty: PublicMissingCounts = { tickets: 0, baggage: 0, documentation: 0, passports: 0,
  passengersWithoutResolvedAccommodation: 0, unresolvedRoomReservations: 0, specificPropertiesPending: 0, transfer: false }
function action(attentionPassengers: number, missing: Partial<PublicMissingCounts>) {
  return publicPendingAction({ attentionPassengers, missing: { ...empty, ...missing } } as Pick<PublicDashboard, 'attentionPassengers' | 'missing'>)
}

describe('publicPendingAction', () => {
  it.each([
    [1, {}, 'Ver casos en atención', '/pasajeros?overall=Attention'],
    [0, { tickets: 1 }, 'Ver tickets pendientes', '/pasajeros?requirement=flight'],
    [0, { baggage: 1 }, 'Ver maletas pendientes', '/pasajeros?requirement=baggage'],
    [0, { documentation: 1 }, 'Ver documentación pendiente', '/pasajeros?requirement=documentation'],
    [0, { passports: 1 }, 'Ver pasaportes pendientes', '/pasajeros?requirement=passport'],
    [0, { passengersWithoutResolvedAccommodation: 1 }, 'Ver alojamiento pendiente', '/pasajeros?requirement=room'],
  ] as const)('maps an individual blocker', (attention, missing, label, destination) =>
    expect(action(attention, missing)).toEqual({ label, destination }))
  it('anchors global accommodation only', () => expect(action(0, { specificPropertiesPending: 1 }))
    .toEqual({ label: 'Ver estado de alojamiento', anchor: 'accommodation-status' }))
  it('anchors transfer only', () => expect(action(0, { transfer: true }))
    .toEqual({ label: 'Ver transfer', anchor: 'transfer-status' }))
  it('opens every pending item when groups are mixed', () => expect(action(0, { tickets: 1, transfer: true }))
    .toEqual({ label: 'Ver todos los pendientes', destination: '/pasajeros' }))
})
