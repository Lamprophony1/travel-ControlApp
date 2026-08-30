import { defineConfig, devices } from '@playwright/test'
const base={...devices['Desktop Chrome']}
export default defineConfig({testDir:'./e2e',fullyParallel:false,workers:1,retries:1,timeout:60_000,reporter:'html',use:{baseURL:process.env.E2E_BASE_URL??'http://127.0.0.1:8080',trace:'on-first-retry'},projects:[
  {name:'mobile-360',use:{...devices['Pixel 7'],viewport:{width:360,height:800}}},
  {name:'mobile-390',use:{...devices['Pixel 7'],viewport:{width:390,height:844}}},
  {name:'mobile-430',use:{...devices['Pixel 7'],viewport:{width:430,height:932}}},
  {name:'tablet-768',use:{...base,viewport:{width:768,height:1024}}},
  {name:'desktop-1440',use:{...base,viewport:{width:1440,height:900}}}
]})
