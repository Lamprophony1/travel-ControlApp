import { CircularProgress, Box } from '@mui/material'
import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { useAuth } from './auth/AuthProvider'
import { LoginPage, SetupPage } from './auth/AuthPages'
import { AppLayout } from './layout/AppLayout'

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
const deferred=(page:React.ReactNode)=><Suspense fallback={<Box sx={{minHeight:320,display:'grid',placeItems:'center'}}><CircularProgress/></Box>}>{page}</Suspense>
export function App(){return <Routes><Route path="/login" element={<LoginPage/>}/><Route path="/setup" element={<SetupPage/>}/><Route element={<Protected/>}><Route index element={deferred(<DashboardPage/>)}/><Route path="passengers" element={deferred(<PassengersPage/>)}/><Route path="passengers/:id" element={deferred(<PassengerDetailPage/>)}/><Route path="rooms" element={deferred(<RoomsPage/>)}/><Route path="flights" element={deferred(<FlightsPage/>)}/><Route path="baggage" element={deferred(<BaggagePage/>)}/><Route path="pending" element={deferred(<PendingPage/>)}/><Route path="import" element={deferred(<ImportExportPage/>)}/><Route path="audit" element={deferred(<AuditPage/>)}/><Route path="users" element={deferred(<UsersPage/>)}/></Route><Route path="*" element={<Navigate to="/" replace/>}/></Routes>}
