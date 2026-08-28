import { test, expect } from '@playwright/test'

const email='admin@example.test', password='Test-only-Password!2026'
async function enter(page:import('@playwright/test').Page){
  await page.goto('/')
  const setup=page.getByRole('heading',{name:'Crear primer administrador'})
  await expect(page.getByRole('heading').first()).toBeVisible()
  if(await setup.isVisible()){
    await page.getByLabel('Nombre visible').fill('Administrador de prueba')
    await page.getByLabel('Correo').fill(email);await page.getByLabel('Contraseña').fill(password)
    await page.getByRole('button',{name:'Crear administrador'}).click();await expect(page.getByText(/Administrador creado/)).toBeVisible();await page.goto('/login')
  }
  if(await page.getByLabel('Correo').isVisible()){await page.getByLabel('Correo').fill(email);await page.getByLabel('Contraseña').fill(password);await page.getByRole('button',{name:'Ingresar'}).click()}
  await expect(page.getByRole('heading',{name:'Estado del viaje'})).toBeVisible()
}

test('sesión, dashboard móvil y navegación sin overflow',async({page})=>{
  await enter(page);await expect(page.getByText('Transfer grupal',{exact:true})).toBeVisible()
  expect(await page.evaluate(()=>document.documentElement.scrollWidth>document.documentElement.clientWidth)).toBeFalsy()
  await page.goto('/passengers');await expect(page.getByRole('heading',{name:'Pasajeros'})).toBeVisible()
  await page.goto('/rooms');await expect(page.getByRole('heading',{name:'Habitaciones'})).toBeVisible()
  await page.goto('/flights');await expect(page.getByRole('heading',{name:'Vuelos'})).toBeVisible()
})

test('importación privada completa cuando CI entrega un workbook',async({page})=>{
  test.skip(!process.env.E2E_WORKBOOK_PATH,'El workbook privado nunca se versiona ni se exige en CI público.')
  await enter(page);await page.goto('/import');await page.locator('input[type=file]').setInputFiles(process.env.E2E_WORKBOOK_PATH!)
  await page.getByRole('button',{name:'Vista previa'}).click();await expect(page.getByText(/46 pasajeros/)).toBeVisible();await expect(page.getByText(/25 habitaciones/)).toBeVisible()
  await page.getByRole('button',{name:/Confirmar importación/}).click();await expect(page.getByText(/Importación confirmada/)).toBeVisible()
})
