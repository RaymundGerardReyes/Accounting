using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Receipts.Application.IssueReceipt;

namespace Rge.Receipts.Application.ResendReceiptEmail;

public sealed record ResendReceiptEmailCommand(
    string PaymentId,
    string? OverrideRecipientEmail = null,
    bool AwaitAcceptance = false) : ICommand<ResendReceiptEmailResponse>;

public sealed record ResendReceiptEmailResponse(
    string PaymentId,
    string OfficialReceiptNumber,
    string RecipientEmail,
    bool Dispatched,
    bool Success = true,
    string? Message = null,
    string? Recipient = null,
    object? SmtpDelivery = null,
    ReceiptDto? Receipt = null);
