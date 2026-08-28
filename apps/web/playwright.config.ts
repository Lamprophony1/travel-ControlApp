import { defineConfig, devices } from '@playwright/test'
export default defineConfig({testDir:'../../tests/e2e',fullyParallel:false,retries:1,reporter:'html',use:{baseURL:'https://localhost',trace:'on-first-retry',ignoreHTTPSErrors:true},projects:[{name:'desktop',use:{...devices['Desktop Chrome']}},{name:'mobile',use:{...devices['Pixel 7']}}]})

