namespace Rge.Payments.Domain.PaymentAttempts;

/// <summary>
/// Canonical payment methods and channel resolvers for PayMongo and local simulation rails.
/// </summary>
public static class PaymentMethod
{
    public const string Card = "card";
    public const string Gcash = "gcash";
    public const string Maya = "paymaya";
    public const string Dob = "dob";
    public const string QrPh = "qrph";
    public const string HostedCheckout = "hosted_checkout";

    public static string Normalize(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            return HostedCheckout;
        }

        string clean = method.Trim().ToLowerInvariant();
        if (clean == "card" || clean.StartsWith("card_", StringComparison.Ordinal))
        {
            return Card;
        }
        if (clean == "gcash")
        {
            return Gcash;
        }
        if (clean is "paymaya" or "maya")
        {
            return Maya;
        }
        if (clean is "gcash_maya" or "ewallet" or "e_wallet" or "wallets")
        {
            return "gcash_maya";
        }
        if (clean == "dob" || clean.StartsWith("dob_", StringComparison.Ordinal) || clean is "bpi" or "ubp" or "unionbank" or "online_banking" or "bank_transfer")
        {
            return Dob;
        }
        if (clean is "qrph" or "qr" or "qr_code" or "ph_qr")
        {
            return QrPh;
        }

        return HostedCheckout;
    }
}

