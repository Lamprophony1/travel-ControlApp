import { render, screen } from '@testing-library/react'
import { ThemeProvider } from '@mui/material'
import { describe, expect, it } from 'vitest'
import { StatusChip } from './StatusChip'
import { theme } from '../theme'
describe('StatusChip',()=>{it('muestra el estado en español y no depende solo del color',()=>{render(<ThemeProvider theme={theme}><StatusChip status="NotIncluded"/></ThemeProvider>);expect(screen.getByText('No incluido')).toBeInTheDocument();expect(screen.getByTestId('ErrorOutlineIcon')).toBeInTheDocument()})})

