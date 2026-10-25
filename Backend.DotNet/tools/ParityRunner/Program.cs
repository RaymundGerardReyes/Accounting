using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Api.Serialization;
using Rge.BuildingBlocks.Infrastructure.Crypto;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.CashAdvances.StateMachines;
using Rge.Finance.Domain.Ledger;

Console.WriteLine("==============================================================");
Console.WriteLine(" R.G.E Accounting Platform - Phase 0 Invariant & Parity Runner");
Console.WriteLine("==============================================================");

int passed = 0;
int failed = 0;

void Assert(string testName, bool condition, string detail = "")
{
    if (condition)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[PASS] {testName}");
        passed++;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] {testName}: {detail}");
        failed++;
    }
    Console.ResetColor();
}

// 1. Test Rounding.DivideHalfUp
Console.WriteLine("\n--- Testing Rounding.DivideHalfUp ---");
Assert("Half-up standard division (150/100 == 2)", Rounding.DivideHalfUp(150, 100) == 2);
Assert("Half-up round down (125/100 == 1)", Rounding.DivideHalfUp(125, 100) == 1);
Assert("Odd denominator 1/3 == 0", Rounding.DivideHalfUp(1, 3) == 0);
Assert("Odd denominator 2/3 == 1", Rounding.DivideHalfUp(2, 3) == 1);
Assert("Odd denominator 5/3 == 2", Rounding.DivideHalfUp(5, 3) == 2);
Assert("Negative half-up (-150/100 == -2)", Rounding.DivideHalfUp(-150, 100) == -2);
Assert("Negative odd denominator (-2/3 == -1)", Rounding.DivideHalfUp(-2, 3) == -1);

bool overflowSafe = true;
try
{
    long largeNum = long.MaxValue - 50;
    long result = Rounding.DivideHalfUp(largeNum, 100);
    overflowSafe = result > 0;
}
catch (OverflowException)
{
    overflowSafe = false;
}
Assert("Large numerator overflow safety near long.MaxValue", overflowSafe);

// 2. Test VatCalculator
Console.WriteLine("\n--- Testing VatCalculator ---");
var vat112 = VatCalculator.ComputeVatInclusive(new Money(11200, "PHP"));
Assert("VATable sales on 112.00 is 100.00", vat112.VatableSales.AmountMinor == 10000);
Assert("VAT amount on 112.00 is 12.00", vat112.VatAmount.AmountMinor == 1200);
Assert("VatableSales + VatAmount == Gross", (vat112.VatableSales.AmountMinor + vat112.VatAmount.AmountMinor) == 11200);

var vat100 = VatCalculator.ComputeVatInclusive(new Money(10000, "PHP"));
Assert("Exact centavo conservation on odd Gross (100.00)", (vat100.VatableSales.AmountMinor + vat100.VatAmount.AmountMinor) == 10000);

// 3. Test AdvanceBalanceCalculator
Console.WriteLine("\n--- Testing AdvanceBalanceCalculator ---");
var entries = new List<AdvanceLedgerEntry>
{
    new("entry-1", "adv-1", "emp-1", LedgerEntryType.DisbursementRelease, new Money(50000, "PHP"), 50000, "actor-1", DateTimeOffset.UtcNow),
    new("entry-2", "adv-1", "emp-1", LedgerEntryType.ExpenseApplied, new Money(30000, "PHP"), -30000, "actor-1", DateTimeOffset.UtcNow),
    new("entry-3", "adv-1", "emp-1", LedgerEntryType.CashReturn, new Money(20000, "PHP"), -20000, "actor-1", DateTimeOffset.UtcNow)
};
var bal1 = AdvanceBalanceCalculator.CalculateOutstandingBalance(entries);
Assert("Full liquidation balance reaches zero centavos", bal1.AmountMinor == 0);

var overspendEntries = new List<AdvanceLedgerEntry>
{
    new("entry-4", "adv-2", "emp-1", LedgerEntryType.DisbursementRelease, new Money(10000, "PHP"), 10000, "actor-1", DateTimeOffset.UtcNow),
    new("entry-5", "adv-2", "emp-1", LedgerEntryType.ExpenseApplied, new Money(15000, "PHP"), -15000, "actor-1", DateTimeOffset.UtcNow)
};
var balOverspend = AdvanceBalanceCalculator.CalculateOutstandingBalance(overspendEntries);
Assert("Overspend drives advance balance negative (-5000)", balOverspend.AmountMinor == -5000);

overspendEntries.Add(new("entry-6", "adv-2", "emp-1", LedgerEntryType.ReimbursementPayable, new Money(5000, "PHP"), 5000, "actor-1", DateTimeOffset.UtcNow));
var balRestored = AdvanceBalanceCalculator.CalculateOutstandingBalance(overspendEntries);
Assert("ReimbursementPayable restores advance balance to zero", balRestored.AmountMinor == 0);

// 4. Test StateTransitionTables
Console.WriteLine("\n--- Testing StateTransitionTables ---");
var postValid = StateTransitionTables.TransitionPosting(PostingStatus.NotApplicable, PostingStatus.Posted);
Assert("Posting transition NotApplicable -> Posted succeeds", postValid.IsSuccess);

