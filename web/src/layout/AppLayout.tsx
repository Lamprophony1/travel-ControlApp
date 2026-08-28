import AirplanemodeActiveIcon from '@mui/icons-material/AirplanemodeActive'
import AssignmentLateIcon from '@mui/icons-material/AssignmentLate'
import DashboardIcon from '@mui/icons-material/Dashboard'
import HistoryIcon from '@mui/icons-material/History'
import HotelIcon from '@mui/icons-material/Hotel'
import LuggageIcon from '@mui/icons-material/Luggage'
import MenuIcon from '@mui/icons-material/Menu'
import PeopleIcon from '@mui/icons-material/People'
import UploadFileIcon from '@mui/icons-material/UploadFile'
import ManageAccountsIcon from '@mui/icons-material/ManageAccounts'
import { AppBar, Box, BottomNavigation, BottomNavigationAction, Button, Divider, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText, Toolbar, Typography, useMediaQuery, useTheme } from '@mui/material'
import { useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthProvider'
import { UpdatePrompt } from './UpdatePrompt'

const items = [
  {path:'/',label:'Dashboard',icon:<DashboardIcon/>},{path:'/passengers',label:'Pasajeros',icon:<PeopleIcon/>},
  {path:'/rooms',label:'Habitaciones',icon:<HotelIcon/>},{path:'/flights',label:'Vuelos',icon:<AirplanemodeActiveIcon/>},
  {path:'/baggage',label:'Equipaje',icon:<LuggageIcon/>},{path:'/pending',label:'Pendientes',icon:<AssignmentLateIcon/>},
  {path:'/import',label:'Importar / exportar',icon:<UploadFileIcon/>,admin:true},{path:'/users',label:'Usuarios',icon:<ManageAccountsIcon/>,admin:true},
  {path:'/audit',label:'Auditoría',icon:<HistoryIcon/>,admin:true}
]

export function AppLayout(){
  const [open,setOpen]=useState(false);const theme=useTheme();const mobile=useMediaQuery(theme.breakpoints.down('md'));const location=useLocation();const navigate=useNavigate();const auth=useAuth()
  const allowed=items.filter(x=>!x.admin||auth.user?.roles.includes('Administrator'));const go=(path:string)=>{navigate(path);setOpen(false)}
  const nav=<Box sx={{width:280,pb:'env(safe-area-inset-bottom)'}} role="navigation"><Toolbar><Typography fontWeight={850} color="primary">Control de Viaje</Typography></Toolbar><Divider/><List>{allowed.map(x=><ListItemButton key={x.path} selected={location.pathname===x.path} onClick={()=>go(x.path)} sx={{minHeight:48}}><ListItemIcon>{x.icon}</ListItemIcon><ListItemText primary={x.label}/></ListItemButton>)}</List></Box>
  const bottom=allowed.filter(x=>['/','/passengers','/rooms','/pending'].includes(x.path))
  return <Box sx={{display:'flex',minHeight:'100dvh'}}><AppBar position="fixed" elevation={0} sx={{zIndex:t=>t.zIndex.drawer+1}}><Toolbar sx={{minHeight:{xs:64,sm:72}}}>{mobile&&<IconButton color="inherit" edge="start" aria-label="Abrir menú" onClick={()=>setOpen(true)} sx={{minWidth:44,minHeight:44}}><MenuIcon/></IconButton>}<Box sx={{flex:1,minWidth:0}}><Typography fontWeight={800} noWrap>Viaje grupal</Typography><Typography variant="caption" sx={{opacity:.82}} noWrap>Panel operativo privado</Typography></Box><Typography variant="body2" sx={{display:{xs:'none',sm:'block'},mr:2}}>{auth.user?.displayName}</Typography><Button color="inherit" onClick={()=>void auth.logout()} sx={{minHeight:44}}>Salir</Button></Toolbar></AppBar>{mobile?<Drawer open={open} onClose={()=>setOpen(false)} ModalProps={{keepMounted:true}}>{nav}</Drawer>:<Drawer variant="permanent" sx={{width:280,flexShrink:0,'& .MuiDrawer-paper':{width:280,boxSizing:'border-box'}}}>{nav}</Drawer>}<Box component="main" sx={{flex:1,minWidth:0,p:{xs:2,sm:3},pt:{xs:10,sm:12},pb:{xs:'calc(88px + env(safe-area-inset-bottom))',md:4}}}><Outlet/></Box>{mobile&&<BottomNavigation showLabels value={bottom.findIndex(x=>x.path===location.pathname)} onChange={(_,v)=>go(bottom[v]?.path??'/')} sx={{position:'fixed',bottom:0,left:0,right:0,zIndex:1200,borderTop:'1px solid',borderColor:'divider',height:'calc(70px + env(safe-area-inset-bottom))',pb:'env(safe-area-inset-bottom)'}}>{bottom.map(x=><BottomNavigationAction key={x.path} label={x.label} icon={x.icon} sx={{minWidth:44,minHeight:44}}/>)}</BottomNavigation>}<UpdatePrompt/></Box>
}
