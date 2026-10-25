using InvoiceApp.Domain;

namespace InvoiceApp.Tests;

internal class FakeInvoiceRepository : IInvoiceRepository
{
    private readonly object _lock = new();
    private readonly List<Invoice> _invoices = new();

    public int UpdateCount { get; private set; }

    public FakeInvoiceRepository(params Invoice[] seed) => _invoices.AddRange(seed);

    public IReadOnlyList<Invoice> GetAll() { lock (_lock) return _invoices.ToList(); }
    public Invoice? GetById(string id) { lock (_lock) return _invoices.FirstOrDefault(i => i.Id == id); }
    public IReadOnlyList<Invoice> GetByIds(IEnumerable<string> ids)
    {
        var wanted = ids.ToHashSet();
        lock (_lock) return _invoices.Where(i => wanted.Contains(i.Id)).ToList();
    }
    public Invoice Add(Invoice invoice) { lock (_lock) { _invoices.Add(invoice); return invoice; } }
    public void Update(Invoice invoice) { lock (_lock) UpdateCount++; }
    public Invoice UpdateAtomically(string id, Action<Invoice> update)
    {
        lock (_lock)
        {
            var index = _invoices.FindIndex(candidate => candidate.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Invoice {id} was not found.");
            }

            var original = _invoices[index];
            var workingCopy = new Invoice
            {
                Id = original.Id,
                CustomerName = original.CustomerName,
                Amount = original.Amount,
                Status = original.Status,
                CreatedAt = original.CreatedAt,
                ApprovalHistory = original.ApprovalHistory.ToList(),
                DecidedBy = original.DecidedBy,
                DecidedAt = original.DecidedAt,
                DecisionNote = original.DecisionNote
            };

            update(workingCopy);
            _invoices[index] = workingCopy;
            UpdateCount++;
            return workingCopy;
        }
    }
    public bool Remove(string id) { lock (_lock) return _invoices.RemoveAll(i => i.Id == id) > 0; }
}
