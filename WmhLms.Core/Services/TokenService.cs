using WmhLms.Data.Entities;

namespace WmhLms.Core.Services;

public interface ITokenService
{
    string Create(User user);
}
