using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Receipts.Application.IssueReceipt;

namespace Rge.Receipts.Application.GetReceiptByPayment;

public sealed record GetReceiptByPaymentQuery(string PaymentId) : IQuery<ReceiptDto>;

