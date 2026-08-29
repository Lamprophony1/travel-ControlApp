import { Box, CircularProgress } from '@mui/material'
import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes, useLocation, useParams } from 'react-router-dom'
import { useAuth } from './auth/AuthProvider'
import { LoginPage, SetupPage } from './auth/AuthPages'
import { AppLayout } from './layout/AppLayout'
import { PublicLayout } from './layout/PublicLayout'

const PublicDashboardPage=lazy(()=>import('./pages/PublicDashboardPage').then(m=>({default:m.PublicDashboardPage})))
const PublicPassengersPage=lazy(()=>import('./pages/PublicPassengersPage').then(m=>({default:m.PublicPassengersPage})))
const PublicPassengerDetailPage=lazy(()=>import('./pages/PublicPassengerDetailPage').then(m=>({default:m.PublicPassengerDetailPage})))
const DashboardPage=lazy(()=>import('./pages/DashboardPage').then(m=>({default:m.DashboardPage})))
const PassengersPage=lazy(()=>import('./pages/PassengersPage').then(m=>({default:m.PassengersPage})))
const PassengerDetailPage=lazy(()=>import('./pages/PassengerDetailPage').then(m=>({default:m.PassengerDetailPage})))
const RoomsPage=lazy(()=>import('./pages/RoomsPage').then(m=>({default:m.RoomsPage})))
const FlightsPage=lazy(()=>import('./pages/OperationsPages').then(m=>({default:m.FlightsPage})))
const BaggagePage=lazy(()=>import('./pages/OperationsPages').then(m=>({default:m.BaggagePage})))
const PendingPage=lazy(()=>import('./pages/PendingPage').then(m=>({default:m.PendingPage})))
const ImportExportPage=lazy(()=>import('./pages/ImportExportPage').then(m=>({default:m.ImportExportPage})))
const AuditPage=lazy(()=>import('./pages/AuditPage').then(m=>({default:m.AuditPage})))
const UsersPage=lazy(()=>import('./pages/UsersPage').then(m=>({default:m.UsersPage})))

function Protected(){const auth=useAuth();if(auth.loading)return <Box sx={{minHeight:'100vh',display:'grid',placeItems:'center'}}><CircularProgress/></Box>;if(auth.setupRequired)return <Navigate to="/setup" replace/>;return auth.user?<AppLayout/>:<Navigate to="/login" replace/>}
function LegacyRedirect({to}:{to:string}){const location=useLocation();return <Navigate to={`${to}${location.search}`} replace/>}
function LegacyPassengerRedirect(){const {id}=useParams();return <Navigate to={`/gestion/pasajeros/${id}`} replace/>}
const deferred=(page:React.ReactNode)=><Suspense fallback={<Box sx={{minHeight:320,display:'grid',placeItems:'center'}}><CircularProgress/></Box>}>{page}</Suspense>
export function App(){return <Routes>
  <Route element={<PublicLayout/>}><Route index element={deferred(<PublicDashboardPage/>)}/><Route path="pasajeros" element={deferred(<PublicPassengersPage/>)}/><Route path="pasajeros/:id" element={deferred(<PublicPassengerDetailPage/>)}/></Route>
  <Route path="/login" element={<LoginPage/>}/><Route path="/setup" element={<SetupPage/>}/>
  <Route path="/gestion" element={<Protected/>}><Route index element={deferred(<DashboardPage/>)}/><Route path="pasajeros" element={deferred(<PassengersPage/>)}/><Route path="pasajeros/:id" element={deferred(<PassengerDetailPage/>)}/><Route path="habitaciones" element={deferred(<RoomsPage/>)}/><Route path="vuelos" element={deferred(<FlightsPage/>)}/><Route path="equipaje" element={deferred(<BaggagePage/>)}/><Route path="pendientes" element={deferred(<PendingPage/>)}/><Route path="importar" element={deferred(<ImportExportPage/>)}/><Route path="auditoria" element={deferred(<AuditPage/>)}/><Route path="usuarios" element={deferred(<UsersPage/>)}/></Route>
  <Route path="/passengers" element={<LegacyRedirect to="/gestion/pasajeros"/>}/><Route path="/passengers/:id" element={<LegacyPassengerRedirect/>}/><Route path="/rooms" element={<LegacyRedirect to="/gestion/habitaciones"/>}/><Route path="/flights" element={<LegacyRedirect to="/gestion/vuelos"/>}/><Route path="/baggage" element={<LegacyRedirect to="/gestion/equipaje"/>}/><Route path="/pending" element={<LegacyRedirect to="/gestion/pendientes"/>}/><Route path="/import" element={<LegacyRedirect to="/gestion/importar"/>}/><Route path="/users" element={<LegacyRedirect to="/gestion/usuarios"/>}/><Route path="/audit" element={<LegacyRedirect to="/gestion/auditoria"/>}/>
  <Route path="*" element={<Navigate to="/" replace/>}/>
</Routes>}
