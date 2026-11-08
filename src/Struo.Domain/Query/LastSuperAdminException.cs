// src/Struo.Domain/Query/LastSuperAdminException.cs
namespace Struo.Domain.Query;

/// <summary>
/// A write would leave no active account in a super-admin role. Surfaced as HTTP 409 with the
/// <c>LAST_SUPER_ADMIN</c> code, distinct from <see cref="ConflictException"/>'s generic
/// <c>CONFLICT</c> so a client can explain this refusal on its own.
/// </summary>
public sealed class LastSuperAdminException(string message) : Exception(message);
