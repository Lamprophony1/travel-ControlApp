import AirplanemodeActiveIcon from '@mui/icons-material/AirplanemodeActive'
import AssignmentLateIcon from '@mui/icons-material/AssignmentLate'
import DashboardIcon from '@mui/icons-material/Dashboard'
import HotelIcon from '@mui/icons-material/Hotel'
import HistoryIcon from '@mui/icons-material/History'
import LuggageIcon from '@mui/icons-material/Luggage'
import MenuIcon from '@mui/icons-material/Menu'
import PeopleIcon from '@mui/icons-material/People'
import SwapHorizIcon from '@mui/icons-material/SwapHoriz'
import UploadFileIcon from '@mui/icons-material/UploadFile'
import { AppBar, Box, BottomNavigation, BottomNavigationAction, Button, Divider, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText, Toolbar, Typography, useMediaQuery, useTheme } from '@mui/material'
import { useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthProvider'
import { UpdatePrompt } from './UpdatePrompt'

const items = [
  { path:'/', label:'Dashboard', icon:<DashboardIcon/> }, { path:'/passengers',label:'Pasajeros',icon:<PeopleIcon/> },
  { path:'/rooms',label:'Habitaciones',icon:<HotelIcon/> }, { path:'/flights',label:'Vuelos',icon:<AirplanemodeActiveIcon/> },
  { path:'/baggage',label:'Equipaje',icon:<LuggageIcon/> }, { path:'/transfers',label:'Transfers',icon:<SwapHorizIcon/> },
  { path:'/pending',label:'Pendientes',icon:<AssignmentLateIcon/> }, { path:'/import',label:'Importar / exportar',icon:<UploadFileIcon/>,admin:true },
  { path:'/audit',label:'Auditoría',icon:<HistoryIcon/>,admin:true }
]

export function AppLayout() {
  const [open,setOpen]=useState(false); const theme=useTheme(); const mobile=useMediaQuery(theme.breakpoints.down('md')); const location=useLocation(); const navigate=useNavigate(); const auth=useAuth();
  const allowedItems=items.filter(item=>!item.admin||auth.user?.roles.includes('Administrator'))
  const nav=<Box sx={{width:270}} role="navigation"><Toolbar><Typography fontWeight={850} color="primary">Control de Viaje</Typography></Toolbar><Divider/><List>{allowedItems.map(item=><ListItemButton key={item.path} selected={location.pathname===item.path} onClick={()=>{navigate(item.path);setOpen(false)}}><ListItemIcon>{item.icon}</ListItemIcon><ListItemText primary={item.label}/></ListItemButton>)}</List></Box>
  const bottom=allowedItems.slice(0,5)
  return <Box sx={{display:'flex',minHeight:'100vh'}}><AppBar position="fixed" elevation={0} sx={{zIndex:t=>t.zIndex.drawer+1}}><Toolbar>{mobile&&<IconButton color="inherit" edge="start" aria-label="Abrir menú" onClick={()=>setOpen(true)}><MenuIcon/></IconButton>}<Box sx={{flex:1}}><Typography fontWeight={800}>Boda Cielito & Ronaldo</Typography><Typography variant="caption" sx={{opacity:.8}}>Riviera Maya · Septiembre 2026</Typography></Box><Typography variant="body2" sx={{display:{xs:'none',sm:'block'},mr:2}}>{auth.user?.displayName}</Typography><Button color="inherit" onClick={()=>void auth.logout()}>Salir</Button></Toolbar></AppBar>{mobile?<Drawer open={open} onClose={()=>setOpen(false)} ModalProps={{keepMounted:true}}>{nav}</Drawer>:<Drawer variant="permanent" sx={{width:270,flexShrink:0,'& .MuiDrawer-paper':{width:270,boxSizing:'border-box'}}}>{nav}</Drawer>}<Box component="main" sx={{flex:1,minWidth:0,p:{xs:2,sm:3},pt:{xs:11,sm:12},pb:{xs:12,md:4}}}><Outlet/></Box>{mobile&&<BottomNavigation showLabels value={bottom.findIndex(x=>x.path===location.pathname)} onChange={(_,v)=>navigate(bottom[v]?.path??'/')} sx={{position:'fixed',bottom:0,left:0,right:0,zIndex:1200,borderTop:'1px solid #dce8ef',height:72}}>{bottom.map(x=><BottomNavigationAction key={x.path} label={x.label} icon={x.icon}/>)}</BottomNavigation>}<UpdatePrompt/></Box>
}
