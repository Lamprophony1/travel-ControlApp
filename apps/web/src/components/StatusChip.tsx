import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline'
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline'
import HourglassEmptyIcon from '@mui/icons-material/HourglassEmpty'
import RemoveCircleOutlineIcon from '@mui/icons-material/RemoveCircleOutline'
import SyncIcon from '@mui/icons-material/Sync'
import { Chip, type ChipProps } from '@mui/material'

const labels: Record<string, string> = { Confirmed: 'Confirmado', ToVerify: 'Por verificar', InProgress: 'En gestión', NotIncluded: 'No incluido', NotApplicable: 'No aplica', Ready: 'Listo', Pending: 'Pendiente', Attention: 'Atención', Valid: 'Vigente', Incomplete: 'Incompleto', Expired: 'Vencido', ExpiringSoon: 'Por vencer' }
const colors: Record<string, ChipProps['color']> = { Confirmed: 'success', Ready: 'success', Valid: 'success', ToVerify: 'warning', Pending: 'warning', InProgress: 'info', NotIncluded: 'error', Attention: 'error', Expired: 'error', ExpiringSoon: 'warning', Incomplete: 'warning', NotApplicable: 'default' }
const icons: Record<string, React.ReactElement> = { Confirmed: <CheckCircleOutlineIcon/>, Ready: <CheckCircleOutlineIcon/>, Valid: <CheckCircleOutlineIcon/>, ToVerify: <HourglassEmptyIcon/>, Pending: <HourglassEmptyIcon/>, InProgress: <SyncIcon/>, NotIncluded: <ErrorOutlineIcon/>, Attention: <ErrorOutlineIcon/>, Expired: <ErrorOutlineIcon/>, NotApplicable: <RemoveCircleOutlineIcon/> }

export function StatusChip({ status, size = 'small' }: { status: string; size?: 'small'|'medium' }) {
  return <Chip size={size} color={colors[status] ?? 'default'} variant={status === 'Confirmed' || status === 'Ready' ? 'filled' : 'outlined'} icon={icons[status]} label={labels[status] ?? status} />
}

