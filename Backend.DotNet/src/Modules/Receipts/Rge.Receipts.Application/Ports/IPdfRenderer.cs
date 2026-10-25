using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.Ports;

/// <summary>
/// Renders a BIR Official Receipt and Treasury Acknowledgement into binary PDF bytes.
/// </summary>
public interface IPdfRenderer
{
    byte[] RenderOfficialReceiptPdf(OfficialReceipt receipt);
}
