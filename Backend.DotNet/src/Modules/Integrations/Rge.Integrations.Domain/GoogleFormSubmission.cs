using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Integrations.Domain;

/// <summary>
/// Aggregate root representing an ingested Google Forms order submission.
/// </summary>
public sealed class GoogleFormSubmission : AggregateRoot<string>
{
    public string ResponseId { get; private set; }
    public string CustomerName { get; private set; }
    public string CustomerEmail { get; private set; }
    public string? CustomerPhone { get; private set; }
    public string ItemReference { get; private set; }
    public int Quantity { get; private set; }
    public Money Amount { get; private set; }
    public string PaymentMethod { get; private set; }
    public string? Notes { get; private set; }
    public string? StudentId { get; private set; }
    public string? InstructorCredit { get; private set; }
    public string? BookTitle { get; private set; }
    public string? OrderId { get; private set; }
    public string? OrderNumber { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public DateTimeOffset SubmittedAtUtc { get; private set; }
    public bool IsDuplicate { get; private set; }

    public GoogleFormSubmission(
        string id,
        string responseId,
        string customerName,
        string customerEmail,
        string? customerPhone,
        string itemReference,
        int quantity,
        Money amount,
        string paymentMethod,
        string? notes,
        string? studentId,
        string? instructorCredit,
        string? bookTitle,
        DateTimeOffset submittedAtUtc,
        bool isDuplicate = false) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        ResponseId = Guard.AgainstNullOrWhiteSpace(responseId);
        CustomerName = Guard.AgainstNullOrWhiteSpace(customerName);
        CustomerEmail = Guard.AgainstNullOrWhiteSpace(customerEmail);
        CustomerPhone = customerPhone;
        ItemReference = Guard.AgainstNullOrWhiteSpace(itemReference);
        Quantity = Math.Max(1, quantity);
        Amount = amount;
        PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "hosted_checkout" : paymentMethod;
        Notes = notes;
        StudentId = studentId;
        InstructorCredit = instructorCredit;
        BookTitle = bookTitle;
        SubmittedAtUtc = submittedAtUtc;
        IsDuplicate = isDuplicate;
    }

    public void AttachOrder(string orderId, string orderNumber, string checkoutUrl)
    {
        OrderId = Guard.AgainstNullOrWhiteSpace(orderId);
        OrderNumber = Guard.AgainstNullOrWhiteSpace(orderNumber);
        CheckoutUrl = Guard.AgainstNullOrWhiteSpace(checkoutUrl);
    }
}

