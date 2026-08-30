import AssignmentIcon from '@mui/icons-material/Assignment'
import FilterAltOffIcon from '@mui/icons-material/FilterAltOff'
import SearchIcon from '@mui/icons-material/Search'
import {
  Alert, Box, Button, Card, CardActionArea, CardContent, Checkbox, Dialog, DialogActions,
  DialogContent, DialogTitle, InputAdornment, LinearProgress, MenuItem, Pagination, Paper,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TableSortLabel,
  TextField, Typography,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { api, postJson } from '../api'
import { ErrorState, LoadingState } from '../components/LoadingState'
import { StatusChip } from '../components/StatusChip'
import { formatDate } from '../format'
import type { Paged, Passenger, RequirementState } from '../types'

interface RoomOption { id: string; internalCode: string }
interface OperatorOption { id: string; name: string }

const requirementOptions = [
  ['passport', 'Pasaporte'], ['documentation', 'Documentación'], ['room', 'Habitación'],
  ['flight', 'Ticket'], ['baggage', 'Maleta 23 kg'],
]
const statusOptions = [
  ['ToVerify', 'Por verificar'], ['InProgress', 'En gestión'], ['Confirmed', 'Confirmado'],
  ['NotIncluded', 'No incluido'], ['NotApplicable', 'No aplica'],
]
const sortOptions = [
  ['name', 'Pasajero'], ['operator', 'Operadora'], ['group', 'Grupo'], ['hotel', 'Hotel'],
  ['overall', 'Estado general'], ['progress', 'Progreso'], ['due', 'Fecha límite'], ['updated', 'Última modificación'],
]

function requirement(passenger: Passenger, key: string): RequirementState {
  return passenger.requirements.find(item => item.key === key) ?? { key, label: key, status: 'ToVerify' }
}

export function PassengersPage() {
  const navigate = useNavigate()
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number(params.get('page') || 1))
  const [search, setSearch] = useState(params.get('search') ?? '')
  const [selected, setSelected] = useState(new Set<string>())
  const [bulkOpen, setBulkOpen] = useState(false)
  const [room, setRoom] = useState('')
  const [action, setAction] = useState('')
  const [due, setDue] = useState('')

  const queryString = new URLSearchParams(params)
  queryString.set('page', String(page))
  queryString.set('pageSize', '25')
  const query = useQuery({ queryKey: ['passengers', queryString.toString()], queryFn: () => api<Paged<Passenger>>(`/api/passengers?${queryString}`) })
  const rooms = useQuery({ queryKey: ['rooms', 'options'], queryFn: () => api<RoomOption[]>('/api/rooms') })
  const operators = useQuery({ queryKey: ['operators'], queryFn: () => api<OperatorOption[]>('/api/operators') })
  const bulk = useMutation({
    mutationFn: () => postJson<{ updated: number }>('/api/passengers/bulk-assign', {
      passengerIds: [...selected], roomReservationId: room || null, flightBookingId: null,
      nextAction: action || null, nextActionDueDate: due || null,
    }),
    onSuccess: async () => {
      setSelected(new Set()); setBulkOpen(false)
      await Promise.all([client.invalidateQueries({ queryKey: ['passengers'] }), client.invalidateQueries({ queryKey: ['dashboard'] })])
    },
  })

  const setFilter = (key: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value); else next.delete(key)
    next.set('page', '1')
    setParams(next)
  }
  const submitSearch = () => setFilter('search', search.trim())
  const clearFilters = () => { setSearch(''); setParams(new URLSearchParams({ page: '1' })) }
  const sort = (key: string) => {
    const current = params.get('sortBy') ?? 'name'
    setFilter('sortDirection', current === key && params.get('sortDirection') !== 'desc' ? 'desc' : 'asc')
    const next = new URLSearchParams(params)
    next.set('sortBy', key)
    next.set('sortDirection', current === key && params.get('sortDirection') !== 'desc' ? 'desc' : 'asc')
    next.set('page', '1')
    setParams(next)
  }
  const toggle = (id: string) => setSelected(current => {
    const next = new Set(current)
    if (next.has(id)) next.delete(id); else next.add(id)
    return next
  })
  const openPassenger = (id: string) => navigate(`/gestion/pasajeros/${id}`, { state: { back: `/gestion/pasajeros?${params}` } })

  if (query.isLoading) return <LoadingState />
  if (query.error) return <ErrorState error={query.error} />
  const data = query.data!

  return <Stack spacing={3}>
    <Box>
      <Typography variant="h1">Pasajeros</Typography>
      <Typography color="text.secondary">Buscá, filtrá y actuá sobre los cinco requisitos de cada persona.</Typography>
    </Box>

    <Card>
      <CardContent>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
          <TextField fullWidth value={search} onChange={event => setSearch(event.target.value)} onKeyDown={event => event.key === 'Enter' && submitSearch()}
            label="Buscar por nombre o PNR" slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon /></InputAdornment> } }} />
          <Button variant="contained" onClick={submitSearch}>Buscar</Button>
          <Button startIcon={<FilterAltOffIcon />} onClick={clearFilters}>Limpiar</Button>
        </Stack>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2,1fr)', lg: 'repeat(4,1fr)' }, gap: 1.5, mt: 2 }}>
          <TextField size="small" label="Pasaporte" value={params.get('passport') ?? ''} onChange={event => setFilter('passport', event.target.value)} />
          <TextField size="small" label="PNR" value={params.get('pnr') ?? ''} onChange={event => setFilter('pnr', event.target.value)} />
          <TextField size="small" label="Ticket" value={params.get('ticket') ?? ''} onChange={event => setFilter('ticket', event.target.value)} />
          <TextField size="small" label="Código de grupo" value={params.get('groupCode') ?? ''} onChange={event => setFilter('groupCode', event.target.value)} />
          <TextField select size="small" label="Operadora" value={params.get('operatorName') ?? ''} onChange={event => setFilter('operatorName', event.target.value)}>
            <MenuItem value="">Todas</MenuItem>{operators.data?.map(item => <MenuItem key={item.id} value={item.name}>{item.name}</MenuItem>)}
          </TextField>
          <TextField select size="small" label="Aerolínea" value={params.get('airline') ?? ''} onChange={event => setFilter('airline', event.target.value)}>
            <MenuItem value="">Todas</MenuItem><MenuItem value="Copa Airlines">Copa Airlines</MenuItem><MenuItem value="LATAM Airlines">LATAM Airlines</MenuItem><MenuItem value="none">Sin aerolínea</MenuItem>
          </TextField>
          <TextField select size="small" label="Estado general" value={params.get('overall') ?? ''} onChange={event => setFilter('overall', event.target.value)}>
            <MenuItem value="">Todos</MenuItem><MenuItem value="Ready">Listo</MenuItem><MenuItem value="Pending">Pendiente</MenuItem><MenuItem value="Attention">Atención</MenuItem>
          </TextField>
          <TextField select size="small" label="Requisito" value={params.get('requirement') ?? ''} onChange={event => setFilter('requirement', event.target.value)}>
            <MenuItem value="">Todos</MenuItem>{requirementOptions.map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}
          </TextField>
          <TextField select size="small" label="Estado del requisito" value={params.get('status') ?? ''} onChange={event => setFilter('status', event.target.value)} disabled={!params.get('requirement')}>
            <MenuItem value="">No resueltos</MenuItem>{statusOptions.map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}
          </TextField>
          <TextField select size="small" label="Propiedad" value={params.get('propertyPending') ?? ''} onChange={event => setFilter('propertyPending', event.target.value)}>
            <MenuItem value="">Todas</MenuItem><MenuItem value="true">Hotel pendiente</MenuItem>
          </TextField>
          <TextField select size="small" label="Próxima acción" value={params.get('overdue') ?? ''} onChange={event => setFilter('overdue', event.target.value)}>
            <MenuItem value="">Todas</MenuItem><MenuItem value="true">Vencida</MenuItem>
          </TextField>
          <TextField select size="small" label="Ordenar por" value={params.get('sortBy') ?? 'name'} onChange={event => setFilter('sortBy', event.target.value)}>
            {sortOptions.map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}
          </TextField>
          <TextField select size="small" label="Dirección" value={params.get('sortDirection') ?? 'asc'} onChange={event => setFilter('sortDirection', event.target.value)}>
            <MenuItem value="asc">Ascendente</MenuItem><MenuItem value="desc">Descendente</MenuItem>
          </TextField>
        </Box>
        {selected.size > 0 && <Alert severity="info" sx={{ mt: 2 }} action={<Button startIcon={<AssignmentIcon />} onClick={() => setBulkOpen(true)}>Asignar datos</Button>}>{selected.size} pasajeros seleccionados</Alert>}
      </CardContent>
    </Card>

    <Box data-testid="passenger-mobile-cards" sx={{ display: { xs: 'block', lg: 'none' } }}>
      <Stack spacing={1.5}>{data.items.map(passenger => <Card key={passenger.id}>
        <CardActionArea onClick={() => openPassenger(passenger.id)}>
          <CardContent>
            <Stack direction="row" justifyContent="space-between" gap={1}>
              <Box><Typography fontWeight={850} fontSize="1.05rem">{passenger.fullName}</Typography><Typography variant="body2" color="text.secondary">{passenger.operator ?? 'Sin operadora'} · {passenger.roomCode ?? 'Sin grupo'} · {passenger.maskedPassport}</Typography>{passenger.flights.length===0?<Typography variant="body2" color="text.secondary" mt={.75}>Sin reserva</Typography>:passenger.flights.map(flight=><Box key={flight.flightBookingId} mt={.75}><Typography variant="body2" fontWeight={800}>{flight.airline??'Sin aerolínea'}</Typography><Typography variant="body2" color="text.secondary">Reserva: {flight.pnr??'Sin reserva'}</Typography></Box>)}</Box>
              <Checkbox checked={selected.has(passenger.id)} onClick={event => event.stopPropagation()} onChange={() => toggle(passenger.id)} inputProps={{ 'aria-label': `Seleccionar ${passenger.fullName}` }} />
            </Stack>
            <Stack direction="row" gap={1} flexWrap="wrap" my={1.5}>{passenger.requirements.map(item => <Stack key={item.key} direction="row" alignItems="center" gap={.5}><Typography variant="caption" fontWeight={800}>{item.label}</Typography><StatusChip status={item.status} /></Stack>)}</Stack>
            <Stack direction="row" justifyContent="space-between" alignItems="center"><StatusChip status={passenger.overallStatus} /><Typography fontWeight={800}>{passenger.progressPercent}%</Typography></Stack>
            <LinearProgress variant="determinate" value={passenger.progressPercent} color={passenger.progressPercent === 100 ? 'success' : 'secondary'} sx={{ height: 8, borderRadius: 4, my: 1 }} />
            <Typography variant="body2">{passenger.nextAction ?? 'Sin próxima acción'} · {formatDate(passenger.nextActionDueDate)}</Typography>
            {passenger.alerts.map(alert => <Alert key={alert} severity={alert.includes('propiedad') ? 'info' : 'error'} sx={{ mt: 1 }}>{alert}</Alert>)}
          </CardContent>
        </CardActionArea>
      </Card>)}</Stack>
    </Box>

    <TableContainer data-testid="passenger-desktop-table" component={Paper} sx={{ display: { xs: 'none', lg: 'block' }, maxHeight: '68vh' }}>
      <Table stickyHeader size="small" aria-label="Lista privada de pasajeros" sx={{ minWidth: 1540 }}>
        <TableHead><TableRow>
          <TableCell padding="checkbox"><Checkbox aria-label="Seleccionar página" checked={data.items.length > 0 && data.items.every(item => selected.has(item.id))} onChange={event => setSelected(current => { const next = new Set(current); data.items.forEach(item => event.target.checked ? next.add(item.id) : next.delete(item.id)); return next })} /></TableCell>
          <SortableCell label="Pasajero" sortKey="name" params={params} onSort={sort} />
          <TableCell>Nro. pasaporte</TableCell><SortableCell label="Operadora" sortKey="operator" params={params} onSort={sort} />
          <SortableCell label="Grupo" sortKey="group" params={params} onSort={sort} /><TableCell>Aerolínea</TableCell><TableCell>Nro. de reserva</TableCell><TableCell>Ticket</TableCell><SortableCell label="Hotel" sortKey="hotel" params={params} onSort={sort} />
          <TableCell>Estado pasaporte</TableCell><TableCell>Documentación</TableCell><TableCell>Habitación</TableCell><TableCell>Maleta 23 kg</TableCell>
          <SortableCell label="Estado" sortKey="overall" params={params} onSort={sort} /><SortableCell label="Progreso" sortKey="progress" params={params} onSort={sort} />
          <TableCell>Próxima acción</TableCell><SortableCell label="Fecha límite" sortKey="due" params={params} onSort={sort} />
        </TableRow></TableHead>
        <TableBody>{data.items.map(passenger => <TableRow hover key={passenger.id} onClick={() => openPassenger(passenger.id)} sx={{ cursor: 'pointer', '& td': { py: 1 } }}>
          <TableCell padding="checkbox"><Checkbox checked={selected.has(passenger.id)} onClick={event => event.stopPropagation()} onChange={() => toggle(passenger.id)} inputProps={{ 'aria-label': `Seleccionar ${passenger.fullName}` }} /></TableCell>
          <TableCell><Typography fontWeight={800} fontSize=".88rem">{passenger.fullName}</Typography></TableCell><TableCell>{passenger.maskedPassport}</TableCell>
          <TableCell>{passenger.operator ?? '—'}</TableCell><TableCell>{passenger.roomCode ?? '—'}</TableCell>
          <TableCell>{passenger.flights.length===0?'Sin aerolínea':<Stack spacing={.5}>{passenger.flights.map(f=><Typography key={f.flightBookingId} variant="body2">{f.airline??'Sin aerolínea'}</Typography>)}</Stack>}</TableCell>
          <TableCell>{passenger.flights.length===0?'Sin reserva':<Stack spacing={.5}>{passenger.flights.map(f=><Typography key={f.flightBookingId} variant="body2" fontFamily="monospace">{f.pnr??'Sin reserva'}</Typography>)}</Stack>}</TableCell>
          <TableCell>{passenger.flights.length===0?<StatusChip status={requirement(passenger,'flight').status}/>:<Stack spacing={.5}>{passenger.flights.map(f=><StatusChip key={f.flightBookingId} status={f.ticketStatus}/>)}</Stack>}</TableCell>
          <TableCell>{passenger.hotel ?? '—'}</TableCell>
          {['passport', 'documentation', 'room', 'baggage'].map(key => <TableCell key={key}><StatusChip status={requirement(passenger, key).status} /></TableCell>)}
          <TableCell><StatusChip status={passenger.overallStatus} /></TableCell><TableCell><Typography fontWeight={800}>{passenger.progressPercent}%</Typography></TableCell>
          <TableCell sx={{ maxWidth: 180 }}><Typography variant="body2" noWrap>{passenger.nextAction ?? '—'}</Typography></TableCell><TableCell>{formatDate(passenger.nextActionDueDate)}</TableCell>
        </TableRow>)}</TableBody>
      </Table>
    </TableContainer>

    {data.items.length === 0 && <Alert severity="info">No hay pasajeros que coincidan con los filtros.</Alert>}
    <Stack alignItems="center"><Pagination count={Math.max(1, Math.ceil(data.total / 25))} page={page} onChange={(_, value) => { const next = new URLSearchParams(params); next.set('page', String(value)); setParams(next) }} /></Stack>

    <Dialog open={bulkOpen} onClose={() => setBulkOpen(false)} fullWidth>
      <DialogTitle>Asignación masiva</DialogTitle>
      <DialogContent><Typography mb={2}>Se actualizarán {selected.size} pasajeros y el cambio quedará auditado.</Typography><Stack spacing={2}>
        <TextField select label="Código interno de grupo" value={room} onChange={event => setRoom(event.target.value)}><MenuItem value="">Sin cambio</MenuItem>{rooms.data?.map(item => <MenuItem key={item.id} value={item.id}>{item.internalCode}</MenuItem>)}</TextField>
        <TextField label="Próxima acción" value={action} onChange={event => setAction(event.target.value)} />
        <TextField type="date" label="Fecha límite" value={due} onChange={event => setDue(event.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
        {bulk.error && <Alert severity="error">{bulk.error.message}</Alert>}
      </Stack></DialogContent>
      <DialogActions><Button onClick={() => setBulkOpen(false)}>Cancelar</Button><Button variant="contained" disabled={(!room && !action) || bulk.isPending} onClick={() => bulk.mutate()}>Confirmar</Button></DialogActions>
    </Dialog>
  </Stack>
}

function SortableCell({ label, sortKey, params, onSort }: { label: string; sortKey: string; params: URLSearchParams; onSort: (key: string) => void }) {
  const active = (params.get('sortBy') ?? 'name') === sortKey
  return <TableCell sortDirection={active ? (params.get('sortDirection') === 'desc' ? 'desc' : 'asc') : false}>
    <TableSortLabel active={active} direction={params.get('sortDirection') === 'desc' ? 'desc' : 'asc'} onClick={() => onSort(sortKey)}>{label}</TableSortLabel>
  </TableCell>
}
