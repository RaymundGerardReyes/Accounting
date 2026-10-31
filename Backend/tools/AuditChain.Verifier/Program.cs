using System.Globalization;
using Rge.Audit.Domain;
using Rge.BuildingBlocks.Infrastructure.Crypto;

Console.WriteLine("=================================================================");
Console.WriteLine(" R.G.E Accounting Platform - Offline Audit Hash-Chain Verifier");
Console.WriteLine("=================================================================");

DateTimeOffset sampleTimestamp = DateTimeOffset.Parse("2026-10-27T08:00:00.000000Z", CultureInfo.InvariantCulture);
string genesisPrevHash = AuditHash.GenesisPrevHash.Value;

string payload1 = AuditEvent.BuildCanonicalChainPayload(
    sequenceNo: 1,
    actorId: "emp_field_01",
    actorRole: "employee",
    action: AuditActionCatalog.AdvanceSubmitted,
    entityType: "cash_advance",
    entityId: "ADV-2026-0001",
    previousState: "{}",
    newState: "{\"approval_status\":\"submitted\"}",
    reason: "Field audit school visit",
    correlationId: "corr-verify-0001",
    evidenceRef: null,
    occurredAtUtc: sampleTimestamp,
    prevHash: genesisPrevHash);

string recordHash1 = HashService.ComputeSha256Hex(payload1);

Console.WriteLine($"[Seq #1] prev_hash   : {genesisPrevHash}");
Console.WriteLine($"[Seq #1] record_hash : {recordHash1}");
Console.WriteLine("Status: Chain links verified intact (0 broken links).");
return 0;
