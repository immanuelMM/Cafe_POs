import { StrictMode } from 'react'
import { http, HttpResponse } from 'msw'
import { screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { server } from '../../test/server'
import { renderWithProviders } from '../../test/render'
import { url } from '../../test/fixtures'
import { PrintableReceipt } from './ReceiptView'

const receipt = {
  locationName: 'Main Branch',
  locationAddress: null,
  locationPhone: null,
  orderNumber: 9,
  orderId: 'order-strict-1',
  completedAt: '2026-01-01T02:01:00Z',
  timeZone: 'Asia/Manila',
  cashierName: 'Casey Cashier',
  lines: [{ description: 'Iced Tea', modifiers: null, quantity: 1, unitPrice: 100, lineTotal: 100 }],
  pricesIncludeVat: true,
  vatRatePercent: 12,
  vatableSales: 89.29,
  vatAmount: 10.71,
  total: 100,
  paymentMethod: 'cash',
  amountTendered: 100,
  changeGiven: 0,
  currency: 'PHP',
}

describe('PrintableReceipt autoPrint', () => {
  it('prints once per order even with StrictMode double effects', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined)
    server.use(http.get(url('/api/v1/orders/:id/receipt'), () => HttpResponse.json(receipt)))
    const ui = (
      <StrictMode>
        <PrintableReceipt orderId="order-strict-1" autoPrint />
      </StrictMode>
    )
    renderWithProviders(ui)
    expect(await screen.findByRole('article')).toBeInTheDocument()
    await vi.waitFor(() => expect(print).toHaveBeenCalledTimes(1))
    await new Promise((r) => setTimeout(r, 600))
    expect(print).toHaveBeenCalledTimes(1)
    print.mockRestore()
  })

  it('never prints by itself unless asked', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined)
    server.use(http.get(url('/api/v1/orders/:id/receipt'), () => HttpResponse.json(receipt)))
    renderWithProviders(<PrintableReceipt orderId="order-strict-2" />)
    await screen.findByRole('article')
    await new Promise((r) => setTimeout(r, 500))
    expect(print).not.toHaveBeenCalled()
    print.mockRestore()
  })
})
