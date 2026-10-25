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

// 8. Test Security Infrastructure (Pillars 1-3)
Console.WriteLine("\n--- Testing Security Infrastructure (Pillars 1-3) ---");
var (rawToken, tokenHash) = Rge.Identity.Infrastructure.SessionTokenHasher.CreateSessionToken();
Assert("Session token raw is non-empty and 43+ chars", !string.IsNullOrEmpty(rawToken) && rawToken.Length >= 43);
Assert("Session token hash matches re-hash", Rge.Identity.Infrastructure.SessionTokenHasher.HashToken(rawToken) == tokenHash);

byte[] testKey = new byte[32];
RandomNumberGenerator.Fill(testKey);
var encService = new Rge.BuildingBlocks.Infrastructure.Crypto.FieldEncryptionService(testKey);
string secretData = "0123-4567-8901 (BDO Bank Account)";
string cipher = encService.Encrypt(secretData, "account_aad");
Assert("FieldEncryptionService ciphertext starts with v1:", cipher.StartsWith("v1:", StringComparison.Ordinal));
string decrypted = encService.Decrypt(cipher, "account_aad");
Assert("FieldEncryptionService decrypts to original plaintext", decrypted == secretData);

byte[] pdfHeader = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];
var magicRes = Rge.Documents.Infrastructure.MagicByteValidator.ValidateAndCreateSafeName("invoice.pdf", 1024, pdfHeader);
Assert("MagicByteValidator detects application/pdf", magicRes.IsSuccess && magicRes.Value.DetectedMimeType == "application/pdf");

var traversalRes = Rge.Documents.Infrastructure.MagicByteValidator.ValidateAndCreateSafeName("../evil.pdf", 1024, pdfHeader);
Assert("MagicByteValidator blocks path traversal", !traversalRes.IsSuccess && traversalRes.Error.Code == "Documents.PathTraversalDetected");

string escapedFormula = Rge.Finance.Infrastructure.Reporting.CsvInjectionSafeWriter.EscapeCell("=cmd|'/C calc'!A0");
Assert("CsvInjectionSafeWriter neutralizes = formula prefix", escapedFormula.StartsWith("'=", StringComparison.Ordinal));

// 9. Test PayMongo Payment Attempt & Webhook Extraction
Console.WriteLine("\n--- Testing PayMongo PaymentAttempt & Webhook Extraction ---");
var attempt = Rge.Payments.Domain.PaymentAttempts.PaymentAttempt.CreatePending(
    "order-test-1", "qrph", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(150_00), "idem-key-1", DateTimeOffset.UtcNow);
Assert("PaymentAttempt initiates in Pending status", attempt.Status == Rge.Payments.Domain.PaymentAttempts.PaymentAttemptStatus.Pending);

var attachRes = attempt.AttachCheckoutSession("cs_test_123", "https://checkout.paymongo.com/cs_test_123", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);
Assert("PaymentAttempt attaches checkout session and moves to RequiresAction", attachRes.IsSuccess && attempt.Status == Rge.Payments.Domain.PaymentAttempts.PaymentAttemptStatus.RequiresAction);

var succeedRes = attempt.MarkSucceeded(DateTimeOffset.UtcNow, "qrph");
Assert("PaymentAttempt moves from RequiresAction to Succeeded", succeedRes.IsSuccess && attempt.Status == Rge.Payments.Domain.PaymentAttempts.PaymentAttemptStatus.Succeeded);

string sampleWebhookPayload = """
{
  "data": {
    "id": "evt_test_999",
    "type": "event",
    "attributes": {
      "type": "checkout_session.payment.paid",
      "livemode": false,
      "created_at": 1729845000,
      "data": {
        "id": "cs_test_123",
        "type": "checkout_session",
        "attributes": {
          "status": "paid"
        }
      }
    }
  }
}
""";

var webhookHandler = new Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook.ReceivePayMongoWebhookHandler();
var webhookCmd = new Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook.ReceivePayMongoWebhookCommand(null, sampleWebhookPayload);
var webhookTask = webhookHandler.HandleAsync(webhookCmd);
webhookTask.Wait();
var webhookRes = webhookTask.Result;
Assert("ReceivePayMongoWebhookHandler parses evt_test_999 and returns received", webhookRes.IsSuccess && webhookRes.Value.ProviderEventId == "evt_test_999" && webhookRes.Value.ProcessingStatus == "received");

