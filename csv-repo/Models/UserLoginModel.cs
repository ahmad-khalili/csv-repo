using System.ComponentModel.DataAnnotations;

namespace csv_repo.Models;

public class UserLoginModel
{
    [Required]
    public string Username { get; set; }

    [Required]
    public string Password { get; set; }
}