import { Button, Snackbar } from '@mui/material'
import { useRegisterSW } from 'virtual:pwa-register/react'

export function UpdatePrompt() {
  const { needRefresh:[needRefresh,setNeedRefresh], updateServiceWorker }=useRegisterSW()
  return <Snackbar open={needRefresh} message="Hay una nueva versión disponible." action={<><Button color="secondary" onClick={()=>void updateServiceWorker(true)}>Actualizar</Button><Button color="inherit" onClick={()=>setNeedRefresh(false)}>Después</Button></>}/>
}

