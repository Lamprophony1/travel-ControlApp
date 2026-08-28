import { createTheme } from '@mui/material/styles'

export const theme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: '#12304a', dark: '#0a2236', light: '#31536e' },
    secondary: { main: '#009b9d', light: '#52d2cf', dark: '#007476' },
    success: { main: '#2e7d5b', light: '#e7f5ee' },
    warning: { main: '#b36b00', light: '#fff4d6' },
    error: { main: '#b94a48', light: '#fdebea' },
    background: { default: '#f4f8fb', paper: '#ffffff' }
  },
  typography: { fontFamily: 'Inter, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif', h1: { fontSize: 'clamp(1.7rem, 3vw, 2.4rem)', fontWeight: 800 }, h2: { fontSize: '1.35rem', fontWeight: 750 } },
  shape: { borderRadius: 14 },
  components: {
    MuiButton: { styleOverrides: { root: { minHeight: 44, textTransform: 'none', fontWeight: 700 } } },
    MuiChip: { styleOverrides: { root: { fontWeight: 700 } } },
    MuiCard: { styleOverrides: { root: { border: '1px solid #dce8ef', boxShadow: '0 6px 20px rgba(18,48,74,.06)' } } }
  }
})

