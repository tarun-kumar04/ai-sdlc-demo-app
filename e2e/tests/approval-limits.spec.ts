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

test('a distinct Director completes a high-value approval after the first approval', async ({ page }) => {
  const invoicePage = new InvoiceApprovalPage(page);
  const customerName = `High-value approval ${Date.now()}`;

  await page.goto('/');
  await invoicePage.signIn('demo.approver', 'ChangeMe123!', 'Manager');
  const invoiceId = await invoicePage.createAndSubmitInvoice(customerName, 500_000.01);
  await invoicePage.attemptApproval(invoiceId);

  const afterFirstApproval = await invoicePage.getInvoiceState(invoiceId);
  expect(afterFirstApproval).toMatchObject({
    status: 'PendingApproval',
    decidedBy: null,
    decidedAt: null,
    decisionNote: null,
    approvalHistory: [{ approver: 'demo.approver', role: 'Manager' }]
  });

  await invoicePage.signOut();
  await invoicePage.signIn('demo.admin', 'ChangeMe123!', 'Director');
  await invoicePage.attemptApproval(invoiceId);

  const afterSecondApproval = await invoicePage.getInvoiceState(invoiceId);
  expect(afterSecondApproval.status).toBe('Approved');
  expect(afterSecondApproval.decidedBy).toBe('demo.admin');
  expect(afterSecondApproval.decidedAt).not.toBeNull();
  expect(afterSecondApproval.approvalHistory).toHaveLength(2);
  expect(afterSecondApproval.approvalHistory[1]).toMatchObject({
    approver: 'demo.admin',
    role: 'Director',
    approvedAt: afterSecondApproval.decidedAt
  });
});

test('the first approver cannot approve a high-value invoice twice', async ({ page }) => {
  const invoicePage = new InvoiceApprovalPage(page);
  const customerName = `Repeated high-value approval ${Date.now()}`;

  await page.goto('/');
  await invoicePage.signIn('demo.approver', 'ChangeMe123!', 'Manager');
  const invoiceId = await invoicePage.createAndSubmitInvoice(customerName, 500_000.01);
  await invoicePage.attemptApproval(invoiceId);
  const beforeRejectedAttempt = await invoicePage.getInvoiceState(invoiceId);

  await invoicePage.attemptApproval(invoiceId);

  await expect(page.getByRole('alert')).toContainText('already provided the first approval');
  expect(await invoicePage.getInvoiceState(invoiceId)).toEqual(beforeRejectedAttempt);
});

test('a Manager cannot provide the second approval for a high-value invoice', async ({ page }) => {
  const invoicePage = new InvoiceApprovalPage(page);
  const customerName = `Non-Director second approval ${Date.now()}`;

  await page.goto('/');
  await invoicePage.signIn('demo.approver', 'ChangeMe123!', 'Manager');
  const invoiceId = await invoicePage.createAndSubmitInvoice(customerName, 500_000.01);
  await invoicePage.signOut();
  await invoicePage.signIn('demo.admin', 'ChangeMe123!', 'Director');
  await invoicePage.attemptApproval(invoiceId);
  await invoicePage.signOut();
  await invoicePage.signIn('demo.approver', 'ChangeMe123!', 'Manager');
  const beforeRejectedAttempt = await invoicePage.getInvoiceState(invoiceId);

  await invoicePage.attemptApproval(invoiceId);

  await expect(page.getByRole('alert')).toContainText('A Director must provide the second approval');
  expect(await invoicePage.getInvoiceState(invoiceId)).toEqual(beforeRejectedAttempt);
});
