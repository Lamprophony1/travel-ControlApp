import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import OpenInNewIcon from '@mui/icons-material/OpenInNew'
import { Alert, Box, Button, Card, CardContent, LinearProgress, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api'
import { ErrorState, LoadingState } from '../components/LoadingState'
import { StatusChip } from '../components/StatusChip'
import { formatDate } from '../format'
import type { PublicPassenger } from '../types'

export function PublicPassengerDetailPage(){
  const {id}=useParams(),navigate=useNavigate(),location=useLocation()
  const q=useQuery({queryKey:['public','passenger',id],queryFn:()=>api<PublicPassenger>(`/api/public/passengers/${id}`)})
  if(q.isLoading)return <LoadingState/>;if(q.error)return <ErrorState error={q.error}/>
  const p=q.data!
  return <Stack spacing={3}>
    <Button startIcon={<ArrowBackIcon/>} onClick={()=>navigate((location.state as {back?:string}|null)?.back??'/pasajeros')} sx={{alignSelf:'flex-start'}}>Volver a pasajeros</Button>
    <Card><CardContent><Stack direction={{xs:'column',sm:'row'}} justifyContent="space-between" gap={2}><Box><Typography variant="h1">{p.name}</Typography><Typography color="text.secondary">{p.operator??'Sin operadora'} · {p.roomCode??'Sin grupo'}</Typography></Box><StatusChip status={p.overallStatus} size="medium"/></Stack><LinearProgress variant="determinate" value={p.progressPercent} color={p.progressPercent===100?'success':'secondary'} sx={{height:12,borderRadius:6,my:2}}/><Typography fontWeight={800}>{p.progressPercent}% resuelto</Typography></CardContent></Card>
    <Box sx={{display:'grid',gridTemplateColumns:{xs:'1fr',md:'repeat(3,1fr)'},gap:3}}>
      <Card><CardContent><Typography variant="h2" mb={2}>Cinco requisitos</Typography><Stack spacing={1.5}>{p.requirements.map(r=><Stack key={r.key} direction="row" justifyContent="space-between" alignItems="center"><Typography fontWeight={800}>{r.label}</Typography><StatusChip status={r.status}/></Stack>)}</Stack>{p.missing.length>0&&<Alert severity="warning" sx={{mt:2}}>Pendiente: {p.missing.join(', ')}.</Alert>}</CardContent></Card>
      <Card><CardContent><Typography variant="h2" mb={2}>Vuelo</Typography>{p.flights.length===0?<Alert severity="warning">Sin aerolínea · Ticket pendiente</Alert>:<Stack spacing={2}>{p.flights.map((flight,index)=><Box key={`${flight.airline}-${index}`}><Info label="Aerolínea" value={flight.airline}/><Info label="Ticket" value={flight.ticketStatus==='Confirmed'?'Confirmado':'Pendiente'}/>{flight.hasTicketAccess&&flight.ticketAccessPath?<><Button href={flight.ticketAccessPath} target="_blank" rel="noopener noreferrer" startIcon={<OpenInNewIcon/>} sx={{mt:1}}>Abrir mi ticket</Button><Typography variant="caption" display="block" color="text.secondary" mt={1}>Se abrirá el sitio oficial de la aerolínea.</Typography></>:<Alert severity="info" sx={{mt:1}}>Acceso al ticket pendiente</Alert>}</Box>)}</Stack>}</CardContent></Card>
      <Card><CardContent><Typography variant="h2" mb={2}>Alojamiento</Typography><Info label="Código interno" value={p.roomCode}/><Info label="Hotel o propiedad" value={p.hotel}/><Info label="Tipo de habitación" value={p.roomType}/><Info label="Check-in" value={formatDate(p.checkIn)}/><Info label="Check-out" value={formatDate(p.checkOut)}/><Info label="Transfer grupal" value={p.transferConfirmed?'Confirmado':'Pendiente'}/></CardContent></Card>
    </Box>
    {p.alerts.map(a=><Alert key={a} severity="error">{a}</Alert>)}
    <Alert severity="info">Los datos documentales y comprobantes están protegidos. Esta vista presenta únicamente el estado operativo del viaje.</Alert>
  </Stack>
}
function Info({label,value}:{label:string;value?:string|null}){return <Box mb={1}><Typography variant="caption" color="text.secondary" fontWeight={800}>{label.toUpperCase()}</Typography><Typography>{value||'Sin información'}</Typography></Box>}
