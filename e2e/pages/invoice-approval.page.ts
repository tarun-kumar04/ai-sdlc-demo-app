import { expect, type Page } from '@playwright/test';

export class InvoiceApprovalPage {
  constructor(private readonly page: Page) {}

  async signIn(username: string, password: string, role: string): Promise<void> {
    await this.page.getByLabel('Username').fill(username);
    await this.page.getByLabel('Password').fill(password);
    await this.page.getByRole('button', { name: 'Sign in' }).click();
    await expect(this.page.getByText(`${username} (${role})`, { exact: true })).toBeVisible();
  }

  async createAndSubmitInvoice(customerName: string, amount: number): Promise<string> {
    await this.page.getByLabel('Customer name').fill(customerName);
    await this.page.getByLabel('Amount').fill(String(amount));
    await this.page.getByRole('button', { name: 'Create invoice' }).click();

    const invoiceRow = this.page.getByRole('row').filter({ hasText: customerName });
    await expect(invoiceRow).toBeVisible();
    const invoiceId = (await invoiceRow.getByRole('cell').nth(0).textContent())?.trim();
    if (!invoiceId) {
      throw new Error(`Invoice row for ${customerName} did not contain an invoice ID.`);
    }

    await invoiceRow.getByRole('button', { name: `Submit invoice ${invoiceId} for approval` }).click();
    await expect(invoiceRow.getByRole('cell').nth(3)).toHaveText('Pending Approval');
    return invoiceId;
  }

  async attemptApproval(invoiceId: string): Promise<void> {
    await this.page.getByRole('tab', { name: 'Approvals' }).click();
    await this.page.getByRole('button', { name: `Approve invoice ${invoiceId}` }).click();
  }

  async getDecisionError(): Promise<string> {
    const alert = this.page.getByRole('alert').filter({ hasText: 'Manager may approve invoices up to' });
    await expect(alert).toBeVisible();
    return (await alert.textContent())?.trim() ?? '';
  }

  async getInvoiceState(invoiceId: string): Promise<{
    status: string;
    decidedBy: string | null;
    decidedAt: string | null;
    decisionNote: string | null;
  }> {
    return this.page.evaluate(async (id) => {
      const sessionValue = sessionStorage.getItem('session');
      if (!sessionValue) {
        throw new Error('Expected an authenticated session.');
      }

      const session = JSON.parse(sessionValue) as { token: string };
      const response = await fetch(`/api/invoices/${id}`, {
        headers: { Authorization: `Bearer ${session.token}` }
      });
      if (!response.ok) {
        throw new Error(`Invoice lookup failed (${response.status}).`);
      }

      const invoice = await response.json() as {
        status: string;
        decidedBy: string | null;
        decidedAt: string | null;
        decisionNote: string | null;
      };
      return {
        status: invoice.status,
        decidedBy: invoice.decidedBy,
        decidedAt: invoice.decidedAt,
        decisionNote: invoice.decisionNote
      };
    }, invoiceId);
  }
}
