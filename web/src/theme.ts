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
  typography: { fontFamily: 'Inter, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif', fontSize: 16, h1: { fontSize: 'clamp(1.55rem, 2.4vw, 1.75rem)', fontWeight: 850 }, h2: { fontSize: '1.3rem', fontWeight: 750 } },
  shape: { borderRadius: 14 },
  components: {
    MuiButton: { styleOverrides: { root: { minHeight: 44, textTransform: 'none', fontWeight: 700 } } },
    MuiChip: { styleOverrides: { root: { fontWeight: 700 } } },
    MuiCard: { styleOverrides: { root: { border: '1px solid #dce8ef', boxShadow: '0 6px 20px rgba(18,48,74,.06)' } } }
  }
})
