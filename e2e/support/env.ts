export const BASE_URL = process.env.E2E_BASE_URL ?? 'http://localhost:5173'
export const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5080'

/** Seeded by CafePos.Infrastructure DevDataSeeder (DEV ONLY credentials, see docs/api). */
export const USERS = {
  owner: { email: 'admin@cafe.local', password: 'Admin#Dev12345', name: 'Alex Admin' },
  cashier: { email: 'cashier@cafe.local', password: 'Cashier#Dev12345', name: 'Casey Cashier' }, // Main only
  manager: { email: 'manager@cafe.local', password: 'Manager#Dev12345', name: 'Morgan Manager' }, // Main + Uptown
  uptownCashier: { email: 'cashier.uptown@cafe.local', password: 'Cashier#Dev12345', name: 'Uma Uptown' }, // Uptown only
} as const

export const LOCATIONS = {
  main: { id: '11111111-1111-1111-1111-111111111111', name: 'Brew & Bean - Main Branch' },
  uptown: { id: '22222222-2222-2222-2222-222222222222', name: 'Brew & Bean - Uptown Branch' },
} as const
