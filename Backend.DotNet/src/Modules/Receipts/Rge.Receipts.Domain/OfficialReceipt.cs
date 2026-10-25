using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Domain.Policies;

namespace Rge.Receipts.Domain;

/// <summary>
/// Aggregate root representing an authoritative BIR Official Receipt and Treasury Acknowledgement.
/// </summary>
public sealed class OfficialReceipt : AggregateRoot<string>
{
    public string OrderId { get; private set; }
    public string OrderNumber { get; private set; }
    public string PaymentAttemptId { get; private set; }
    public OfficialReceiptNumber ReceiptNumber { get; private set; }
    public TreasuryAcknowledgement Acknowledgement { get; private set; }
    public string CustomerName { get; private set; }
    public string CustomerEmail { get; private set; }
    public string Description { get; private set; }
    public string PaymentMethod { get; private set; }
    public VatBreakdown VatBreakdown { get; private set; }
    public AuditDigest Digest { get; private set; }
    public DateTimeOffset IssuedAtUtc { get; private set; }

    public OfficialReceipt(
        string id,
        string orderId,
        string orderNumber,
        string paymentAttemptId,
        OfficialReceiptNumber receiptNumber,
        TreasuryAcknowledgement acknowledgement,
        string customerName,
        string customerEmail,
        string description,
        string paymentMethod,
        VatBreakdown vatBreakdown,
        AuditDigest digest,
        DateTimeOffset issuedAtUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        OrderId = Guard.AgainstNullOrWhiteSpace(orderId);
        OrderNumber = Guard.AgainstNullOrWhiteSpace(orderNumber);
        PaymentAttemptId = Guard.AgainstNullOrWhiteSpace(paymentAttemptId);
        ReceiptNumber = receiptNumber ?? throw new ArgumentNullException(nameof(receiptNumber));
        Acknowledgement = acknowledgement ?? throw new ArgumentNullException(nameof(acknowledgement));
        CustomerName = string.IsNullOrWhiteSpace(customerName) ? "Valued Customer" : customerName.Trim();
        CustomerEmail = string.IsNullOrWhiteSpace(customerEmail) ? string.Empty : customerEmail.Trim();
        Description = Guard.AgainstNullOrWhiteSpace(description);
        PaymentMethod = Guard.AgainstNullOrWhiteSpace(paymentMethod);
        VatBreakdown = vatBreakdown ?? throw new ArgumentNullException(nameof(vatBreakdown));
        Digest = digest ?? throw new ArgumentNullException(nameof(digest));
        IssuedAtUtc = issuedAtUtc;
    }

    public static Result<OfficialReceipt> Issue(
        string orderId,
        string orderNumber,
        string paymentAttemptId,
        long sequenceNumber,
        string customerName,
        string customerEmail,
        string description,
        string paymentMethod,
        Money grossAmount,
        AuditDigest digest,
        DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(digest);

        var vatBreakdown = VatBreakdown.FromVatInclusiveGross(grossAmount);
        var orNumber = OfficialReceiptNumber.Create(issuedAtUtc, sequenceNumber, digest.ShortHash8);
        var ack = TreasuryAcknowledgement.Create(issuedAtUtc, sequenceNumber, digest.ShortHash8);

        Result policyCheck = BirRmc77_2024Policy.ValidateReceiptInvariants(
            grossAmount,
            vatBreakdown,
            orNumber,
            digest);

        if (policyCheck.IsFailure)
        {
            return Result.Failure<OfficialReceipt>(policyCheck.Error);
        }

        var receipt = new OfficialReceipt(
            id: Guid.NewGuid().ToString("D"),
            orderId: orderId,
            orderNumber: orderNumber,
            paymentAttemptId: paymentAttemptId,
            receiptNumber: orNumber,
            acknowledgement: ack,
            customerName: customerName,
            customerEmail: customerEmail,
            description: description,
            paymentMethod: paymentMethod,
            vatBreakdown: vatBreakdown,
            digest: digest,
            issuedAtUtc: issuedAtUtc);

        return Result.Success(receipt);
    }
}
