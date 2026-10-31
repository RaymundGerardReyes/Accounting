using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Finance.Api.Contracts;
using Rge.Finance.Application.TaxCodes.List;
using TaxCodesRegister = Rge.Finance.Application.TaxCodes.Register;
using Rge.Finance.Application.Vendors.DecideBankChange;
using Rge.Finance.Application.Vendors.List;
using VendorsRegister = Rge.Finance.Application.Vendors.Register;
using Rge.Finance.Application.Vendors.RequestBankChange;
using Rge.Finance.Application.Vendors.Verify;

namespace Rge.Finance.Api.Vendors;

/// <summary>
/// Minimal API endpoints for Vendors, Vendor Bank Changes, and Tax Codes (FIN-050, FIN-051, FIN-054 through FIN-058).
/// </summary>
public sealed class VendorEndpoints : IEndpointModule
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder vendorsGroup = app.MapVersionedApiGroup("/api/v1/finance/vendors", "Vendors");
        RouteGroupBuilder bankChangesGroup = app.MapVersionedApiGroup("/api/v1/finance/vendor-bank-changes", "VendorBankChanges");
        RouteGroupBuilder taxCodesGroup = app.MapVersionedApiGroup("/api/v1/finance/tax-codes", "TaxCodes");

        // FIN-050: GET /api/v1/finance/tax-codes
        taxCodesGroup.MapGet("/", async (
            HttpContext httpContext,
            Application.TaxCodes.List.ListHandler handler,
            CancellationToken ct) =>
        {
            var query = new ListTaxCodesQuery();
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-051: POST /api/v1/finance/tax-codes
        taxCodesGroup.MapPost("/", async (
            RegisterTaxCodeApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            TaxCodesRegister.RegisterHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new TaxCodesRegister.RegisterTaxCodeCommand(
                Code: request.Code,
                Description: request.Description,
                TaxType: request.TaxType,
                RateBps: request.RateBps,
                RequiresBir2307: request.RequiresBir2307,
                CpaApproved: request.CpaApproved,
                ApprovedById: request.CpaApproved ? actor.ActorId : null);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-054: POST /api/v1/finance/vendor-bank-changes/{change_id}/decide
        bankChangesGroup.MapPost("/{change_id}/decide", async (
            string change_id,
            DecideBankChangeApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            DecideBankChangeHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new DecideVendorBankChangeCommand(
                ChangeId: change_id,
                ApproverId: actor.ActorId,
                Decision: request.Decision);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-055: GET /api/v1/finance/vendors
        vendorsGroup.MapGet("/", async (
            string? status,
            int? limit,
            HttpContext httpContext,
            Application.Vendors.List.ListHandler handler,
            CancellationToken ct) =>
        {
            var query = new ListVendorsQuery(status, limit ?? 50);
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-056: POST /api/v1/finance/vendors
        vendorsGroup.MapPost("/", async (
            RegisterVendorApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            VendorsRegister.RegisterHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new VendorsRegister.RegisterVendorCommand(
                RegisteredName: request.RegisteredName,
                PayeeType: request.PayeeType,
                PaymentMethod: request.PaymentMethod,
                CreatedById: actor.ActorId,
                Tin: request.Tin,
                Address: request.Address,
                ContactPerson: request.ContactPerson,
                ContactEmail: request.ContactEmail,
                BankName: request.BankName,
                RawAccountNumber: request.RawAccountNumber,
                BankAccountRef: request.BankAccountRef,
                KycDocumentId: request.KycDocumentId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-057: POST /api/v1/finance/vendors/{vendor_id}/bank-changes
        vendorsGroup.MapPost("/{vendor_id}/bank-changes", async (
            string vendor_id,
            RequestBankChangeApiRequest request,
            HttpContext httpContext,
            ICurrentActor actor,
            RequestBankChangeHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new RequestVendorBankChangeCommand(
                VendorId: vendor_id,
                NewBankName: request.NewBankName,
                RawNewAccountNumber: request.RawNewAccountNumber,
                NewBankAccountRef: request.NewBankAccountRef,
                Reason: request.Reason,
                RequestorId: actor.ActorId,
                SupportingDocumentId: request.SupportingDocId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });

        // FIN-058: POST /api/v1/finance/vendors/{vendor_id}/verify
        vendorsGroup.MapPost("/{vendor_id}/verify", async (
            string vendor_id,
            HttpContext httpContext,
            ICurrentActor actor,
            VerifyHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new VerifyVendorCommand(
                VendorId: vendor_id,
                VerifierId: actor.ActorId);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        });
    }
}
