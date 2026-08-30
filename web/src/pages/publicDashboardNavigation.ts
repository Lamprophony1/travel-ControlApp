import type { PublicDashboard } from '../types'

export interface PublicPendingAction { label: string; destination?: string; anchor?: 'accommodation-status' | 'transfer-status' }

export function publicPendingAction(data: Pick<PublicDashboard, 'attentionPassengers' | 'missing'>): PublicPendingAction {
  const actions: PublicPendingAction[] = []
  if (data.attentionPassengers > 0) actions.push({ label: 'Ver casos en atención', destination: '/pasajeros?overall=Attention' })
  if (data.missing.tickets > 0) actions.push({ label: 'Ver tickets pendientes', destination: '/pasajeros?requirement=flight' })
  if (data.missing.baggage > 0) actions.push({ label: 'Ver maletas pendientes', destination: '/pasajeros?requirement=baggage' })
  if (data.missing.documentation > 0) actions.push({ label: 'Ver documentación pendiente', destination: '/pasajeros?requirement=documentation' })
  if (data.missing.passports > 0) actions.push({ label: 'Ver pasaportes pendientes', destination: '/pasajeros?requirement=passport' })
  if (data.missing.passengersWithoutResolvedAccommodation > 0) actions.push({ label: 'Ver alojamiento pendiente', destination: '/pasajeros?requirement=room' })
  if (data.missing.unresolvedRoomReservations > 0 || data.missing.specificPropertiesPending > 0)
    actions.push({ label: 'Ver estado de alojamiento', anchor: 'accommodation-status' })
  if (data.missing.transfer) actions.push({ label: 'Ver transfer', anchor: 'transfer-status' })
  return actions.length === 1 ? actions[0]! : { label: 'Ver todos los pendientes', destination: '/pasajeros' }
}

export function pendingPassengerDestination(data: Pick<PublicDashboard, 'attentionPassengers' | 'missing'>): string {
  const action = publicPendingAction(data)
  return action.destination ?? `#${action.anchor}`
}
