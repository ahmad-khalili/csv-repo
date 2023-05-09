using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using csv_repo.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using IFormFile = Microsoft.AspNetCore.Http.IFormFile;

namespace csv_repo.Controllers;

[Authorize]
[Controller]
[Route("files")]
public class FilesController : ControllerBase
{
    private readonly HttpClient _httpClient;
    const string GatewayUrl = "https://pcmxlikega.execute-api.us-east-1.amazonaws.com/release/files";
    
    public FilesController()
    {
        _httpClient = new HttpClient();
    }

    [HttpGet]
    public async Task<IActionResult> RetrieveAllFiles()
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
                HttpContext.User.Claims.FirstOrDefault(c => c.Type == "CognitoToken")?.Value);
            
            var response = await _httpClient.GetAsync(GatewayUrl);

            var responseJson = await response.Content.ReadFromJsonAsync<ResponseDto>() ?? new ResponseDto
            {
                StatusCode = (int)HttpStatusCode.InternalServerError,
                Body = string.Empty
            };

            return StatusCode(responseJson.StatusCode, responseJson.Body);
        }
        catch (Exception)
        {
            return Problem();
        }
    }

    [HttpGet("{fileName}")]
    public async Task<IActionResult> RetrieveFile(string fileName)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
                HttpContext.User.Claims.FirstOrDefault(c => c.Type == "CognitoToken")?.Value);

            var response = await _httpClient.GetAsync($"{GatewayUrl}/{fileName}");
            
            var responseJson = await response.Content.ReadFromJsonAsync<FileResponse>();

            return StatusCode((int)response.StatusCode, responseJson);
        }
        catch (Exception)
        {
            return Problem();
        }
    }
    
    [HttpGet("{fileName}/download")]
    public async Task<IActionResult> DownloadFile(string fileName)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
                HttpContext.User.Claims.FirstOrDefault(c => c.Type == "CognitoToken")?.Value);

            var response = await _httpClient.GetAsync($"{GatewayUrl}/{fileName}/download");

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return NotFound();
            }

            var ms = new MemoryStream();

            await response.Content.CopyToAsync(ms);

            return File(ms.ToArray(), "text/csv", fileName);
        }
        catch (Exception)
        {
            return Problem();
        }
    }
    
    [HttpPost("{fileName}/delete")]
    public async Task<IActionResult> DeleteFile(string fileName)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
                HttpContext.User.Claims.FirstOrDefault(c => c.Type == "CognitoToken")?.Value);

            var response =
                await _httpClient.PostAsync($"{GatewayUrl}/{fileName}/delete", new StringContent(string.Empty));

            return StatusCode((int)response.StatusCode);
        }
        catch (Exception)
        {
            return Problem();
        }
    }
    
    [HttpPost]
    public async Task<IActionResult> UploadFiles(IFormFile file)
    {
        using var ms = new MemoryStream();

        await file.CopyToAsync(ms);

        var fileBytes = ms.ToArray();

        var base64 = Convert.ToBase64String(fileBytes);

        var obj = new
        {
            FileName = file.FileName,
            FileContent = base64
        };

        var json = JsonConvert.SerializeObject(obj);

        var stringContent = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);

        var multipartForm = new MultipartFormDataContent();

        multipartForm.Add(new StreamContent(ms), file.Name, file.FileName);

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
                HttpContext.User.Claims.FirstOrDefault(c => c.Type == "CognitoToken")?.Value);

            var response = await _httpClient.PostAsync(GatewayUrl, stringContent);

            return StatusCode((int)response.StatusCode);
        }
        catch (Exception)
        {
            return Problem();
        }
    }
}