// 10. Test Tests.Common Infrastructure (FakeClock, WebhookSigner, Builders, Generators)
Console.WriteLine("\n--- Testing Tests.Common Infrastructure ---");
var fakeClock = new Rge.Tests.Common.Time.FakeClock(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero));
fakeClock.Advance(TimeSpan.FromHours(8));
Assert("FakeClock advances time deterministically", fakeClock.UtcNow.Hour == 8);

string validSigHeader = Rge.Tests.Common.Webhooks.WebhookSigner.CreateSignatureHeader(
    sampleWebhookPayload, "whsec_test_abc", 1729845000, isLive: false, tamper: false);
Assert("WebhookSigner produces test signature header format (te=)", validSigHeader.Contains("te=", StringComparison.Ordinal) && validSigHeader.Contains("t=1729845000", StringComparison.Ordinal));

var builtAttempt = new Rge.Tests.Common.Builders.PaymentAttemptBuilder()
    .WithOrderId("ord-builder-1")
    .WithPaymentMethod("gcash")
    .BuildSucceeded("cs_builder_999");
Assert("PaymentAttemptBuilder constructs aggregate in Succeeded state", builtAttempt.Status == Rge.Payments.Domain.PaymentAttempts.PaymentAttemptStatus.Succeeded && builtAttempt.OrderId == "ord-builder-1");

var builtOrder = new Rge.Tests.Common.Builders.OrderBuilder()
    .WithOrderNumber("ORD-TEST-0001")
    .WithAmount(Rge.BuildingBlocks.SharedKernel.Money.Money.Php(2500_00))
    .BuildPaid();
Assert("OrderBuilder constructs aggregate in Paid state with correct amount", builtOrder.Status == "paid" && builtOrder.Amount.AmountMinor == 2500_00);

var idGen = new Rge.Tests.Common.Generators.FakeIdGenerator();
var id1 = idGen.NextId("adv");
var id2 = idGen.NextId("adv");
Assert("FakeIdGenerator generates monotonic padded IDs", id1 == "adv-000001" && id2 == "adv-000002");

var seededRand1 = new Rge.Tests.Common.Generators.DeterministicRandom(12345);
var seededRand2 = new Rge.Tests.Common.Generators.DeterministicRandom(12345);
Assert("DeterministicRandom produces identical sequences across runs", seededRand1.Next(1, 1000) == seededRand2.Next(1, 1000) && seededRand1.NextLong(100, 10000) == seededRand2.NextLong(100, 10000));

var ledgerEntries = new List<Rge.Finance.Domain.Ledger.AdvanceLedgerEntry>
{
    new Rge.Tests.Common.Builders.AdvanceLedgerEntryBuilder().AsRelease(Rge.BuildingBlocks.SharedKernel.Money.Money.Php(15000_00)).Build(),
    new Rge.Tests.Common.Builders.AdvanceLedgerEntryBuilder().AsExpenseApplied(Rge.BuildingBlocks.SharedKernel.Money.Money.Php(12000_00)).Build(),
    new Rge.Tests.Common.Builders.AdvanceLedgerEntryBuilder().AsCashReturn(Rge.BuildingBlocks.SharedKernel.Money.Money.Php(3000_00)).Build()
};
var derivedBalance = Rge.Finance.Domain.Ledger.AdvanceBalanceCalculator.CalculateOutstandingBalance(ledgerEntries);
Assert("AdvanceLedgerEntryBuilder and AdvanceBalanceCalculator derive zero balance on exact return", derivedBalance.AmountMinor == 0);

// 11. Test Four-Eyes Separation of Duties (INV-SOD-001)
Console.WriteLine("\n--- Testing Four-Eyes Separation of Duties (INV-SOD-001) ---");
var sodSelfApproval = Rge.Finance.Domain.Disbursements.FourEyesRule.EnsureRequestorIsNotApprover("user_alice", "user_alice");
Assert("Requestor cannot approve own request", sodSelfApproval.IsFailure && sodSelfApproval.Error.Code == "Finance.SoD.SelfApprovalForbidden");

var sodDistinctApprover = Rge.Finance.Domain.Disbursements.FourEyesRule.EnsureRequestorIsNotApprover("user_alice", "user_bob");
Assert("Distinct approver succeeds", sodDistinctApprover.IsSuccess);

var sodDisbursementConflict = Rge.Finance.Domain.Disbursements.FourEyesRule.EnsureDistinctDisbursementActors(
    makerId: "actor_1", verifierId: "actor_1");
