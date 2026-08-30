export type VerificationStatus = 'Confirmed' | 'ToVerify' | 'InProgress' | 'NotIncluded' | 'NotApplicable'
export type OverallStatus = 'Ready' | 'Pending' | 'Attention'
export interface RequirementState { key: string; status: VerificationStatus; label: string; reason?: string }
export interface Passenger {
  id: string; fullName: string; maskedPassport: string; passportStatus: string; operator?: string; roomCode?: string; hotel?: string; roomType?: string
  checkIn?: string; checkOut?: string; nights?: number; documentationStatus: VerificationStatus; overallStatus: OverallStatus; progressPercent: number
  requirements: RequirementState[]; alerts: string[]; nextAction?: string; nextActionDueDate?: string; updatedAt: string; version: number
}
export interface Paged<T> { items: T[]; page: number; pageSize: number; total: number }
export interface DashboardKpi { key: string; label: string; value: number; total: number; percent: number; filter: string }
export interface CategoryProgress { key: string; label: string; confirmed: number; pending: number; inProgress: number; notIncluded: number; notApplicable: number; resolvedPercent: number }
export interface OperatorSummary { name: string; rooms: number; passengers: number; confirmedRooms: number; alerts: string[] }
export interface PriorityAction { severity: 'critical'|'warning'|'info'|'success'; title: string; count: number; filter: string }
export interface TransferStatus { isConfirmed:boolean; confirmedAt?:string; notes?:string; updatedBy?:string; updatedAt:string; version:number }
export interface TripReadiness { overallStatus:'Ready'|'Pending'|'Attention';progressPercent:number;allPassengersReady:boolean;transferConfirmed:boolean;alerts:string[] }
export interface RecentActivity { id:number;passenger?:string;field:string;user?:string;at:string;previous?:string;current?:string }
export interface DashboardData { kpis: DashboardKpi[]; categories: CategoryProgress[]; overallDistribution: Record<string, number>; operators: OperatorSummary[]; priorityActions: PriorityAction[]; recentActivity: RecentActivity[];transfer:TransferStatus;tripReadiness:TripReadiness;roomsConfirmed:number;roomsPending:number;specificPropertiesPending:number }
export interface User { id: string; email: string; displayName: string; roles: string[] }
export interface PublicRequirement { key:string;label:string;status:VerificationStatus }
export interface PublicPassenger {
  id:string;name:string;operator?:string;roomCode?:string;hotel?:string;roomType?:string;checkIn?:string;checkOut?:string
  overallStatus:OverallStatus;progressPercent:number;requirements:PublicRequirement[];missing:string[];alerts:string[];transferConfirmed:boolean
}
export interface PublicMissingCounts {
  tickets:number;baggage:number;documentation:number;passports:number
  passengersWithoutResolvedAccommodation:number;unresolvedRoomReservations:number;specificPropertiesPending:number;transfer:boolean
}
export interface PublicDashboard {
  tripName:string;destination:string;totalPassengers:number;readyPassengers:number;pendingPassengers:number;attentionPassengers:number
  progressPercent:number;overallStatus:OverallStatus;transferConfirmed:boolean;kpis:{key:string;label:string;value:number;total:number;percent:number}[]
  categories:CategoryProgress[];operators:{name:string;rooms:number;passengers:number;resolvedRooms:number}[]
  missing:PublicMissingCounts;alerts:string[];updatedAt:string
}
