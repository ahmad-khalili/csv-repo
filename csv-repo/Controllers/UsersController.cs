using Amazon.CognitoIdentityProvider.Model;
using csv_repo.Interfaces;
using csv_repo.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace csv_repo.Controllers;

[Controller]
[Route("users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> LoginUser(UserLoginModel request)
    {
        try
        {
            await _userService.LoginUserAsync(request);

            return NoContent();
        }
        
        catch (Amazon.CognitoIdentityProvider.Model.NotAuthorizedException ex)
        {
            return Unauthorized(ex.Message);
        }
        
        catch (Exception)
        {
            return Problem();
        }
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> LogoutUser()
    {
        await _userService.LogoutUserAsync();
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<ActionResult<string>> SignupUser(UserSignupModel request)
    {
        try
        {
            var response = await _userService.SignupUserAsync(request);

            return
                Ok(
                    $"Confirmation Code sent to {response.CodeDeliveryDetails.Destination} via {response.CodeDeliveryDetails.DeliveryMedium.Value}");

        }
        catch (InvalidPasswordException ex)
        {
            return BadRequest(ex.Message);
        }
        
        catch (Exception)
        {
            return Problem();
        }
    }

    [AllowAnonymous]
    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmUser(UserConfirmModel request)
    {
        try
        {
            await _userService.ConfirmUserAsync(request);

            return Ok();
        }
        catch (Exception)
        {
            return Problem();
        }
        
    }
    
}