Assert("Maker cannot act as Verifier on disbursement", sodDisbursementConflict.IsFailure && sodDisbursementConflict.Error.Code == "Finance.SoD.MakerCannotVerify");

var sodDisbursementBankConflict = Rge.Finance.Domain.Disbursements.FourEyesRule.EnsureDistinctDisbursementActors(
    makerId: "actor_1", verifierId: "actor_2", bankApproverId: "actor_1");
Assert("Bank Approver cannot match Maker", sodDisbursementBankConflict.IsFailure && sodDisbursementBankConflict.Error.Code == "Finance.SoD.BankApproverConflict");

var sodFourEyesSuccess = Rge.Finance.Domain.Disbursements.FourEyesRule.EnsureDistinctDisbursementActors(
    makerId: "actor_1", verifierId: "actor_2", bankApproverId: "actor_3", evidenceVerifierId: "actor_4");
Assert("Four distinct actors on disbursement succeeds", sodFourEyesSuccess.IsSuccess);

// 12. Test Additional CashAdvance State Transitions
Console.WriteLine("\n--- Testing State Transition Tables (All 5 Machines) ---");
var payTransOk = Rge.Finance.Domain.CashAdvances.StateMachines.StateTransitionTables.TransitionPayment(
    Rge.Finance.Domain.CashAdvances.StateMachines.PaymentStatus.Prepared,
    Rge.Finance.Domain.CashAdvances.StateMachines.PaymentStatus.Verified);
Assert("PaymentStatus Prepared -> Verified succeeds", payTransOk.IsSuccess);

var payTransFail = Rge.Finance.Domain.CashAdvances.StateMachines.StateTransitionTables.TransitionPayment(
    Rge.Finance.Domain.CashAdvances.StateMachines.PaymentStatus.NotStarted,
    Rge.Finance.Domain.CashAdvances.StateMachines.PaymentStatus.EvidenceVerified);
Assert("PaymentStatus NotStarted -> EvidenceVerified fails", payTransFail.IsFailure);

var liqTransOk = Rge.Finance.Domain.CashAdvances.StateMachines.StateTransitionTables.TransitionLiquidation(
    Rge.Finance.Domain.CashAdvances.StateMachines.LiquidationReviewStatus.Submitted,
    Rge.Finance.Domain.CashAdvances.StateMachines.LiquidationReviewStatus.FinanceReview);
Assert("LiquidationReviewStatus Submitted -> FinanceReview succeeds", liqTransOk.IsSuccess);

var settleTransOk = Rge.Finance.Domain.CashAdvances.StateMachines.StateTransitionTables.TransitionSettlement(
    Rge.Finance.Domain.CashAdvances.StateMachines.SettlementStatus.Open,
    Rge.Finance.Domain.CashAdvances.StateMachines.SettlementStatus.ShortfallReceivable);
Assert("SettlementStatus Open -> ShortfallReceivable succeeds", settleTransOk.IsSuccess);

// 13. Test Approval Matrix & Router Domain Invariants
Console.WriteLine("\n--- Testing Approval Matrix & Router (Specification Parity) ---");
var matrixList = new List<Rge.Finance.Domain.Approvals.ApprovalMatrix>
{
    Rge.Finance.Domain.Approvals.ApprovalMatrix.Create(
        workflow: "cash_advance",
        stage: "business_approval",
        role: "supervisor",
        minAmount: Rge.BuildingBlocks.SharedKernel.Money.Money.Zero(),
        maxAmount: Rge.BuildingBlocks.SharedKernel.Money.Money.Php(50_000_00),
        departmentScope: "*"),
    Rge.Finance.Domain.Approvals.ApprovalMatrix.Create(
        workflow: "cash_advance",
        stage: "business_approval",
        role: "finance_controller",
        minAmount: Rge.BuildingBlocks.SharedKernel.Money.Money.Zero(),
        maxAmount: Rge.BuildingBlocks.SharedKernel.Money.Money.Php(500_000_00),
        departmentScope: "*"),
    Rge.Finance.Domain.Approvals.ApprovalMatrix.Create(
        workflow: "disbursement_bank",
        stage: "bank_approval",
        role: "bank_approver",
        minAmount: Rge.BuildingBlocks.SharedKernel.Money.Money.Zero(),
        maxAmount: Rge.BuildingBlocks.SharedKernel.Money.Money.Php(200_000_00),
        departmentScope: "*")
};

