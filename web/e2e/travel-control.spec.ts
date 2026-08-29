import { test, expect, type Page, type TestInfo } from '@playwright/test'

const email='admin@example.test',password='Test-only-Password!2026'
const forbidden=['passportNumber','maskedPassport','birthDate','nationality','passportExpiry','phone','email','dietaryRestrictions','notes','nextAction','nextActionDueDate','pnr','electronicTicketNumber','sourceReference','operatorContact','attachments','followUps','audit','updatedBy','userName']

async function noOverflow(page:Page){expect(await page.evaluate(()=>document.documentElement.scrollWidth>document.documentElement.clientWidth)).toBeFalsy()}
async function enterManagement(page:Page){
  await page.goto('/gestion')
  await expect(page.getByRole('heading',{name:/^(Estado del viaje|Crear primer administrador|Control de Viaje)$/})).toBeVisible()
  if(page.url().includes('/setup')){
    await page.getByLabel('Nombre visible').fill('Administrador de prueba')
    await page.getByLabel('Correo').fill(email)
    await page.getByLabel('Contraseña').fill(password)
    await page.getByRole('button',{name:'Crear administrador'}).click()
    await expect(page.getByText(/Administrador creado/)).toBeVisible()
    await page.goto('/login')
    await page.waitForURL(/\/login$/)
  }
  if(page.url().includes('/login')){
    await page.getByLabel('Correo').fill(email)
    await page.getByLabel('Contraseña').fill(password)
    await page.getByRole('button',{name:'Ingresar'}).click()
  }
  await expect(page).toHaveURL(/\/gestion/)
  await expect(page.getByRole('heading',{name:'Estado del viaje'})).toBeVisible()
}

test('consulta pública, navegación responsive y PWA sin login',async({page})=>{
  await page.goto('/')
  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByRole('heading',{name:'Estado del viaje'})).toBeVisible()
  await expect(page.getByText('Vista de consulta · Solo lectura')).toBeVisible()
  await expect(page.getByText('Todavía faltan entregables para cerrar el viaje')).toBeVisible()
  await expect(page.getByRole('button',{name:'Administrar'})).toBeVisible()
  await expect(page.getByRole('button',{name:'Compartir enlace'})).toBeVisible()
  await noOverflow(page)
  const manifest=await page.request.get('/manifest.webmanifest')
  expect(manifest.ok()).toBeTruthy()
  expect((await manifest.json()).start_url).toBe('/')
  await page.goto('/pasajeros')
  await expect(page.getByRole('heading',{name:'Pasajeros'})).toBeVisible()
  await noOverflow(page)
})

test('gestión protegida y detalle público no expone datos sensibles',async({page},testInfo:TestInfo)=>{
  await enterManagement(page)
  const csrfResponse=await page.request.get('/api/auth/csrf')
  const csrf=(await csrfResponse.json()).token as string
  const name=`Persona ficticia ${testInfo.project.name}`
  const create=await page.request.post('/api/passengers',{headers:{'X-XSRF-TOKEN':csrf},data:{fullName:name,birthDate:'1990-01-01',nationality:'Ficticia',passportNumber:`SECRET-${testInfo.project.name}`,passportExpiry:'2030-01-01',phone:'000-SECRET',email:'fixture@example.test',primaryOperatorId:null,roomReservationId:null,nextAction:'Dato interno',nextActionDueDate:null,dietaryRestrictions:'Dato interno',notes:'Dato interno'}})
  expect([201,409]).toContain(create.status())
  await page.goto(`/pasajeros?search=${encodeURIComponent(name)}`)
  const visibleName=page.getByText(name,{exact:true}).filter({visible:true})
  await expect(visibleName).toBeVisible()
  await visibleName.click()
  await expect(page.getByRole('heading',{name})).toBeVisible()
  await expect(page.getByText(`SECRET-${testInfo.project.name}`)).toHaveCount(0)
  await expect(page.getByText('000-SECRET')).toHaveCount(0)
  await expect(page.getByText('fixture@example.test')).toHaveCount(0)
  await expect(page.getByText('PNR',{exact:true})).toHaveCount(0)
  await noOverflow(page)
  const response=await page.request.get(`/api/public/passengers?search=${encodeURIComponent(name)}`)
  expect(response.ok()).toBeTruthy()
  const json=JSON.stringify(await response.json())
  for(const key of forbidden)expect(json.toLowerCase()).not.toContain(`"${key.toLowerCase()}"`)
  const privateResponse=await page.request.get('/api/passengers?page=1&pageSize=1')
  expect(privateResponse.status()).toBe(200)
  await page.goto('/gestion/pasajeros')
  await expect(page.getByRole('heading',{name:'Pasajeros'})).toBeVisible()
  await noOverflow(page)
})

test('rutas privadas sin cookie devuelven 401',async({request})=>{
  expect((await request.get('/api/dashboard')).status()).toBe(401)
  expect((await request.get('/api/passengers?page=1&pageSize=1')).status()).toBe(401)
  expect((await request.post('/api/baggage',{data:{}})).status()).toBe(401)
})
