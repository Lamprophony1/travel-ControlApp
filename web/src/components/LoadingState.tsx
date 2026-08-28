import { Alert, Box, Button, Skeleton, Stack } from '@mui/material'

export function LoadingState() { return <Stack spacing={2} aria-label="Cargando"><Skeleton height={80}/><Skeleton height={180}/><Skeleton height={180}/></Stack> }
export function ErrorState({ error, retry }: { error: Error; retry?: () => void }) { return <Box><Alert severity="error" action={retry && <Button color="inherit" onClick={retry}>Reintentar</Button>}>{error.message}</Alert></Box> }

