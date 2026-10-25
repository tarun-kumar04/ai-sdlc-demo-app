namespace InvoiceApp.Domain;

public interface IInvoiceRepository
{
    IReadOnlyList<Invoice> GetAll();
    Invoice? GetById(string id);
    IReadOnlyList<Invoice> GetByIds(IEnumerable<string> ids);
    Invoice Add(Invoice invoice);
    void Update(Invoice invoice);
    /// <summary>Serializes a change and commits it only if the callback completes successfully.</summary>
    Invoice UpdateAtomically(string id, Action<Invoice> update);
    bool Remove(string id);
}
