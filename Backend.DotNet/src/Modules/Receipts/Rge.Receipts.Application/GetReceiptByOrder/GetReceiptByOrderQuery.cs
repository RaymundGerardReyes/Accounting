using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Receipts.Application.IssueReceipt;

namespace Rge.Receipts.Application.GetReceiptByOrder;

public sealed record GetReceiptByOrderQuery(string OrderId) : IQuery<ReceiptDto>;
