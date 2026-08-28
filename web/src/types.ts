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
export interface PriorityAction { severity: 'critical'|'warning'|'info'; title: string; count: number; filter: string }
export interface TransferStatus { isConfirmed:boolean; confirmedAt?:string; notes?:string; updatedBy?:string; updatedAt:string; version:number }
export interface TripReadiness { overallStatus:'Ready'|'Pending'|'Attention';progressPercent:number;allPassengersReady:boolean;transferConfirmed:boolean;alerts:string[] }
export interface RecentActivity { id:number;passenger?:string;field:string;user?:string;at:string;previous?:string;current?:string }
export interface DashboardData { kpis: DashboardKpi[]; categories: CategoryProgress[]; overallDistribution: Record<string, number>; operators: OperatorSummary[]; priorityActions: PriorityAction[]; recentActivity: RecentActivity[];transfer:TransferStatus;tripReadiness:TripReadiness }
export interface User { id: string; email: string; displayName: string; roles: string[] }
