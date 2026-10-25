using System.Diagnostics.CodeAnalysis;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Audit.Domain;

/// <summary>
/// Cryptographic SHA-256 hash value (64-character lowercase hex string) representing a tamper-evident audit node.
/// </summary>
public readonly record struct AuditHash : IEquatable<AuditHash>
{
    public static readonly AuditHash Genesis = new(new string('0', 64));

    public string Value { get; }

    public AuditHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64)
        {
            throw new ArgumentException("Audit hash must be exactly 64 hexadecimal characters.", nameof(value));
        }

        Value = value.ToLowerInvariant();
    }

    public static Result<AuditHash> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<AuditHash>(Error.Validation("AuditHash.Empty", "Audit hash value cannot be empty."));
        }

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64)
        {
            return Result.Failure<AuditHash>(Error.Validation("AuditHash.InvalidLength", "Audit hash must be exactly 64 characters."));
        }

        foreach (char c in normalized)
        {
            bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isHex)
            {
                return Result.Failure<AuditHash>(Error.Validation("AuditHash.InvalidCharacters", "Audit hash must contain only hexadecimal characters."));
            }
        }

        return Result.Success(new AuditHash(normalized));
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out AuditHash? result)
    {
        var res = Create(value);
        if (res.IsSuccess)
        {
            result = res.Value;
            return true;
        }

        result = null;
        return false;
    }

    public override string ToString() => Value;

    public static implicit operator string(AuditHash hash) => hash.Value;
}