var postInvalid = StateTransitionTables.TransitionPosting(PostingStatus.Posted, PostingStatus.NotApplicable);
Assert("Posting transition Posted -> NotApplicable fails", postInvalid.IsFailure);

var appValid = StateTransitionTables.TransitionApproval(ApprovalStatus.Draft, ApprovalStatus.Submitted);
Assert("Approval transition Draft -> Submitted succeeds", appValid.IsSuccess);

var appInvalid = StateTransitionTables.TransitionApproval(ApprovalStatus.Draft, ApprovalStatus.Approved);
Assert("Approval transition Draft -> Approved (self-approval bypass) fails", appInvalid.IsFailure);

// 5. Test HmacVerifier & ConstantTimeComparer
Console.WriteLine("\n--- Testing HmacVerifier & ConstantTimeComparer ---");
string secret = "whsec_test_secret_key_12345";
string body = "{\"event\":\"payment.paid\",\"id\":\"evt_123\"}";
long unixNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
string toSign = $"{unixNow}.{body}";
byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(secret);
byte[] hashBytes = HMACSHA256.HashData(keyBytes, System.Text.Encoding.UTF8.GetBytes(toSign));
string validSig = Convert.ToHexString(hashBytes).ToUpperInvariant();
string header = $"t={unixNow},te={validSig}";

bool verified = HmacVerifier.VerifyPayMongoSignature(header, body, secret, DateTimeOffset.UtcNow);
Assert("PayMongo valid signature verifies successfully", verified);

bool tampered = HmacVerifier.VerifyPayMongoSignature(header, body + " ", secret, DateTimeOffset.UtcNow);
Assert("Tampered payload body is rejected", !tampered);

bool stale = HmacVerifier.VerifyPayMongoSignature(header, body, secret, DateTimeOffset.UtcNow.AddMinutes(10));
Assert("Expired timestamp beyond skew tolerance is rejected", !stale);

// 6. Test CanonicalJson & HashService
Console.WriteLine("\n--- Testing CanonicalJson & HashService ---");
var obj1 = new { b = 2, a = 1, c = new { z = 26, y = 25 } };
var obj2 = new { c = new { y = 25, z = 26 }, a = 1, b = 2 };
string json1 = CanonicalJson.SerializeDeterministic(obj1);
string json2 = CanonicalJson.SerializeDeterministic(obj2);
Assert("CanonicalJson produces deterministic key-sorted output", json1 == json2, $"{json1} vs {json2}");

string birDigest = HashService.ComputeBirReceiptDigestV2("OR-2026-0001|PHP|10000");
Assert("BIR Receipt Digest starts with sha256:v2:", birDigest.StartsWith("sha256:v2:", StringComparison.Ordinal));

string fp1 = HashService.ComputeExpenseFingerprint("2026-10-25", "MEALS", 50000, "INV-999");
string fp2 = HashService.ComputeExpenseFingerprint("2026-10-25", "meals", 50000, "inv-999");
Assert("Expense line fingerprint is case-insensitive normalized", fp1 == fp2);

// 7. Test ProblemDetailsFactory & JsonDefaults
Console.WriteLine("\n--- Testing ProblemDetailsFactory & JsonDefaults ---");
var httpCtx = new DefaultHttpContext();
httpCtx.Request.Path = "/api/v1/payments";
httpCtx.Request.Headers[ProblemDetailsFactory.CorrelationIdHeader] = "corr-test-123";

var probVal = ProblemDetailsFactory.Create(httpCtx, Error.Validation("ERR_VAL", "Invalid input"));
Assert("ProblemDetails maps Validation to 400", probVal.Status == StatusCodes.Status400BadRequest);
Assert("ProblemDetails carries correlation_id", probVal.Extensions["correlation_id"]?.ToString() == "corr-test-123");

var probSod = ProblemDetailsFactory.Create(httpCtx, Error.SodViolation("ERR_SOD", "Maker cannot verify"));
Assert("ProblemDetails maps SodViolation to 403", probSod.Status == StatusCodes.Status403Forbidden);

var probPol = ProblemDetailsFactory.Create(httpCtx, Error.PolicyViolation("ERR_POL", "Over limit"));
Assert("ProblemDetails maps PolicyViolation to 422", probPol.Status == StatusCodes.Status422UnprocessableEntity);

var jsonOpts = new JsonSerializerOptions();
JsonDefaults.ConfigureOptions(jsonOpts);
var sample = new { AmountMinor = 10000, OrderNumber = "ORD-001" };
string snakeJson = JsonSerializer.Serialize(sample, jsonOpts);
Assert("JsonDefaults serializes snake_case keys", snakeJson.Contains("\"amount_minor\":10000", StringComparison.Ordinal) && snakeJson.Contains("\"order_number\":\"ORD-001\"", StringComparison.Ordinal));

Console.WriteLine("\n==============================================================");
Console.WriteLine($" RESULTS: {passed} PASSED, {failed} FAILED");
Console.WriteLine("==============================================================");

if (failed > 0)
{
    Environment.Exit(1);
}

