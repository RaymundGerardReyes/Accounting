using System.Text;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Documents.Application.Ports;

namespace Rge.Documents.Infrastructure;

public sealed class AntivirusScanHook : IAntivirusScanner
{
    private static readonly byte[] EicarSignature =
        Encoding.ASCII.GetBytes("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

    public Task<Result> ScanBytesAsync(
        ReadOnlyMemory<byte> fileBytes,
        string filename,
        CancellationToken cancellationToken = default)
    {
        if (fileBytes.Span.IndexOf(EicarSignature) >= 0)
        {
            return Task.FromResult(Result.Failure(
                Error.Validation("Documents.MalwareDetected", $"Malware signature detected in uploaded file '{filename}'.")));
        }

        return Task.FromResult(Result.Success());
    }
}

