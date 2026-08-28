import { CssBaseline, ThemeProvider } from '@mui/material'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import { AuthProvider } from './auth/AuthProvider'
import { theme } from './theme'
import './styles.css'

const queryClient=new QueryClient({defaultOptions:{queries:{staleTime:15_000,retry:(count,error)=>!(error instanceof Error&&'status'in error&&(error as {status:number}).status===401)&&count<2}}})
createRoot(document.getElementById('root')!).render(<StrictMode><ThemeProvider theme={theme}><CssBaseline/><QueryClientProvider client={queryClient}><BrowserRouter><AuthProvider><App/></AuthProvider></BrowserRouter></QueryClientProvider></ThemeProvider></StrictMode>)

