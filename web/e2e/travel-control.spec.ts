import { test, expect, type Page, type TestInfo } from '@playwright/test'
import { readFile } from 'node:fs/promises'
import path from 'node:path'

const email='admin@example.test',password='Test-only-Password!2026'
const forbidden=['passportNumber','normalizedPassportNumber','maskedPassport','birthDate','nationality','passportExpiry','phone','email','dietaryRestrictions','notes','nextAction','nextActionDueDate','pnr','electronicTicketNumber','sourceReference','securePath','storedName','originalName','sha256','operatorContact','attachments','attachmentId','attachmentLinkId','linkId','evidenceType','sourceId','managePath','affectedPassengerCount','ticketVersion','updatedById','followUps','audit','auditLog','updatedBy','userName']

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

async function csrf(page:Page){
  const response=await page.request.get('/api/auth/csrf')
  expect(response.ok()).toBeTruthy()
  return (await response.json()).token as string
}

async function createPassenger(page:Page,name:string,token:string){
  const response=await page.request.post('/api/passengers',{headers:{'X-XSRF-TOKEN':token},data:{fullName:name,birthDate:null,nationality:null,passportNumber:null,passportExpiry:null,phone:null,email:null,primaryOperatorId:null,roomReservationId:null,nextAction:null,nextActionDueDate:null,dietaryRestrictions:null,notes:null}})
  if(response.status()===201)return (await response.json()).id as string
  expect(response.status()).toBe(409)
  const result=await (await page.request.get(`/api/passengers?search=${encodeURIComponent(name)}&page=1&pageSize=100`)).json()
  return result.items.find((item:{fullName:string})=>item.fullName===name).id as string
}

interface E2ERoom {id:string;internalCode:string;operator:{id:string};storedStatus:string;hotel?:string;roomType?:string;checkIn?:string;checkOut?:string;expectedCapacity:number;capacityOverride:boolean;capacityOverrideReason?:string;hotelReservationNumber?:string;mealPlan?:string;sourceReference?:string;operatorContact?:string;notes?:string;version:number}
interface E2EFlightPassenger {passengerId:string;ticketStatus:string;version:number}
interface E2EFlight {id:string;airline?:string;issuingAgency?:string;pnr?:string;generalReference?:string;sourceReference?:string;notes?:string;segments:{id:string;type:string;flightNumber?:string;originAirport?:string;destinationAirport?:string;departureAt?:string;arrivalAt?:string;originTimeZone?:string;destinationTimeZone?:string;sequence:number}[];passengers:E2EFlightPassenger[];version:number}

function roomUpdate(room:E2ERoom,hotel:string){
  return {internalCode:room.internalCode,operatorId:room.operator.id,status:'Confirmed',hotel,roomType:room.roomType,expectedCapacity:room.expectedCapacity,capacityOverride:room.capacityOverride,capacityOverrideReason:room.capacityOverrideReason,checkIn:room.checkIn,checkOut:room.checkOut,hotelReservationNumber:room.hotelReservationNumber,mealPlan:room.mealPlan,sourceReference:room.sourceReference,operatorContact:room.operatorContact,notes:room.notes,version:room.version}
}

