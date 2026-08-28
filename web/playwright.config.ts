import { defineConfig, devices } from '@playwright/test'
export default defineConfig({testDir:'./e2e',fullyParallel:false,workers:1,retries:1,reporter:'html',use:{baseURL:process.env.E2E_BASE_URL??'http://127.0.0.1:8080',trace:'on-first-retry'},projects:[{name:'desktop',use:{...devices['Desktop Chrome']}},{name:'mobile',use:{...devices['Pixel 7']}}]})
