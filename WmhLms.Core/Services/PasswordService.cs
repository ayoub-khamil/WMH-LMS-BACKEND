using Microsoft.AspNetCore.Identity;
using WmhLms.Data.Entities;

namespace WmhLms.Core.Services;

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(User user, string password);
}

public class PasswordService : IPasswordService
{
    private readonly PasswordHasher<User> _hasher = new();
    public string Hash(string password) =>
        _hasher.HashPassword(null!, password);
    public bool Verify(User user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password)
        == PasswordVerificationResult.Success;
}