async function prepareReadinessBlocker(page:Page,token:string){
  const fixture=path.join(process.cwd(),'e2e','fixtures','readiness-master.xlsx')
  const master=await page.request.post('/api/imports/commit',{headers:{'X-XSRF-TOKEN':token},multipart:{file:{name:'readiness-master.xlsx',mimeType:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',buffer:await readFile(fixture)}}})
  expect(master.ok()).toBeTruthy()

  const passengerPage=await (await page.request.get('/api/passengers?page=1&pageSize=100')).json() as {items:{id:string}[]}
  const passengerIds=passengerPage.items.map(item=>item.id)
  expect(passengerIds.length).toBeGreaterThan(0)
  let rooms=await (await page.request.get('/api/rooms')).json() as E2ERoom[]
  let room=rooms.find(item=>item.internalCode==='E2E-READY')!
  expect(room).toBeTruthy()
  const occupants=await page.request.put(`/api/rooms/${room.id}/occupants`,{headers:{'X-XSRF-TOKEN':token},data:{passengerIds,version:room.version}})
  expect(occupants.ok()).toBeTruthy()

  let flights=await (await page.request.get('/api/flights')).json() as E2EFlight[]
  let readinessFlight=flights.find(item=>item.pnr==='E2E-READY-PNR')
  const segments=readinessFlight?.segments??[
    {id:null,type:'Outbound',flightNumber:'RDY1',originAirport:'AAA',destinationAirport:'BBB',departureAt:'2026-09-06T10:00:00Z',arrivalAt:'2026-09-06T12:00:00Z',originTimeZone:null,destinationTimeZone:null,sequence:1},
    {id:null,type:'Return',flightNumber:'RDY2',originAirport:'BBB',destinationAirport:'AAA',departureAt:'2026-09-15T10:00:00Z',arrivalAt:'2026-09-15T12:00:00Z',originTimeZone:null,destinationTimeZone:null,sequence:2},
  ]
  const flightData={status:'Confirmed',airline:'Aerolínea ficticia',issuingAgency:null,pnr:'E2E-READY-PNR',generalReference:null,sourceReference:'Referencia ficticia',notes:'Fixture de readiness',passengerIds,version:readinessFlight?.version??0,segments}
  const savedFlight=readinessFlight
    ?await page.request.put(`/api/flights/${readinessFlight.id}`,{headers:{'X-XSRF-TOKEN':token},data:flightData})
    :await page.request.post('/api/flights',{headers:{'X-XSRF-TOKEN':token},data:flightData})
  expect(savedFlight.ok()).toBeTruthy()
  flights=await (await page.request.get('/api/flights')).json() as E2EFlight[]
  readinessFlight=flights.find(item=>item.pnr==='E2E-READY-PNR')!
  const proof=Buffer.from('%PDF-1.7\nTravel Control fictional readiness evidence\n%%EOF')
  const evidence=await page.request.post('/api/attachments',{headers:{'X-XSRF-TOKEN':token},multipart:{file:{name:'readiness-evidence.pdf',mimeType:'application/pdf',buffer:proof},documentType:'AirTicket',flightId:readinessFlight.id}})
  expect(evidence.ok()).toBeTruthy()

  for(const flight of flights){
    for(const link of flight.passengers.filter(item=>item.ticketStatus!=='Confirmed')){
      const ticket=await page.request.put(`/api/flights/${flight.id}/passengers/${link.passengerId}/ticket`,{headers:{'X-XSRF-TOKEN':token},data:{electronicTicketNumber:`E2E-${flight.id.slice(0,6)}-${link.passengerId.slice(0,12)}`,status:'Confirmed',notes:'Fixture ficticio',version:link.version}})
      expect(ticket.ok()).toBeTruthy()
    }
  }
  const baggage=await page.request.post('/api/baggage/confirm-group',{headers:{'X-XSRF-TOKEN':token},data:{flightBookingId:readinessFlight.id,passengerIds,sourceReference:'Referencia ficticia',notes:'Fixture ficticio'}})
  expect(baggage.ok()).toBeTruthy()

  for(const passengerId of passengerIds){
    const detail=await (await page.request.get(`/api/passengers/${passengerId}`)).json()
    const passenger=detail.passenger
    const updated=await page.request.put(`/api/passengers/${passengerId}`,{headers:{'X-XSRF-TOKEN':token},data:{fullName:passenger.fullName,birthDate:'1990-01-01',nationality:'Ficticia',passportNumber:`E2E-RDY-${passengerId.slice(0,18)}`,passportExpiry:'2035-01-01',passportReviewStatus:'Confirmed',documentationStatus:'Confirmed',documentationExceptionReason:null,phone:passenger.phone,email:passenger.email,primaryOperatorId:passenger.primaryOperator?.id??null,roomReservationId:room.id,estimatedHotelArrival:passenger.estimatedHotelArrival,dietaryRestrictions:passenger.dietaryRestrictions,notes:passenger.notes,nextAction:null,nextActionDueDate:null,version:passenger.version}})
    expect(updated.ok()).toBeTruthy()
  }
  const transfer=await (await page.request.get('/api/transfer')).json()
  const transferUpdate=await page.request.put('/api/transfer',{headers:{'X-XSRF-TOKEN':token},data:{isConfirmed:true,notes:'Fixture ficticio',version:transfer.version}})
  expect(transferUpdate.ok()).toBeTruthy()

  rooms=await (await page.request.get('/api/rooms')).json() as E2ERoom[]
  room=rooms.find(item=>item.id===room.id)!
  const pendingProperty=await page.request.put(`/api/rooms/${room.id}`,{headers:{'X-XSRF-TOKEN':token},data:roomUpdate(room,'PENDIENTE')})
  expect(pendingProperty.ok()).toBeTruthy()
  const dashboard=await (await page.request.get('/api/public/dashboard')).json()
  expect(dashboard).toMatchObject({overallStatus:'Attention',progressPercent:99,missing:{tickets:0,baggage:0,documentation:0,passports:0,passengersWithoutResolvedAccommodation:0,unresolvedRoomReservations:0,specificPropertiesPending:1,transfer:false}})
  return room.id
}

async function openTicketDialog(page:Page,pnr:string,passengerName:string){
  await page.goto('/gestion/vuelos')
  const card=page.getByRole('heading',{name:pnr}).locator('xpath=ancestor::*[contains(@class,"MuiCard-root")][1]')
  await expect(card).toBeVisible()
  const passenger=card.getByText(passengerName,{exact:true}).locator('xpath=ancestor::*[contains(@class,"MuiAlert-root")][1]')
  await passenger.getByRole('button',{name:'Gestionar ticket'}).click()
  await expect(page.getByRole('dialog',{name:`Ticket de ${passengerName}`})).toBeVisible()
}

test('consulta pública, navegación responsive y PWA sin login',async({page})=>{
  await page.goto('/')
  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByRole('heading',{name:'Estado del viaje'})).toBeVisible()
  await expect(page.getByText('Vista de consulta · Solo lectura')).toBeVisible()
  await expect(page.getByText(/Todavía faltan entregables para cerrar el viaje|El viaje está listo/)).toBeVisible()
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
  await expect(page.getByLabel('Pasaporte')).toBeVisible()
  await expect(page.getByLabel('PNR',{exact:true})).toBeVisible()
  await expect(page.getByLabel('Ticket')).toBeVisible()
  const desktop=(page.viewportSize()?.width??0)>=1200
  if(desktop){
    await expect(page.getByTestId('passenger-desktop-table')).toBeVisible()
    await expect(page.getByTestId('passenger-mobile-cards')).toBeHidden()
  }else{
    await expect(page.getByTestId('passenger-desktop-table')).toBeHidden()
    await expect(page.getByTestId('passenger-mobile-cards')).toBeVisible()
  }
  await noOverflow(page)
})

test('rutas privadas sin cookie devuelven 401',async({request})=>{
  expect((await request.get('/api/dashboard')).status()).toBe(401)
  expect((await request.get('/api/passengers?page=1&pageSize=1')).status()).toBe(401)
  expect((await request.get('/api/rooms')).status()).toBe(401)
  expect((await request.get('/api/flights')).status()).toBe(401)
  expect((await request.get('/api/baggage')).status()).toBe(401)
  expect((await request.get('/api/attachments')).status()).toBe(401)
  expect((await request.post('/api/baggage',{data:{}})).status()).toBe(401)
})

test('manifiesto ficticio confirma reservas sin ticket electrónico y mantiene el PNR privado',async({page},testInfo:TestInfo)=>{
  await enterManagement(page)
  const token=await csrf(page)
  const suffix=testInfo.project.name.replace(/[^a-z0-9]/gi,'-')
  const firstName=`E2E Reserva Uno ${suffix}`
  const secondName=`E2E Reserva Dos ${suffix}`
  const firstId=await createPassenger(page,firstName,token)
  await createPassenger(page,secondName,token)
  const pnr=`RSV-${suffix}`.toUpperCase()
  const csv=[
    'row;name;passport;birth_date;passport_expiry;nationality_code;pnr;airline_code;check_in;check_out',
    `1;${firstName};PX-${suffix}-1;1990-01-01;2035-01-01;Pya;${pnr};CM;;`,
    `2;${secondName};PX-${suffix}-2;1991-01-01;2035-01-01;Pya;${pnr};CM;;`,
  ].join('\n')

  await page.goto('/gestion/importar')
  const card=page.getByRole('heading',{name:'Actualización de pasajeros y tickets'}).locator('xpath=ancestor::*[contains(@class,"MuiCard-root")][1]')
  await card.locator('input[type="file"]').setInputFiles({name:'manifest-fixture.csv',mimeType:'text/csv',buffer:Buffer.from(csv,'utf8')})
  await card.getByLabel('Confirmo que los valores personales no vacíos de esta fuente pueden sobrescribir valores existentes').check()
  await card.getByLabel('Confirmo agregar las nuevas reservas detectadas conservando las asignaciones aéreas existentes').check()
  await card.getByRole('button',{name:'Vista previa'}).click()
  await expect(card.getByRole('group',{name:'Filas leídas'})).toContainText('2')
  await expect(card.getByRole('group',{name:'PNR únicos'})).toContainText('1')
  await expect(card.getByText('Copa Airlines: 2 pasajeros')).toBeVisible()
  await card.getByLabel('Confirmo administrativamente esta actualización autoritativa con el mismo hash').check()
  await card.getByRole('button',{name:'Confirmar actualización'}).click()
  await expect(card.getByText('Actualización confirmada y auditada.')).toBeVisible()

  await page.goto(`/gestion/pasajeros/${firstId}`)
  await expect(page.getByText('Copa Airlines (CM)',{exact:true}).first()).toBeVisible()
  await expect(page.getByText(pnr,{exact:true}).first()).toBeVisible()
  await expect(page.getByText('Número electrónico no informado',{exact:true}).first()).toBeVisible()
  if((page.viewportSize()?.width??0)<900){
    await page.getByRole('combobox',{name:'Sección'}).click()
    await page.getByRole('option',{name:'Vuelo'}).click()
  }else await page.getByRole('tab',{name:'Vuelo'}).click()
  await expect(page.getByText('Itinerario detallado no cargado',{exact:true}).first()).toBeVisible()

  await page.goto(`/pasajeros/${firstId}`)
  await expect(page.getByText('Copa Airlines',{exact:true}).first()).toBeVisible()
  await expect(page.getByText('Confirmado',{exact:true}).first()).toBeVisible()
  await expect(page.getByText(pnr,{exact:true})).toHaveCount(0)
  const publicResponse=await page.request.get(`/api/public/passengers/${firstId}`)
  const publicJson=JSON.stringify(await publicResponse.json())
  expect(publicJson).toContain('Copa Airlines')
  for(const key of forbidden)expect(publicJson.toLowerCase()).not.toContain(`"${key.toLowerCase()}"`)
})

test('evidencia compartida tipada, impacto seguro y ticket concurrente',async({page},testInfo:TestInfo)=>{
  await enterManagement(page)
  const token=await csrf(page)
  const suffix=testInfo.project.name.replace(/[^a-z0-9]/gi,'-')
  const firstName=`E2E Evidencia Uno ${suffix}`
  const secondName=`E2E Evidencia Dos ${suffix}`
  const firstId=await createPassenger(page,firstName,token)
  const secondId=await createPassenger(page,secondName,token)
  const pnr=`E2E-${suffix}`
  const existingFlights=await (await page.request.get('/api/flights')).json() as {id:string;pnr?:string;passengers:{passengerId:string}[]}[]
  let flightId=existingFlights.find(item=>item.pnr===pnr&&item.passengers.some(passenger=>passenger.passengerId===firstId))?.id
  if(!flightId){
    const flight=await page.request.post('/api/flights',{headers:{'X-XSRF-TOKEN':token},data:{status:'Confirmed',airline:'Aerolínea E2E',issuingAgency:null,pnr,generalReference:null,sourceReference:null,notes:null,passengerIds:[firstId,secondId],version:0,segments:[{id:null,type:'Outbound',flightNumber:'E2E1',originAirport:'AAA',destinationAirport:'BBB',departureAt:'2026-09-01T10:00:00Z',arrivalAt:'2026-09-01T12:00:00Z',originTimeZone:null,destinationTimeZone:null,sequence:1},{id:null,type:'Return',flightNumber:'E2E2',originAirport:'BBB',destinationAirport:'AAA',departureAt:'2026-09-10T10:00:00Z',arrivalAt:'2026-09-10T12:00:00Z',originTimeZone:null,destinationTimeZone:null,sequence:2}]}})
    expect(flight.status()).toBe(201)
    flightId=(await flight.json()).id as string
  }
  expect(flightId).toBeTruthy()
  const pdf=Buffer.from('%PDF-1.7\nTravel Control fictional shared evidence\n%%EOF')
  const upload=async(documentType:string)=>page.request.post('/api/attachments',{headers:{'X-XSRF-TOKEN':token},multipart:{file:{name:'e2e-shared-evidence.pdf',mimeType:'application/pdf',buffer:pdf},documentType,flightId:flightId!}})
  const ticketUpload=await upload('AirTicket');expect(ticketUpload.ok()).toBeTruthy();const ticketLink=await ticketUpload.json()
  const baggageUpload=await upload('BaggageProof');expect(baggageUpload.ok()).toBeTruthy();const baggageLink=await baggageUpload.json()
  expect(baggageLink.attachmentId).toBe(ticketLink.attachmentId)
  expect(baggageLink.linkId).not.toBe(ticketLink.linkId)
  expect(new Set([ticketLink.evidenceType,baggageLink.evidenceType])).toEqual(new Set(['AirTicket','BaggageProof']))
  const repeated=await upload('BaggageProof');expect(repeated.ok()).toBeTruthy();expect((await repeated.json()).linkCreated).toBeFalsy()
  const physical=await (await page.request.get('/api/attachments')).json() as {id:string;links:{flightId?:string;evidenceType:string}[]}[]
  const stored=physical.filter(item=>item.id===ticketLink.attachmentId)
  expect(stored).toHaveLength(1)
  expect(stored[0].links.filter(link=>link.flightId===flightId).map(link=>link.evidenceType).sort()).toEqual(['AirTicket','BaggageProof'])
  const impact=await (await page.request.get(`/api/attachments/${ticketLink.attachmentId}/links/${ticketLink.linkId}/impact`)).json()
  expect(impact).toMatchObject({sourceType:'FlightBooking',affectedPassengerCount:2,isShared:true,canUnlink:true})

  for(const passengerId of [firstId,secondId]){
    const detail=await (await page.request.get(`/api/passengers/${passengerId}`)).json()
    const shared=detail.relatedEvidence.filter((item:{attachmentId:string;sourceType:string})=>item.attachmentId===ticketLink.attachmentId&&item.sourceType==='FlightBooking')
    expect(shared).toHaveLength(2)
    expect(shared.every((item:{isDirect:boolean;canUnlinkHere:boolean;affectedPassengerCount:number})=>!item.isDirect&&!item.canUnlinkHere&&item.affectedPassengerCount===2)).toBeTruthy()
  }
  await page.goto(`/gestion/pasajeros/${firstId}`)
  if((page.viewportSize()?.width??0)<900){
    await page.getByRole('combobox',{name:'Sección'}).click();await page.getByRole('option',{name:'Documentación'}).click()
  }else await page.getByRole('tab',{name:'Documentación'}).click()
  await expect(page.getByText(`Compartido por PNR ${pnr}`)).toHaveCount(2)
  await expect(page.getByText('cubre a 2 pasajero(s)').first()).toBeVisible()
  await expect(page.getByRole('button',{name:'Desvincular',exact:true})).toHaveCount(0)
  await page.getByRole('button',{name:'Administrar en Vuelos'}).first().click()
  await expect(page.getByRole('heading',{name:'Evidencias compartidas por PNR'})).toBeVisible()
  const scope=page.getByText(`Compartido por ${pnr} · cubre a 2 pasajero(s)`).first().locator('..').locator('..')
  const dialogPromise=page.waitForEvent('dialog')
  await scope.getByRole('button',{name:'Desvincular del PNR'}).click()
  const dialog=await dialogPromise
  const warning=dialog.message()
  await dialog.dismiss()
  expect(warning).toContain(`PNR ${pnr}`);expect(warning).toContain('2 pasajero(s)')

  const secondPage=await page.context().newPage()
  await Promise.all([openTicketDialog(page,pnr,firstName),openTicketDialog(secondPage,pnr,firstName)])
  const number=`E2E-TICKET-${suffix}-${Date.now()}`
  for(const current of [page,secondPage]){
    await current.getByLabel('Número de ticket electrónico').fill(number)
    await current.getByLabel('Estado individual').click()
    await current.getByRole('option',{name:'Confirmado'}).click()
  }
  await page.getByRole('dialog').getByRole('button',{name:'Guardar ticket'}).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await secondPage.getByRole('dialog').getByRole('button',{name:'Guardar ticket'}).click()
  await expect(secondPage.getByText('El ticket cambió desde que abriste la ficha. Recargá antes de guardar.')).toBeVisible()
  await expect(secondPage.getByRole('button',{name:'Recargar datos'})).toBeVisible()
  await expect(secondPage.getByRole('dialog')).toBeVisible()
  await secondPage.close()
})

test('identificación XLSX es idempotente y exige doble confirmación al sobrescribir',async({page},testInfo:TestInfo)=>{
  await enterManagement(page)
  const token=await csrf(page)
  const project=testInfo.project.name
  const name=`E2E Identificación ${project}`
  await createPassenger(page,name,token)
  const fixture=(kind:'initial'|'conflict')=>path.join(process.cwd(),'e2e','fixtures',`identification-${project}-${kind}.xlsx`)

  await page.goto('/gestion/importar')
  const importCard=page.getByRole('heading',{name:'Importar identificación'}).locator('xpath=ancestor::*[contains(@class,"MuiCard-root")][1]')
  await importCard.locator('input[type="file"]').setInputFiles(fixture('initial'))
  await importCard.getByRole('button',{name:'Vista previa'}).click()
  await expect(importCard.getByText('Hoja seleccionada: Identificación. Control preventivo interno; verificar requisitos migratorios oficiales.')).toBeVisible()
  await expect(importCard.getByRole('group',{name:'Pasajeros a actualizar'})).toBeVisible()
  await importCard.getByRole('button',{name:'Confirmar identificación'}).click()
  await expect(importCard.getByText(/Importación confirmada/)).toBeVisible()
  await expect(page.getByRole('heading',{name:'Calidad de datos de identificación'})).toBeVisible()

  const upload=async(endpoint:string,kind:'initial'|'conflict',overwriteExisting:boolean,confirmOverwrite=false)=>page.request.post(endpoint,{headers:{'X-XSRF-TOKEN':token},multipart:{file:{name:`${kind}.xlsx`,mimeType:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',buffer:await readFile(fixture(kind))},overwriteExisting:String(overwriteExisting),confirmOverwrite:String(confirmOverwrite)}})
  const repeated=await upload('/api/imports/identification/commit','initial',false)
  expect(repeated.ok()).toBeTruthy()
  expect(await repeated.json()).toMatchObject({willUpdate:0,unchanged:1})
  const safeConflict=await upload('/api/imports/identification/preview','conflict',false)
  expect(safeConflict.ok()).toBeTruthy()
  const safeSummary=await safeConflict.json();expect(safeSummary.conflicts).toBeGreaterThan(0);expect(safeSummary.willOverwrite).toBe(0)
  const overwritePreview=await upload('/api/imports/identification/preview','conflict',true)
  expect(overwritePreview.ok()).toBeTruthy();expect((await overwritePreview.json()).willOverwrite).toBeGreaterThan(0)
  const unconfirmed=await upload('/api/imports/identification/commit','conflict',true,false)
  expect(unconfirmed.status()).toBe(400)
  const confirmed=await upload('/api/imports/identification/commit','conflict',true,true)
  expect(confirmed.ok()).toBeTruthy();expect((await confirmed.json()).willOverwrite).toBeGreaterThan(0)
  const privatePassengers=await (await page.request.get(`/api/passengers?search=${encodeURIComponent(name)}&page=1&pageSize=10`)).json()
  expect(privatePassengers.total).toBe(1)
})

test('readiness limita a 99 con un bloqueante global y vuelve a 100 al resolverlo',async({page})=>{
  await enterManagement(page)
  const token=await csrf(page)
  const roomId=await prepareReadinessBlocker(page,token)
  await page.goto('/')
  await expect(page.getByText('Todavía faltan entregables para cerrar el viaje')).toBeVisible()
  await expect(page.getByText('99%',{exact:true})).toBeVisible()
  await expect(page.getByText('Atención',{exact:true})).toBeVisible()
  await expect(page.getByRole('button',{name:'Ver estado de alojamiento'})).toBeVisible()

  const room=(await (await page.request.get('/api/rooms')).json() as E2ERoom[]).find(item=>item.id===roomId)!
  const resolved=await page.request.put(`/api/rooms/${room.id}`,{headers:{'X-XSRF-TOKEN':token},data:roomUpdate(room,'Hotel ficticio')})
  expect(resolved.ok()).toBeTruthy()
  await expect.poll(async()=>{
    const response=await page.request.get('/api/public/dashboard')
    return response.ok()?(await response.json()).overallStatus:'Unavailable'
  },{timeout:10_000}).toBe('Ready')
  await page.reload()
  await expect(page.getByText('El viaje está listo')).toBeVisible()
  await expect(page.getByText('100%',{exact:true})).toBeVisible()
  await expect(page.getByText('Listo',{exact:true})).toBeVisible()
})
