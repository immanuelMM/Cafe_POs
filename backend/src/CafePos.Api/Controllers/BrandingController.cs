using CafePos.Api.Http;
using CafePos.Api.Infrastructure;
using CafePos.Application.Features.Branding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Controllers;

[ApiController]
[Route("api/v1/branding")]
[Authorize]
public class BrandingController(BrandingService branding) : ControllerBase
{
    // Headroom above the file limit so an oversize file is answered with a 400, not a transport-level 413.
    private const long TarpaulinRequestLimit = 20L * 1024 * 1024;
    private const long LogoRequestLimit = 3L * 1024 * 1024;

    /// <summary>Brand settings without the image bytes. Anonymous: the login screen shows it before anyone signs in.</summary>
    [HttpGet, AllowAnonymous]
    public async Task<ActionResult<BrandingDto>> Get(CancellationToken ct) =>
        (await branding.GetAsync(ct)).ToActionResult();

    /// <summary>Update name, tagline and colors. Business-wide (every location), so Owner or Admin.</summary>
    [HttpPut, Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<BrandingDto>> Update(UpdateBrandingRequest req, CancellationToken ct) =>
        (await branding.UpdateAsync(req, ct)).ToActionResult();

    // ---- design studio documents ----

    /// <summary>The saved editable design for "logo" or "banner". 404 when none has been saved yet.</summary>
    [HttpGet("designs/{kind}"), Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<BrandDesignDto>> GetDesign(string kind, CancellationToken ct) =>
        (await branding.GetDesignAsync(kind, ct)).ToActionResult();

    /// <summary>Create or replace the editable design. The document must be a JSON object (max 8 MB, embedded images included).</summary>
    [HttpPut("designs/{kind}"), Authorize(Policy = Policies.AdminUp)]
    [RequestSizeLimit(BrandingService.DesignMaxChars + 1024 * 1024)]
    public async Task<ActionResult<BrandDesignDto>> SaveDesign(string kind, SaveBrandDesignRequest req, CancellationToken ct) =>
        (await branding.SaveDesignAsync(kind, req, ct)).ToActionResult();

    [HttpGet("logo"), AllowAnonymous]
    public async Task<IActionResult> GetLogo(CancellationToken ct)
    {
        var image = await branding.GetLogoAsync(ct);
        return Uploads.Serve(this, image?.Data, image?.ContentType);
    }

    [HttpPut("logo"), Authorize(Policy = Policies.AdminUp)]
    [RequestSizeLimit(LogoRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = LogoRequestLimit)]
    public async Task<ActionResult<BrandingDto>> PutLogo([FromForm] UploadFileRequest req, CancellationToken ct) =>
        (await branding.PutLogoAsync(Uploads.ToUploadedFile(req.File), ct)).ToActionResult();

    [HttpDelete("logo"), Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<BrandingDto>> DeleteLogo(CancellationToken ct) =>
        (await branding.DeleteLogoAsync(ct)).ToActionResult();

    [HttpGet("tarpaulin"), AllowAnonymous]
    public async Task<IActionResult> GetTarpaulin(CancellationToken ct)
    {
        var image = await branding.GetTarpaulinAsync(ct);
        return Uploads.Serve(this, image?.Data, image?.ContentType);
    }

    [HttpPut("tarpaulin"), Authorize(Policy = Policies.AdminUp)]
    [RequestSizeLimit(TarpaulinRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = TarpaulinRequestLimit)]
    public async Task<ActionResult<BrandingDto>> PutTarpaulin([FromForm] UploadFileRequest req, CancellationToken ct) =>
        (await branding.PutTarpaulinAsync(Uploads.ToUploadedFile(req.File), ct)).ToActionResult();

    [HttpDelete("tarpaulin"), Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<BrandingDto>> DeleteTarpaulin(CancellationToken ct) =>
        (await branding.DeleteTarpaulinAsync(ct)).ToActionResult();
}
