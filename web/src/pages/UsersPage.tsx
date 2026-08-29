import AddIcon from '@mui/icons-material/Add'
import { Alert, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, Switch, TextField, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, postJson, putJson } from '../api'
import { useAuth } from '../auth/AuthProvider'
import { ErrorState, LoadingState } from '../components/LoadingState'

interface ManagedUser {id:string;email:string;displayName:string;isActive:boolean;roles:string[];lockoutEnd?:string}
const roleLabels:Record<string,string>={Administrator:'Administrador',Editor:'Editor',Viewer:'Consulta'}
const roleLabel=(roles:string[])=>{const role=roles[0]??'Viewer';return roleLabels[role]??role}

export function UsersPage(){
 const auth=useAuth(),client=useQueryClient()
 const q=useQuery({queryKey:['users'],queryFn:()=>api<ManagedUser[]>('/api/users')})
 const [open,setOpen]=useState(false),[email,setEmail]=useState(''),[name,setName]=useState(''),[role,setRole]=useState('Viewer'),[password,setPassword]=useState('')
 const [reset,setReset]=useState<ManagedUser>(),[newPassword,setNewPassword]=useState('')
 const create=useMutation({mutationFn:()=>postJson('/api/users',{email,displayName:name,role,initialPassword:password}),onSuccess:async()=>{setOpen(false);setPassword('');await client.invalidateQueries({queryKey:['users']})}})
 const update=useMutation({mutationFn:({user,nextRole,nextActive}:{user:ManagedUser;nextRole:string;nextActive:boolean})=>putJson(`/api/users/${user.id}`,{displayName:user.displayName,role:nextRole,isActive:nextActive}),onSuccess:async()=>client.invalidateQueries({queryKey:['users']})})
 const resetPassword=useMutation({mutationFn:()=>postJson(`/api/users/${reset!.id}/reset-password`,{newPassword}),onSuccess:()=>{setReset(undefined);setNewPassword('')}})
 if(q.isLoading)return <LoadingState/>;if(q.error)return <ErrorState error={q.error}/>
 return <Stack spacing={3}>
  <Stack direction={{xs:'column',sm:'row'}} justifyContent="space-between" gap={2}><div><Typography variant="h1">Usuarios</Typography><Typography color="text.secondary">Roles, acceso y restablecimiento seguro de contraseñas. Siempre debe quedar un administrador activo.</Typography></div><Button variant="contained" startIcon={<AddIcon/>} onClick={()=>setOpen(true)}>Crear usuario</Button></Stack>
  {update.error&&<Alert severity="error">{update.error.message}</Alert>}
  {q.data!.map(u=><Card key={u.id}><CardContent><Stack direction={{xs:'column',md:'row'}} justifyContent="space-between" gap={2}><div><Typography variant="h2">{u.displayName}{u.id===auth.user?.id?' · tu cuenta':''}</Typography><Typography>{u.email}</Typography>{u.lockoutEnd&&new Date(u.lockoutEnd)>new Date()&&<Alert severity="warning" sx={{mt:1}}>Cuenta bloqueada temporalmente.</Alert>}</div><Stack direction={{xs:'column',sm:'row'}} alignItems={{sm:'center'}} gap={1}><TextField select label="Rol" value={u.roles[0]??'Viewer'} onChange={e=>update.mutate({user:u,nextRole:e.target.value,nextActive:u.isActive})} disabled={update.isPending} sx={{minWidth:180}}><MenuItem value="Administrator">Administrador</MenuItem><MenuItem value="Editor">Editor</MenuItem><MenuItem value="Viewer">Consulta</MenuItem></TextField><Typography>{u.isActive?'Activo':'Desactivado'}</Typography><Switch checked={u.isActive} disabled={update.isPending||u.id===auth.user?.id} onChange={()=>update.mutate({user:u,nextRole:u.roles[0]??'Viewer',nextActive:!u.isActive})} inputProps={{'aria-label':`Acceso de ${u.displayName}`}}/><Button onClick={()=>setReset(u)}>Restablecer clave</Button></Stack></Stack><Typography variant="caption" color="text.secondary">Rol actual: {roleLabel(u.roles)}</Typography></CardContent></Card>)}
  <Dialog open={open} onClose={()=>setOpen(false)}><DialogTitle>Crear usuario</DialogTitle><DialogContent><Stack spacing={2} mt={1}><TextField label="Nombre visible" value={name} onChange={e=>setName(e.target.value)}/><TextField label="Correo" type="email" value={email} onChange={e=>setEmail(e.target.value)}/><TextField select label="Rol" value={role} onChange={e=>setRole(e.target.value)}><MenuItem value="Administrator">Administrador</MenuItem><MenuItem value="Editor">Editor</MenuItem><MenuItem value="Viewer">Consulta</MenuItem></TextField><TextField label="Contraseña inicial" type="password" value={password} onChange={e=>setPassword(e.target.value)} helperText="Mínimo 12 caracteres, mayúscula, minúscula, número y símbolo."/>{create.error&&<Alert severity="error">{create.error.message}</Alert>}</Stack></DialogContent><DialogActions><Button onClick={()=>setOpen(false)}>Cancelar</Button><Button variant="contained" disabled={!email||!name||password.length<12||create.isPending} onClick={()=>create.mutate()}>Crear</Button></DialogActions></Dialog>
  <Dialog open={!!reset} onClose={()=>setReset(undefined)}><DialogTitle>Restablecer contraseña</DialogTitle><DialogContent><Alert severity="warning" sx={{my:1}}>La clave no se mostrará ni quedará registrada en auditoría.</Alert><TextField label="Nueva contraseña" type="password" value={newPassword} onChange={e=>setNewPassword(e.target.value)}/>{resetPassword.error&&<Alert severity="error" sx={{mt:2}}>{resetPassword.error.message}</Alert>}</DialogContent><DialogActions><Button onClick={()=>setReset(undefined)}>Cancelar</Button><Button variant="contained" disabled={newPassword.length<12||resetPassword.isPending} onClick={()=>resetPassword.mutate()}>Restablecer</Button></DialogActions></Dialog>
 </Stack>
}
