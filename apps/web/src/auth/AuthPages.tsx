import { zodResolver } from '@hookform/resolvers/zod'
import LockOutlinedIcon from '@mui/icons-material/LockOutlined'
import { Alert, Avatar, Box, Button, Card, CardContent, Checkbox, FormControlLabel, Stack, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Navigate } from 'react-router-dom'
import { z } from 'zod'
import { ApiError, postJson } from '../api'
import { useAuth } from './AuthProvider'

const loginSchema = z.object({ email: z.email('Ingresá un correo válido.'), password: z.string().min(1, 'Ingresá tu contraseña.'), rememberMe: z.boolean() })
type LoginForm = z.infer<typeof loginSchema>
const setupSchema = z.object({ displayName: z.string().min(2), email: z.email(), password: z.string().min(12, 'Usá al menos 12 caracteres.').regex(/[^a-zA-Z0-9]/, 'Incluí un símbolo.') })
type SetupForm = z.infer<typeof setupSchema>

function Shell({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  return <Box sx={{ minHeight:'100vh', display:'grid', placeItems:'center', p:2, background:'linear-gradient(145deg,#12304a 0 45%,#e8f7f7 45%)' }}><Card sx={{ width:'100%', maxWidth:460 }}><CardContent sx={{ p:{xs:3,sm:5} }}><Stack spacing={3} alignItems="stretch"><Avatar sx={{ bgcolor:'secondary.main', mx:'auto', width:56, height:56 }}><LockOutlinedIcon/></Avatar><Box textAlign="center"><Typography variant="h1" sx={{fontSize:'1.65rem'}}>{title}</Typography><Typography color="text.secondary" mt={1}>{subtitle}</Typography></Box>{children}</Stack></CardContent></Card></Box>
}

export function LoginPage() {
  const auth = useAuth(); const [error, setError] = useState('');
  const { register, handleSubmit, formState:{errors,isSubmitting} } = useForm<LoginForm>({ resolver:zodResolver(loginSchema), defaultValues:{rememberMe:false} })
  if (auth.setupRequired) return <Navigate to="/setup" replace/>; if (auth.user) return <Navigate to="/" replace/>
  return <Shell title="Control de Viaje" subtitle="Boda Cielito & Ronaldo · Riviera Maya 2026"><Box component="form" onSubmit={handleSubmit(async v => { try { setError(''); await auth.login(v.email,v.password,v.rememberMe) } catch(e) { setError(e instanceof Error ? e.message : 'No se pudo iniciar sesión.') } })}><Stack spacing={2}>{error && <Alert severity="error">{error}</Alert>}<TextField label="Correo" autoComplete="username" {...register('email')} error={!!errors.email} helperText={errors.email?.message}/><TextField label="Contraseña" type="password" autoComplete="current-password" {...register('password')} error={!!errors.password} helperText={errors.password?.message}/><FormControlLabel control={<Checkbox {...register('rememberMe')}/>} label="Mantener la sesión en este dispositivo"/><Button type="submit" variant="contained" disabled={isSubmitting}>Ingresar</Button></Stack></Box></Shell>
}

export function SetupPage() {
  const auth = useAuth(); const [done,setDone]=useState(false); const [error,setError]=useState('');
  const { register, handleSubmit, formState:{errors,isSubmitting} } = useForm<SetupForm>({ resolver:zodResolver(setupSchema) })
  if (!auth.loading && !auth.setupRequired && !done) return <Navigate to="/login" replace/>
  return <Shell title="Crear primer administrador" subtitle="Este paso solo estará disponible una vez."><Box component="form" onSubmit={handleSubmit(async v=>{try{setError('');await postJson('/api/auth/setup',v);setDone(true);await auth.refresh()}catch(e){setError(e instanceof ApiError?e.message:'No se pudo crear el administrador.')}})}><Stack spacing={2}>{done&&<Alert severity="success">Administrador creado. Ya podés iniciar sesión.</Alert>}{error&&<Alert severity="error">{error}</Alert>}<TextField label="Nombre visible" {...register('displayName')} error={!!errors.displayName} helperText={errors.displayName?.message}/><TextField label="Correo" {...register('email')} error={!!errors.email} helperText={errors.email?.message}/><TextField label="Contraseña" type="password" {...register('password')} error={!!errors.password} helperText={errors.password?.message ?? 'Mínimo 12 caracteres, con mayúscula, minúscula, número y símbolo.'}/><Button type="submit" variant="contained" disabled={isSubmitting||done}>Crear administrador</Button>{done&&<Button href="/login">Ir al inicio de sesión</Button>}</Stack></Box></Shell>
}
