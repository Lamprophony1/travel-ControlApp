import DownloadIcon from '@mui/icons-material/Download'
import UploadFileIcon from '@mui/icons-material/UploadFile'
import {
  Alert, Box, Button, Card, CardContent, Checkbox, CircularProgress, Divider,
  FormControlLabel, List, ListItem, ListItemText, MenuItem, Stack, Switch, TextField, Typography,
} from '@mui/material'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../api'

interface Summary {
  passengerRows: number; roomRows: number; added: number; updated: number; unchanged: number
  errors: number; canCommit: boolean; issues: { level: string; sheet: string; row?: number; message: string }[]
  expectedComparison: Record<string, number>; importRunId?: string
}
interface IdentificationIssue { level: string; row?: number; field?: string; message: string; passportReference?: string; willOverwrite: boolean }
interface IdentificationSummary {
  rowsRead: number; matched: number; unmatched: number; duplicates: number; unchanged: number
  missingFields: number; conflicts: number; invalidDates: number; duplicatePassports: number
  blockingErrors: number; warnings: number; willUpdate: number; willOverwrite: number
  selectedSheet?: string; candidateSheets?: string[]; expiredPassports: number; expiriesBeforeReturn: number
  expiriesWithinWarningThreshold: number; temporallyInconsistentRows: number
  canCommit: boolean; issues: IdentificationIssue[]; importRunId?: string
}
interface IdentificationQuality {
  totalPassengers: number; completePassports: number; incompletePassports: number
  birthDates: number; nationalities: number; passportExpiries: number; duplicatePassports: number
}

