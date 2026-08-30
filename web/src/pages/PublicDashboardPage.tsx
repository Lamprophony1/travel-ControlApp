import ArrowForwardIcon from '@mui/icons-material/ArrowForward'
import { Alert, Box, Button, Card, CardActionArea, CardContent, LinearProgress, Paper, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { api } from '../api'
import { ErrorState, LoadingState } from '../components/LoadingState'
import { StatusChip } from '../components/StatusChip'
import type { PublicDashboard } from '../types'
import { pendingPassengerDestination } from './publicDashboardNavigation'

function kpiDestination(key: string) {
  const destinations: Record<string, string> = {
    ready: '/pasajeros?overall=Ready',
    attention: '/pasajeros?overall=Attention',
    pending: '/pasajeros?overall=Pending',
    flights: '/pasajeros?requirement=flight',
    baggage: '/pasajeros?requirement=baggage',
    documentation: '/pasajeros?requirement=documentation',
    passports: '/pasajeros?requirement=passport',
    accommodationPassengers: '/pasajeros?requirement=room',
    roomsConfirmed: '/pasajeros',
  }
  return destinations[key] ?? '/pasajeros'
}

export function PublicDashboardPage() {
  const navigate = useNavigate()
  const query = useQuery({ queryKey: ['public', 'dashboard'], queryFn: () => api<PublicDashboard>('/api/public/dashboard') })
  if (query.isLoading) return <LoadingState />
  if (query.error) return <ErrorState error={query.error} retry={() => void query.refetch()} />

  const data = query.data!
  const hasMissing = Object.values(data.missing).some(value => typeof value === 'boolean' ? value : value > 0) || data.alerts.length > 0
  const missing = [
    data.missing.tickets > 0 && `${data.missing.tickets} tickets pendientes`,
    data.missing.baggage > 0 && `${data.missing.baggage} maletas pendientes`,
    data.missing.documentation > 0 && `${data.missing.documentation} documentaciones pendientes`,
    data.missing.passports > 0 && `${data.missing.passports} pasaportes pendientes`,
    data.missing.passengersWithoutResolvedAccommodation > 0 && `${data.missing.passengersWithoutResolvedAccommodation} pasajeros con alojamiento pendiente`,
    data.missing.unresolvedRoomReservations > 0 && `${data.missing.unresolvedRoomReservations} reservas de habitación pendientes`,
    data.missing.specificPropertiesPending > 0 && `${data.missing.specificPropertiesPending} propiedades de hotel pendientes`,
    data.missing.transfer && 'Transfer grupal pendiente',
  ].filter(Boolean) as string[]

  return <Stack spacing={4}>
    <Box>
      <Typography variant="h1">Estado del viaje</Typography>
      <Typography color="text.secondary" mt={1}>{data.tripName} · {data.destination}</Typography>
    </Box>

    {hasMissing
      ? <Alert severity="error" variant="filled" action={<Button color="inherit" endIcon={<ArrowForwardIcon />} onClick={() => navigate(pendingPassengerDestination(data))}>Ver pasajeros pendientes</Button>}>
          <Typography fontWeight={900}>Todavía faltan entregables para cerrar el viaje</Typography>
          <Typography>{missing.join(' · ')}</Typography>
        </Alert>
      : <Alert severity="success" variant="filled">
          <Typography fontWeight={900}>El viaje está listo</Typography>
          <Typography>Todos los pasajeros están listos, el transfer está confirmado y no hay alertas globales.</Typography>
        </Alert>}

    <Paper sx={{ p: { xs: 2.5, sm: 3 } }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2}>
        <Box><Typography variant="h2">Avance global</Typography><Typography variant="h3" fontWeight={900} mt={1}>{data.progressPercent}%</Typography></Box>
        <StatusChip status={hasMissing ? 'Attention' : 'Ready'} size="medium" />
      </Stack>
      <LinearProgress variant="determinate" value={data.progressPercent} color={hasMissing ? 'error' : 'success'} sx={{ height: 12, borderRadius: 6, mt: 2 }} />
    </Paper>

    <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(min(100%,210px),1fr))', gap: 2 }}>
      {data.kpis.map(kpi => <Card key={kpi.key}>
        <CardActionArea onClick={() => navigate(kpiDestination(kpi.key))} sx={{ height: '100%', minHeight: 44 }}>
          <CardContent>
            <Typography color="text.secondary" fontWeight={750}>{kpi.label}</Typography>
            <Typography variant="h3" fontWeight={900} my={1}>{kpi.value}<Typography component="span" fontSize="1rem" color="text.secondary"> / {kpi.total}</Typography></Typography>
            <LinearProgress variant="determinate" value={kpi.percent} color={kpi.percent === 100 ? 'success' : 'secondary'} />
          </CardContent>
        </CardActionArea>
      </Card>)}
    </Box>

    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1.4fr 1fr' }, gap: 3 }}>
      <Paper sx={{ p: 3 }}>
        <Typography variant="h2" mb={2}>Cinco requisitos</Typography>
        <Stack spacing={2}>{data.categories.map(category => <Box key={category.key}>
          <Stack direction="row" justifyContent="space-between"><Typography fontWeight={800}>{category.label}</Typography><Typography>{category.resolvedPercent}% resuelto</Typography></Stack>
          <LinearProgress variant="determinate" value={category.resolvedPercent} color={category.resolvedPercent === 100 ? 'success' : 'secondary'} sx={{ height: 9, borderRadius: 5, my: .75 }} />
          <Typography variant="caption" color="text.secondary">{category.confirmed} confirmados · {category.notApplicable} no aplica · {category.pending} por verificar · {category.inProgress} en gestión · {category.notIncluded} no incluidos</Typography>
        </Box>)}</Stack>
      </Paper>
      <Paper sx={{ p: 3 }}>
        <Typography variant="h2" mb={2}>Por operadora</Typography>
        <Stack spacing={2}>{data.operators.map(operator => <Box key={operator.name}>
          <Typography fontWeight={850}>{operator.name}</Typography>
          <Typography color="text.secondary">{operator.passengers} pasajeros · {operator.rooms} habitaciones</Typography>
          <Typography color="success.main">{operator.resolvedRooms} habitaciones confirmadas</Typography>
        </Box>)}</Stack>
      </Paper>
    </Box>
    <Typography variant="caption" color="text.secondary">Última actualización operativa: {new Date(data.updatedAt).toLocaleString('es-PY')}. Control preventivo; verificar requisitos migratorios en fuentes oficiales.</Typography>
  </Stack>
}
