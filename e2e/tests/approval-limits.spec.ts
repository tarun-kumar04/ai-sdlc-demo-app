import { expect, test } from '@playwright/test';
import { InvoiceApprovalPage } from '../pages/invoice-approval.page';

test('Manager sees rejection and invoice remains unchanged when approving over 50,000', async ({ page }) => {
  const invoicePage = new InvoiceApprovalPage(page);
  const customerName = `Over-limit approval ${Date.now()}`;

  await page.goto('/');
  await invoicePage.signIn('demo.approver', 'ChangeMe123!', 'Manager');
  const invoiceId = await invoicePage.createAndSubmitInvoice(customerName, 50_000.01);

  const before = await invoicePage.getInvoiceState(invoiceId);
  await invoicePage.attemptApproval(invoiceId);

  await expect(page.getByRole('alert').filter({ hasText: 'Manager may approve invoices up to' }))
    .toContainText(`invoice ${invoiceId} is 50,000.01`);
  const after = await invoicePage.getInvoiceState(invoiceId);

  expect(after).toEqual(before);
  expect(after).toMatchObject({
    status: 'PendingApproval',
    decidedBy: null,
    decidedAt: null,
    decisionNote: null
  });
});

test('Director demo identity cannot use the Admin-only invoice delete action', async ({ page }) => {
  const invoicePage = new InvoiceApprovalPage(page);

  await page.goto('/');
  await invoicePage.signIn('demo.admin', 'ChangeMe123!', 'Director');

  const deleteStatus = await page.evaluate(async () => {
    const sessionValue = sessionStorage.getItem('session');
    if (!sessionValue) {
      throw new Error('Expected an authenticated session.');
    }

    const session = JSON.parse(sessionValue) as { token: string };
    const response = await fetch('/api/invoices/INV-1001', {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${session.token}` }
    });
    return response.status;
  });

  expect(deleteStatus).toBe(403);
  expect(await invoicePage.getInvoiceState('INV-1001')).toMatchObject({ status: 'PendingApproval' });
});
