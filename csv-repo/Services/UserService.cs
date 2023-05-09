using System.Security.Claims;
using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.Extensions.CognitoAuthentication;
using csv_repo.Interfaces;
using csv_repo.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace csv_repo.Services;

public class UserService : IUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AppConfig _appConfig;
    private readonly AmazonCognitoIdentityProviderClient _provider;
    private readonly CognitoUserPool _userPool;
    
    public UserService(IOptions<AppConfig> appConfig, IHttpContextAccessor httpContextAccessor)
    {
        var accessKeyId = Environment.GetEnvironmentVariable("AccessKeyId", EnvironmentVariableTarget.Machine);
        var accessSecretKey = Environment.GetEnvironmentVariable("AccessSecretKey", EnvironmentVariableTarget.Machine);
        
        _httpContextAccessor = httpContextAccessor;
        _appConfig = appConfig.Value;
        _provider = new AmazonCognitoIdentityProviderClient(accessKeyId, accessSecretKey,
            RegionEndpoint.GetBySystemName(_appConfig.Region));
        _userPool = new CognitoUserPool(_appConfig.UserPoolId, _appConfig.AppClientId, _provider);
    }

    public async Task LoginUserAsync(UserLoginModel request)
    {
        var user = new CognitoUser(request.Username, _appConfig.AppClientId, _userPool, _provider);

        var authRequest = new InitiateSrpAuthRequest()
        {
            Password = request.Password
        };

        var authResponse = await user.StartWithSrpAuthAsync(authRequest);
        var result = authResponse.AuthenticationResult;

        if (result.AccessToken != null)
        {
            await _httpContextAccessor.HttpContext.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim(ClaimTypes.NameIdentifier, request.Username),
                new Claim("CognitoToken", authResponse.AuthenticationResult.IdToken)
            }, CookieAuthenticationDefaults.AuthenticationScheme)));
        }
    }

    public async Task LogoutUserAsync()
    {
        await _httpContextAccessor.HttpContext.SignOutAsync();
    }

    public async Task<SignUpResponse> SignupUserAsync(UserSignupModel request)
    {
        var signupRequest = new SignUpRequest
        {
            ClientId = _appConfig.AppClientId,
            Password = request.Password,
            Username = request.Username
        };

        signupRequest.UserAttributes.Add(new AttributeType
        {
            Name = "email",
            Value = request.Email
        });

        signupRequest.UserAttributes.Add(new AttributeType
        {
            Name = "given_name",
            Value = request.Name
        });

        var response = await _provider.SignUpAsync(signupRequest);

        return response;
    }

    public async Task ConfirmUserAsync(UserConfirmModel request)
    {
        var confirmRequest = new ConfirmSignUpRequest
        {
            ClientId = _appConfig.AppClientId,
            Username = request.Username,
            ConfirmationCode = request.ConfirmationCode
        };
        var response = await _provider.ConfirmSignUpAsync(confirmRequest);
    }
}