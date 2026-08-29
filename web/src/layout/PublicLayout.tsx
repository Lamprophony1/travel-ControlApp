import AdminPanelSettingsOutlinedIcon from '@mui/icons-material/AdminPanelSettingsOutlined'
import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined'
import PeopleOutlineIcon from '@mui/icons-material/PeopleOutline'
import ShareOutlinedIcon from '@mui/icons-material/ShareOutlined'
import { AppBar, Box, BottomNavigation, BottomNavigationAction, Button, Chip, Container, IconButton, Snackbar, Stack, Toolbar, Typography, useMediaQuery, useTheme } from '@mui/material'
import { useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthProvider'
import { UpdatePrompt } from './UpdatePrompt'

const publicItems=[{path:'/',label:'Dashboard',icon:<DashboardOutlinedIcon/>},{path:'/pasajeros',label:'Pasajeros',icon:<PeopleOutlineIcon/>}]

export function PublicLayout(){
  const auth=useAuth(),location=useLocation(),navigate=useNavigate(),theme=useTheme(),mobile=useMediaQuery(theme.breakpoints.down('sm'))
  const [toast,setToast]=useState('')
  const administer=auth.setupRequired?'/setup':auth.user?'/gestion':'/login'
  const share=async()=>{const url=window.location.origin+location.pathname+location.search;try{if(navigator.share)await navigator.share({title:'Control de Viaje',url});else{await navigator.clipboard.writeText(url);setToast('Enlace copiado')}}catch(e){if((e as DOMException).name!=='AbortError')setToast('No se pudo compartir el enlace')}}
  return <Box sx={{minHeight:'100dvh',pb:{xs:'calc(78px + env(safe-area-inset-bottom))',sm:0}}}>
    <a className="skip-link" href="#contenido">Saltar al contenido</a>
    <AppBar position="sticky" elevation={0} color="inherit" sx={{borderBottom:'1px solid',borderColor:'divider'}}><Toolbar sx={{minHeight:{xs:72,sm:80},gap:{xs:1,sm:2}}}><Box sx={{flex:1,minWidth:0}}><Typography fontWeight={900} color="primary.main" noWrap>Control de Viaje</Typography><Typography variant="body2" color="text.secondary" noWrap>Boda Cielito &amp; Ronaldo</Typography></Box>{!mobile&&<Stack direction="row" alignItems="center" spacing={1}>{publicItems.map(item=><Button key={item.path} color={location.pathname===item.path?'secondary':'primary'} startIcon={item.icon} onClick={()=>navigate(item.path)}>{item.label}</Button>)}<Button startIcon={<ShareOutlinedIcon/>} onClick={()=>void share()}>Compartir enlace</Button></Stack>}{mobile&&<IconButton aria-label="Compartir enlace" onClick={()=>void share()} sx={{minWidth:44,minHeight:44}}><ShareOutlinedIcon/></IconButton>}<Button variant="contained" startIcon={<AdminPanelSettingsOutlinedIcon/>} disabled={auth.loading} onClick={()=>navigate(administer)} sx={{px:{xs:1.5,sm:2}}}>Administrar</Button></Toolbar></AppBar>
    <Container component="main" id="contenido" tabIndex={-1} maxWidth="xl" sx={{py:{xs:3,md:5}}}><Stack spacing={2} mb={3}><Chip label="Vista de consulta · Solo lectura" color="info" variant="outlined" sx={{alignSelf:'flex-start'}}/><Typography variant="body2" color="text.secondary">Los datos documentales y comprobantes están protegidos. Esta vista presenta únicamente el estado operativo del viaje.</Typography></Stack><Outlet/></Container>
    {mobile&&<BottomNavigation showLabels value={location.pathname.startsWith('/pasajeros')?1:0} onChange={(_,value)=>navigate(publicItems[value]?.path??'/')} sx={{position:'fixed',bottom:0,left:0,right:0,zIndex:1200,height:'calc(68px + env(safe-area-inset-bottom))',pb:'env(safe-area-inset-bottom)',borderTop:'1px solid',borderColor:'divider'}}>{publicItems.map(item=><BottomNavigationAction key={item.path} label={item.label} icon={item.icon}/>)}</BottomNavigation>}
    <Snackbar open={!!toast} autoHideDuration={2500} onClose={()=>setToast('')} message={toast}/><UpdatePrompt/>
  </Box>
}
