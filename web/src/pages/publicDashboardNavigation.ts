import type { PublicDashboard } from '../types'

export function pendingPassengerDestination(data: Pick<PublicDashboard, 'attentionPassengers' | 'missing'>): string {
  if (data.attentionPassengers > 0) return '/pasajeros?overall=Attention'
  if (data.missing.tickets > 0) return '/pasajeros?requirement=flight'
  if (data.missing.baggage > 0) return '/pasajeros?requirement=baggage'
  if (data.missing.documentation > 0) return '/pasajeros?requirement=documentation'
  if (data.missing.passports > 0) return '/pasajeros?requirement=passport'
  if (data.missing.passengersWithoutResolvedAccommodation > 0) return '/pasajeros?requirement=room'
  return '/pasajeros'
}
