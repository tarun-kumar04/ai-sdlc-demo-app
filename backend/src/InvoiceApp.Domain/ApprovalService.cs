namespace InvoiceApp.Domain;

/// <summary>
/// Moves invoices through submission, approval, and rejection while enforcing approval policy.
/// </summary>
public class ApprovalService
{
    private readonly IInvoiceRepository _repository;

    public ApprovalService(IInvoiceRepository repository)
    {
        _repository = repository;
    }

    public Invoice Submit(string invoiceId)
    {
        return _repository.UpdateAtomically(invoiceId, invoice =>
        {
            if (invoice.Status != InvoiceStatus.Draft)
            {
                throw new InvalidOperationException(
                    $"Invoice {invoiceId} is {invoice.Status}; only drafts can be submitted.");
            }

            invoice.Status = InvoiceStatus.PendingApproval;
        });
    }

    public Invoice Approve(string invoiceId, string approver, string approverRole)
    {
        if (string.IsNullOrWhiteSpace(approver))
        {
            throw new ArgumentException("An authenticated approver identity is required.", nameof(approver));
        }

        var approvalLimit = approverRole switch
        {
            "Manager" => 50_000m,
            "Director" => 500_000m,
            _ => throw new InvalidOperationException(
                $"Role '{approverRole}' is not authorized to approve invoices.")
        };

        return _repository.UpdateAtomically(invoiceId, invoice =>
        {
            EnsurePending(invoice);
            var approvalHistory = invoice.ApprovalHistory;

            if (invoice.Amount > 500_000m)
            {
                ApproveHighValueInvoice(invoice, approvalHistory, approver, approverRole);
                return;
            }

            if (invoice.Amount > approvalLimit)
            {
                throw new InvalidOperationException(
                    $"{approverRole} may approve invoices up to {approvalLimit:N0}; " +
                    $"invoice {invoice.Id} is {invoice.Amount:N2}.");
            }

            if (approvalHistory.Count != 0)
            {
                throw new InvalidOperationException(
                    $"Invoice {invoice.Id} has an approval history that is invalid for single approval.");
            }

            var approvedAt = DateTime.UtcNow;
            approvalHistory.Add(new InvoiceApproval(approver, approverRole, approvedAt));
            invoice.Status = InvoiceStatus.Approved;
            invoice.DecidedBy = approver;
            invoice.DecidedAt = approvedAt;
        });
    }

    private static void ApproveHighValueInvoice(
        Invoice invoice,
        List<InvoiceApproval> approvalHistory,
        string approver,
        string approverRole)
    {
        if (invoice.DecidedBy is not null || invoice.DecidedAt is not null)
        {
            throw new InvalidOperationException(
                $"Invoice {invoice.Id} has final decision fields set before approval is complete.");
        }

        if (approvalHistory.Count == 0)
        {
            approvalHistory.Add(new InvoiceApproval(approver, approverRole, DateTime.UtcNow));
            return;
        }

        if (approvalHistory.Count != 1)
        {
            throw new InvalidOperationException(
                $"Invoice {invoice.Id} is in an invalid approval stage.");
        }

        var firstApproval = approvalHistory[0];
        if (string.Equals(firstApproval.Approver, approver, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"User '{approver}' has already provided the first approval for invoice {invoice.Id}.");
        }

        if (approverRole != "Director")
        {
            throw new InvalidOperationException(
                $"A Director must provide the second approval for invoice {invoice.Id}.");
        }

        var approvedAt = DateTime.UtcNow;
        approvalHistory.Add(new InvoiceApproval(approver, approverRole, approvedAt));
        invoice.Status = InvoiceStatus.Approved;
        invoice.DecidedBy = approver;
        invoice.DecidedAt = approvedAt;
    }

    public Invoice Reject(string invoiceId, string approver, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to reject an invoice.", nameof(reason));
        }

        return _repository.UpdateAtomically(invoiceId, invoice =>
        {
            EnsurePending(invoice);

            invoice.Status = InvoiceStatus.Rejected;
            invoice.DecidedBy = approver;
            invoice.DecidedAt = DateTime.UtcNow;
            invoice.DecisionNote = reason;
        });
    }

    private static void EnsurePending(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.PendingApproval)
        {
            throw new InvalidOperationException(
                $"Invoice {invoice.Id} is {invoice.Status}; only pending invoices can be decided.");
        }
    }
}
