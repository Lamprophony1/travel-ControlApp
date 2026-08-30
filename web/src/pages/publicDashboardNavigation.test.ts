import { describe, expect, it } from 'vitest'
import type { PublicDashboard, PublicMissingCounts } from '../types'
import { pendingPassengerDestination } from './publicDashboardNavigation'

const empty: PublicMissingCounts = {
  tickets: 0, baggage: 0, documentation: 0, passports: 0,
  passengersWithoutResolvedAccommodation: 0, unresolvedRoomReservations: 0,
  specificPropertiesPending: 0, transfer: false,
}

function destination(attentionPassengers: number, missing: Partial<PublicMissingCounts>) {
  return pendingPassengerDestination({ attentionPassengers, missing: { ...empty, ...missing } } as Pick<PublicDashboard, 'attentionPassengers' | 'missing'>)
}

describe('pendingPassengerDestination', () => {
  it('prioritizes attention over every requirement', () => expect(destination(1, { tickets: 2 })).toBe('/pasajeros?overall=Attention'))
  it('uses the requested deterministic requirement order', () => {
    expect(destination(0, { tickets: 1, baggage: 1 })).toBe('/pasajeros?requirement=flight')
    expect(destination(0, { baggage: 1, documentation: 1 })).toBe('/pasajeros?requirement=baggage')
    expect(destination(0, { documentation: 1, passports: 1 })).toBe('/pasajeros?requirement=documentation')
    expect(destination(0, { passports: 1, passengersWithoutResolvedAccommodation: 1 })).toBe('/pasajeros?requirement=passport')
    expect(destination(0, { passengersWithoutResolvedAccommodation: 1 })).toBe('/pasajeros?requirement=room')
  })
  it('opens the unfiltered list for global-only pending items', () => expect(destination(0, { transfer: true })).toBe('/pasajeros'))
})