string[] sysAdminRoles = ["system_admin"];
string[] supervisorRoles = ["supervisor"];
string[] controllerRoles = ["finance_controller"];
string[] auditorRoles = ["auditor"];

// System Admin without financial role has zero approval authority
var adminAttempt = Rge.Finance.Domain.Approvals.ApprovalRouter.RouteApproval(
    matrixEntries: matrixList,
    actorId: "sys_admin_1",
    actorRoles: sysAdminRoles,
    workflow: "cash_advance",
    stage: "business_approval",
    amountMinor: 10_000_00);
Assert("System Admin without financial role is rejected from financial approvals",
    adminAttempt.IsFailure && adminAttempt.Error.Code == "Finance.SoD.AdminNoApprovalAuthority");

// Supervisor approves within threshold (PHP 30,000 <= PHP 50,000)
var supervisorOk = Rge.Finance.Domain.Approvals.ApprovalRouter.RouteApproval(
    matrixEntries: matrixList,
    actorId: "sup_1",
    actorRoles: supervisorRoles,
    workflow: "cash_advance",
    stage: "business_approval",
    amountMinor: 30_000_00);
Assert("Supervisor approves within threshold (PHP 30,000 <= PHP 50,000)", supervisorOk.IsSuccess);

// Supervisor exceeds threshold (PHP 80,000 > PHP 50,000)
var supervisorExceeded = Rge.Finance.Domain.Approvals.ApprovalRouter.RouteApproval(
    matrixEntries: matrixList,
    actorId: "sup_1",
    actorRoles: supervisorRoles,
    workflow: "cash_advance",
    stage: "business_approval",
    amountMinor: 80_000_00);
Assert("Supervisor exceeds threshold (PHP 80,000 > PHP 50,000) and is rejected",
    supervisorExceeded.IsFailure && supervisorExceeded.Error.Code == "Finance.Approval.LimitExceeded");

// Finance Controller approves large amount (PHP 80,000 <= PHP 500,000)
var controllerOk = Rge.Finance.Domain.Approvals.ApprovalRouter.RouteApproval(
    matrixEntries: matrixList,
    actorId: "ctrl_1",
    actorRoles: controllerRoles,
    workflow: "cash_advance",
    stage: "business_approval",
    amountMinor: 80_000_00);
Assert("Finance Controller approves within upper threshold (PHP 80,000 <= PHP 500,000)", controllerOk.IsSuccess);

// Unknown role has no authority
var unknownRole = Rge.Finance.Domain.Approvals.ApprovalRouter.RouteApproval(
    matrixEntries: matrixList,
    actorId: "viewer_1",
    actorRoles: auditorRoles,
    workflow: "cash_advance",
    stage: "business_approval",
    amountMinor: 10_000_00);
Assert("Auditor role has no authority for cash_advance business_approval",
    unknownRole.IsFailure && unknownRole.Error.Code == "Finance.Approval.NoAuthority");

// 14. Test ApprovalHistoryEntry Record Creation
var historyEntry = Rge.Finance.Domain.Approvals.ApprovalHistoryEntry.Create(
    entityType: "cash_advance",
    entityId: "adv-001",
    approverId: "sup_1",
    approverRole: "supervisor",
    approvalStage: "business_approval",
    decision: "approved",
    previousStatus: "submitted",
    resultingStatus: "approved",
    comments: "Approved for client audit engagement.",
    approvedAmountMinor: 30_000_00);
Assert("ApprovalHistoryEntry records decision with approved amount and audit fields",
    historyEntry.Decision == "approved" && historyEntry.ApprovedAmountMinor == 30_000_00 && historyEntry.PreviousStatus == "submitted");

// 15. Test JsonDefaults and Serialization Context
Console.WriteLine("\n--- Testing API Serialization & Filter Defaults ---");
var optionsInstance = Rge.BuildingBlocks.Api.Serialization.JsonDefaults.Options;
Assert("JsonDefaults.Options configures snake_case property naming",
    optionsInstance.PropertyNamingPolicy == System.Text.Json.JsonNamingPolicy.SnakeCaseLower);


Console.WriteLine("\n==============================================================");
Console.WriteLine($" RESULTS: {passed} PASSED, {failed} FAILED");
Console.WriteLine("==============================================================");

if (failed > 0)
{
    Environment.Exit(1);
}

