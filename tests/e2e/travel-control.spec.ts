import { test, expect } from '@playwright/test'

test.describe('control integral de viaje',()=>{
  test.skip(!process.env.E2E_ADMIN_EMAIL||!process.env.E2E_ADMIN_PASSWORD,'Requiere credenciales de un entorno efímero.')
  test('sesión, importación, control y exportación',async({page})=>{
    await page.goto('/login');await page.getByLabel('Correo').fill(process.env.E2E_ADMIN_EMAIL!);await page.getByLabel('Contraseña').fill(process.env.E2E_ADMIN_PASSWORD!);await page.getByRole('button',{name:'Ingresar'}).click();
    await expect(page.getByRole('heading',{name:'Estado del viaje'})).toBeVisible();
    await page.getByRole('link',{name:/Importar/}).click().catch(()=>page.goto('/import'));
    await page.locator('input[type=file]').setInputFiles('../../data/private/Control_viaje_boda_Cielito_Ronaldo.xlsx');await page.getByRole('button',{name:'Vista previa'}).click();
    await expect(page.getByText(/46 pasajeros/)).toBeVisible();await expect(page.getByText(/25 habitaciones/)).toBeVisible();
    await page.getByRole('button',{name:'Confirmar importación'}).click();await expect(page.getByText(/Importación confirmada/)).toBeVisible();
    await page.goto('/passengers?requirement=flight');await expect(page.getByRole('heading',{name:'Pasajeros'})).toBeVisible();
    const download=page.waitForEvent('download');await page.goto('/import');await page.getByRole('link',{name:/Descargar control XLSX/}).click();await download;
  })
})
