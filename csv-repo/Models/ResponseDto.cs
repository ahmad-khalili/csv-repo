using System.Net;

namespace csv_repo.Models;

public class ResponseDto
{
    public int StatusCode { get; set; } = (int)HttpStatusCode.InternalServerError;

    public string Body { get; set; } = string.Empty;
}