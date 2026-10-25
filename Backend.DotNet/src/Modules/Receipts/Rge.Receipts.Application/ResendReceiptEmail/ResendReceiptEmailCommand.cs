using Rge.BuildingBlocks.Application.Abstractions;

namespace Rge.Receipts.Application.ResendReceiptEmail;

public sealed record ResendReceiptEmailCommand(
    string PaymentId,
    string? OverrideRecipientEmail = null) : ICommand<ResendReceiptEmailResponse>;

public sealed record ResendReceiptEmailResponse(
    string PaymentId,
    string OfficialReceiptNumber,
    string RecipientEmail,
    bool Dispatched);
