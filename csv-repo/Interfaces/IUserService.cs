using Amazon.CognitoIdentityProvider.Model;
using csv_repo.Models;

namespace csv_repo.Interfaces;

public interface IUserService
{
    Task LoginUserAsync(UserLoginModel request);

    Task LogoutUserAsync();

    Task<SignUpResponse> SignupUserAsync(UserSignupModel request);

    Task ConfirmUserAsync(UserConfirmModel request);
}