export function ImportExportPage() {
  const client = useQueryClient()
  const [file, setFile] = useState<File>()
  const [summary, setSummary] = useState<Summary>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [identificationFile, setIdentificationFile] = useState<File>()
  const [identification, setIdentification] = useState<IdentificationSummary>()
  const [identificationBusy, setIdentificationBusy] = useState(false)
  const [identificationError, setIdentificationError] = useState('')
  const [overwriteExisting, setOverwriteExisting] = useState(false)
  const [confirmOverwrite, setConfirmOverwrite] = useState(false)
  const [sheetName, setSheetName] = useState('')
  const quality = useQuery({ queryKey: ['identification-quality'], queryFn: () => api<IdentificationQuality>('/api/imports/identification/quality') })

  const sendMaster = async (commit: boolean) => {
    if (!file) return
    setBusy(true); setError('')
    try {
      const form = new FormData(); form.append('file', file)
      setSummary(await api<Summary>(`/api/imports/${commit ? 'commit' : 'preview'}`, { method: 'POST', body: form }))
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Falló la importación.') }
    finally { setBusy(false) }
  }
  const sendIdentification = async (commit: boolean) => {
    if (!identificationFile) return
    setIdentificationBusy(true); setIdentificationError('')
    try {
      const form = new FormData()
      form.append('file', identificationFile)
      form.append('overwriteExisting', String(overwriteExisting))
      if (sheetName) form.append('sheetName', sheetName)
      if (commit) form.append('confirmOverwrite', String(confirmOverwrite))
      const result = await api<IdentificationSummary>(`/api/imports/identification/${commit ? 'commit' : 'preview'}`, { method: 'POST', body: form })
      setIdentification(result)
      if (commit && result.importRunId) {
        await Promise.all([
          client.invalidateQueries({ queryKey: ['dashboard'] }),
          client.invalidateQueries({ queryKey: ['passengers'] }),
          client.invalidateQueries({ queryKey: ['identification-quality'] }),
        ])
      }
    } catch (reason) { setIdentificationError(reason instanceof Error ? reason.message : 'Falló la importación de identificación.') }
    finally { setIdentificationBusy(false) }
  }

  return <Stack spacing={3}>
    <Box><Typography variant="h1">Importar y exportar</Typography><Typography color="text.secondary">Las vistas previas no modifican datos. Revisá advertencias antes de confirmar.</Typography></Box>

    <Card>
      <CardContent>
        <Typography variant="h2">Importar identificación</Typography>
        <Typography color="text.secondary" mt={.5}>Completa pasaporte, nacimiento, nacionalidad y vencimiento sobre pasajeros existentes. Nunca crea ni elimina personas.</Typography>
        <Button component="label" startIcon={<UploadFileIcon />} variant="outlined" sx={{ my: 2 }}>
          Elegir XLSX de identificación
          <input hidden type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={event => { setIdentificationFile(event.target.files?.[0]); setIdentification(undefined); setConfirmOverwrite(false); setSheetName('') }} />
        </Button>
        {identificationFile && <Alert severity="info">{identificationFile.name} · {(identificationFile.size / 1024).toFixed(1)} KB</Alert>}
        {(identification?.candidateSheets?.length??0)>1&&<TextField select fullWidth sx={{mt:2}} label="Hoja de identificación" value={sheetName} onChange={event=>setSheetName(event.target.value)}><MenuItem value="">Selección automática</MenuItem>{identification!.candidateSheets!.map(name=><MenuItem key={name} value={name}>{name}</MenuItem>)}</TextField>}
        <Stack mt={2}>
          <FormControlLabel control={<Switch checked={overwriteExisting} onChange={event => { setOverwriteExisting(event.target.checked); setIdentification(undefined); setConfirmOverwrite(false) }} />} label={overwriteExisting ? 'Sobrescribir valores existentes' : 'Completar solo campos vacíos'} />
          {overwriteExisting && <Alert severity="warning">La vista previa indicará exactamente cuántos valores productivos se sobrescribirán.</Alert>}
        </Stack>
        {identificationError && <Alert severity="error" sx={{ mt: 2 }}>{identificationError}</Alert>}
        <Stack direction={{ xs: 'column', sm: 'row' }} alignItems={{ sm: 'center' }} gap={1} mt={2}>
          <Button variant="contained" disabled={!identificationFile || identificationBusy} onClick={() => void sendIdentification(false)}>Vista previa</Button>
          {identification?.canCommit && <Button color="success" variant="contained" disabled={identificationBusy || (overwriteExisting && !confirmOverwrite)} onClick={() => void sendIdentification(true)}>Confirmar identificación</Button>}
          {identificationBusy && <CircularProgress size={28} />}
        </Stack>
        {identification && <Box mt={3}>
          <Divider /><Typography variant="h2" mt={2}>Resultado de identificación</Typography>
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(145px,1fr))', gap: 1.5, my: 2 }}>
            <Metric label="Filas leídas" value={identification.rowsRead} /><Metric label="Coincidencias" value={identification.matched} />
            <Metric label="Sin coincidencia" value={identification.unmatched} /><Metric label="Duplicados" value={identification.duplicates} />
            <Metric label="Sin cambios" value={identification.unchanged} /><Metric label="Campos a completar" value={identification.missingFields} />
            <Metric label="Conflictos" value={identification.conflicts} /><Metric label="Fechas inválidas" value={identification.invalidDates} />
            <Metric label="Pasaportes duplicados" value={identification.duplicatePassports} /><Metric label="Pasajeros a actualizar" value={identification.willUpdate} />
            <Metric label="Valores a sobrescribir" value={identification.willOverwrite} />
            <Metric label="Pasaportes vencidos" value={identification.expiredPassports} />
            <Metric label="Vencen antes del regreso" value={identification.expiriesBeforeReturn} />
            <Metric label="Dentro del umbral" value={identification.expiriesWithinWarningThreshold} />
            <Metric label="Filas temporalmente incoherentes" value={identification.temporallyInconsistentRows} />
          </Box>
          {identification.selectedSheet&&<Alert severity="info">Hoja seleccionada: {identification.selectedSheet}. Control preventivo interno; verificar requisitos migratorios oficiales.</Alert>}
          {identification.blockingErrors > 0 && <Alert severity="error">Hay {identification.blockingErrors} errores bloqueantes. Corregí el archivo y repetí la vista previa.</Alert>}
          {overwriteExisting && identification.willOverwrite > 0 && <FormControlLabel sx={{ mt: 1 }} control={<Checkbox checked={confirmOverwrite} onChange={event => setConfirmOverwrite(event.target.checked)} />} label={`Confirmo administrativamente la sobrescritura de ${identification.willOverwrite} valores`} />}
          {identification.importRunId && <Alert severity="success" sx={{ mt: 1 }}>Importación confirmada. ID {identification.importRunId}</Alert>}
          <List dense>{identification.issues.map((issue, index) => <ListItem key={`${issue.row}-${issue.field}-${index}`}>
            <ListItemText primary={issue.message} secondary={`${issue.level}${issue.row ? ` · fila ${issue.row}` : ''}${issue.field ? ` · ${issue.field}` : ''}${issue.passportReference ? ` · ${issue.passportReference}` : ''}`} primaryTypographyProps={{ color: issue.level === 'Error' ? 'error.main' : 'warning.main' }} />
          </ListItem>)}</List>
        </Box>}
      </CardContent>
    </Card>

    <Card>
      <CardContent>
        <Typography variant="h2">Calidad de datos de identificación</Typography>
        {quality.error && <Alert severity="error" sx={{ mt: 2 }}>{quality.error.message}</Alert>}
        {quality.data && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(170px,1fr))', gap: 1.5, mt: 2 }}>
          <Metric label="Pasaportes completos" value={quality.data.completePassports} total={quality.data.totalPassengers} />
          <Metric label="Pasaportes incompletos" value={quality.data.incompletePassports} total={quality.data.totalPassengers} />
          <Metric label="Nacimientos cargados" value={quality.data.birthDates} total={quality.data.totalPassengers} />
          <Metric label="Nacionalidades cargadas" value={quality.data.nationalities} total={quality.data.totalPassengers} />
          <Metric label="Vencimientos cargados" value={quality.data.passportExpiries} total={quality.data.totalPassengers} />
          <Metric label="Pasaportes duplicados" value={quality.data.duplicatePassports} />
        </Box>}
      </CardContent>
    </Card>

    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1.2fr .8fr' }, gap: 3 }}>
      <Card><CardContent>
        <Typography variant="h2">Importar control maestro XLSX</Typography>
        <Button component="label" startIcon={<UploadFileIcon />} variant="outlined" sx={{ my: 2 }}>Elegir archivo<input hidden type="file" accept=".xlsx" onChange={event => { setFile(event.target.files?.[0]); setSummary(undefined) }} /></Button>
        {file && <Alert severity="info">{file.name} · {(file.size / 1024).toFixed(1)} KB</Alert>}{error && <Alert severity="error" sx={{ mt: 2 }}>{error}</Alert>}
        <Stack direction="row" gap={1} mt={2}><Button variant="contained" disabled={!file || busy} onClick={() => void sendMaster(false)}>Vista previa</Button>{summary?.canCommit && <Button color="success" variant="contained" disabled={busy} onClick={() => void sendMaster(true)}>Confirmar importación</Button>}{busy && <CircularProgress size={28} />}</Stack>
        {summary && <Box mt={3}><Divider /><Typography variant="h2" mt={2}>Resultado</Typography><Typography>{summary.passengerRows} pasajeros · {summary.roomRows} habitaciones</Typography><Typography>{summary.added} altas · {summary.updated} actualizaciones · {summary.unchanged} sin cambios · {summary.errors} errores</Typography>{summary.importRunId && <Alert severity="success" sx={{ mt: 1 }}>Importación confirmada. ID {summary.importRunId}</Alert>}<List>{summary.issues.map((issue, index) => <ListItem key={index}><ListItemText primary={issue.message} secondary={`${issue.level} · ${issue.sheet}${issue.row ? ` · fila ${issue.row}` : ''}`} primaryTypographyProps={{ color: issue.level === 'Error' ? 'error.main' : issue.level === 'Advertencia' ? 'warning.main' : 'text.primary' }} /></ListItem>)}</List></Box>}
      </CardContent></Card>
      <Card><CardContent><Typography variant="h2">Exportaciones</Typography><Typography color="text.secondary" mb={2}>Los archivos se generan desde la base de datos actual.</Typography><Stack spacing={1.5}><Button href="/api/exports/control.xlsx" startIcon={<DownloadIcon />} variant="contained">Descargar control XLSX</Button><Button href="/api/exports/passengers.csv" startIcon={<DownloadIcon />} variant="outlined">Pasajeros CSV</Button><Button href="/api/exports/pending.xlsx" startIcon={<DownloadIcon />} variant="outlined">Reporte de pendientes XLSX</Button><Button href="/api/exports/structured.json" startIcon={<DownloadIcon />} variant="outlined">Exportación estructurada JSON (admin)</Button></Stack><Alert severity="info" sx={{ mt: 3 }}>Incluye datos estructurados del viaje. No reemplaza el respaldo completo del servidor y no incluye archivos, claves ni configuración.</Alert></CardContent></Card>
    </Box>
  </Stack>
}

function Metric({ label, value, total }: { label: string; value: number; total?: number }) {
  return <Box role="group" aria-label={label} sx={{ p: 1.5, border: '1px solid', borderColor: 'divider', borderRadius: 2 }}><Typography variant="caption" color="text.secondary" fontWeight={800}>{label}</Typography><Typography variant="h5" fontWeight={900}>{value}{total !== undefined && <Typography component="span" color="text.secondary" fontSize=".9rem"> / {total}</Typography>}</Typography></Box>
}
