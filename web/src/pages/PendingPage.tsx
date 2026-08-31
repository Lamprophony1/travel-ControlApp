import { Alert, Box, Card, CardActionArea, CardContent, Chip, Paper, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { api } from '../api'
import { ErrorState, LoadingState } from '../components/LoadingState'
import { formatDate } from '../format'
import type { DashboardData, Paged, Passenger, TicketAccessStatus, VerificationStatus } from '../types'

interface FollowUp {id:string;passengerId?:string;passenger?:string;title:string;dueDate?:string;status:'Open'|'InProgress'|'Closed';priority:string}
interface Flight {id:string;pnr?:string;airline?:string;baggageStatus:VerificationStatus;passengers:{ticketAccessStatus:TicketAccessStatus}[]}
const labels:Record<string,string>={ToVerify:'Por verificar',InProgress:'En gestión',NotIncluded:'No incluido',NotApplicable:'No aplica',Confirmed:'Confirmado'}

export function PendingPage(){
  const nav=useNavigate()
  const peopleQuery=useQuery({queryKey:['pending'],queryFn:()=>api<Paged<Passenger>>('/api/passengers?page=1&pageSize=100')})
  const dashboard=useQuery({queryKey:['dashboard'],queryFn:()=>api<DashboardData>('/api/dashboard')})
  const followups=useQuery({queryKey:['follow-ups'],queryFn:()=>api<FollowUp[]>('/api/follow-ups')})
  const flights=useQuery({queryKey:['flights'],queryFn:()=>api<Flight[]>('/api/flights')})
  if(peopleQuery.isLoading||dashboard.isLoading||followups.isLoading||flights.isLoading)return <LoadingState/>
  if(peopleQuery.error||dashboard.error||followups.error||flights.error)return <ErrorState error={(peopleQuery.error||dashboard.error||followups.error||flights.error) as Error}/>
  const people=peopleQuery.data!.items.filter(x=>x.overallStatus!=='Ready'),today=new Date().toISOString().slice(0,10)
  const open=followups.data!.filter(x=>x.status!=='Closed'),overdue=open.filter(x=>x.dueDate&&x.dueDate<today),upcoming=open.filter(x=>!x.dueDate||x.dueDate>=today)
  const byRequirement=(key:string)=>people.filter(p=>p.requirements.some(r=>r.key===key&&r.status!=='Confirmed'&&r.status!=='NotApplicable'))
  const baggagePending=flights.data!.filter(x=>x.baggageStatus==='ToVerify'||x.baggageStatus==='InProgress')
  const baggageExcluded=flights.data!.filter(x=>x.baggageStatus==='NotIncluded')
  const ticketAccessPending=flights.data!.reduce((total,flight)=>total+flight.passengers.filter(x=>x.ticketAccessStatus!=='Verified').length,0)
  return <Stack spacing={3}>
    <Box><Typography variant="h1">Pendientes</Typography><Typography color="text.secondary">Las acciones de equipaje se agrupan por reserva; las métricas mantienen la cantidad de personas afectadas.</Typography></Box>
    <Stack direction={{xs:'column',md:'row'}} gap={2}><Alert severity={ticketAccessPending?'warning':'success'} sx={{flex:1}}>{ticketAccessPending} accesos a ticket pendientes</Alert><Alert severity={baggagePending.length?'warning':'success'} sx={{flex:1}}>{baggagePending.length} PNR con equipaje por verificar</Alert><Alert severity={baggageExcluded.length?'error':'success'} sx={{flex:1}}>{baggageExcluded.length} PNR no incluyen maleta</Alert></Stack>
    <Section title="Equipaje por PNR">{[...baggageExcluded,...baggagePending].length===0?<Typography color="text.secondary">Sin pendientes.</Typography>:[...baggageExcluded,...baggagePending].map(f=><Card key={f.id} variant="outlined"><CardActionArea onClick={()=>nav('/gestion/vuelos?focus=baggage')}><CardContent><Stack direction={{xs:'column',sm:'row'}} justifyContent="space-between"><Box><Typography fontWeight={850}>{f.airline??'Aerolínea pendiente'} · {f.pnr??'PNR pendiente'}</Typography><Typography color="text.secondary">{f.passengers.length} pasajero(s) afectados</Typography></Box><Chip label={labels[f.baggageStatus]} color={f.baggageStatus==='NotIncluded'?'error':'warning'}/></Stack></CardContent></CardActionArea></Card>)}</Section>
    <Section title="Inconsistencias críticas"><PassengerCards items={people.filter(x=>x.overallStatus==='Attention')} onOpen={id=>nav(`/gestion/pasajeros/${id}`)}/></Section>
    <Section title="Transfer global">{!dashboard.data!.transfer.isConfirmed?<Alert severity="error">Transfer grupal pendiente. Este entregable aparece una sola vez para todo el viaje.</Alert>:<Alert severity="success">Transfer grupal confirmado.</Alert>}</Section>
    <FollowSection title="Seguimientos vencidos" items={overdue}/><FollowSection title="Seguimientos próximos" items={upcoming}/>
    <Section title="Tickets pendientes"><PassengerCards items={byRequirement('flight')} onOpen={id=>nav(`/gestion/pasajeros/${id}`)}/></Section>
    <Section title="Documentaciones pendientes"><PassengerCards items={byRequirement('documentation')} onOpen={id=>nav(`/gestion/pasajeros/${id}`)}/></Section>
    <Section title="Pasaportes pendientes"><PassengerCards items={byRequirement('passport')} onOpen={id=>nav(`/gestion/pasajeros/${id}`)}/></Section>
  </Stack>
}
function Section({title,children}:{title:string;children:React.ReactNode}){return <Paper sx={{p:{xs:2,sm:3}}}><Typography variant="h2" mb={2}>{title}</Typography><Stack spacing={1.5}>{children}</Stack></Paper>}
function PassengerCards({items,onOpen}:{items:Passenger[];onOpen:(id:string)=>void}){return items.length===0?<Typography color="text.secondary">Sin pendientes.</Typography>:items.map(p=><Card variant="outlined" key={p.id}><CardActionArea onClick={()=>onOpen(p.id)}><CardContent><Stack direction={{xs:'column',sm:'row'}} justifyContent="space-between"><Box><Typography fontWeight={850}>{p.fullName}</Typography><Typography color="text.secondary">{p.roomCode??'Sin habitación'} · {p.nextAction??'Sin próxima acción'}</Typography></Box><Chip label={p.overallStatus==='Attention'?'Atención':'Pendiente'} color={p.overallStatus==='Attention'?'error':'warning'}/></Stack><Stack direction="row" gap={1} flexWrap="wrap" mt={1}>{p.requirements.filter(x=>x.status!=='Confirmed'&&x.status!=='NotApplicable').map(x=><Chip key={x.key} label={`${x.label}: ${labels[x.status]??x.status}`} variant="outlined" color={x.status==='NotIncluded'?'error':'warning'}/>)}</Stack></CardContent></CardActionArea></Card>)}
function FollowSection({title,items}:{title:string;items:FollowUp[]}){return <Section title={title}>{items.length===0?<Typography color="text.secondary">Sin seguimientos.</Typography>:items.map(item=><Alert key={item.id} severity={item.priority==='Critical'?'error':'warning'}><b>{item.title}</b>{item.passenger?` · ${item.passenger}`:''} · vence {formatDate(item.dueDate)}</Alert>)}</Section>}
