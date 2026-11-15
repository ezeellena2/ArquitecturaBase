using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>La cuenta que dejó <see cref="UserContactLinker.CreateOrRestoreAsync"/>, y si era una borrada.</summary>
internal sealed record CreatedAccount(UserAccount Account, bool Restored);
