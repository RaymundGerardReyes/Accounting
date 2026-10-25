using Rge.Audit.Infrastructure;
using Rge.BuildingBlocks.Infrastructure.Persistence;

Console.WriteLine("==============================================================");
Console.WriteLine(" R.G.E Accounting Platform - Cryptographic Audit Chain Verifier");
Console.WriteLine("==============================================================");

try
{
    await using var connectionScope = new ConnectionScope();
    var verifier = new ChainVerifier(connectionScope);

    var result = await verifier.VerifyChainAsync().ConfigureAwait(false);

    if (result.Valid)
    {
        Console.WriteLine($"[PASS] Audit chain integrity verified successfully.");
        Console.WriteLine($"Total Events: {result.EventCount}");
        Console.WriteLine($"Head Hash:    {result.HeadHash}");
        return 0;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] Audit chain integrity check failed!");
        Console.WriteLine($"Broken at sequence: {result.BrokenAtSequence}");
        Console.WriteLine($"Failure reason:     {result.Reason}");
        Console.ResetColor();
        return 1;
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[ERROR] Unhandled exception during audit verification: {ex.Message}");
    Console.ResetColor();
    return 2;
}
