using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CafePos.Application.Common;
using CafePos.Infrastructure.Persistence;

namespace CafePos.Api.Tests;

[Collection("api")]
public class ItemPhotoTests
{
    private readonly ApiFactory _f;
    public ItemPhotoTests(ApiFactory f) => _f = f;

    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1 };

    private static MultipartFormDataContent Form(byte[] bytes, string contentType)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { part, "file", "photo.jpg" } };
    }

    private static async Task<ItemDto> FirstItemAsync(HttpClient c) =>
        (await c.GetFromJsonAsync<PagedResult<ItemDto>>("/api/v1/items?pageSize=1", ApiFactory.Json))!.Data[0];

    [Fact]
    public async Task Manager_uploads_photo_it_is_served_anonymously_and_can_be_removed()
    {
        var manager = await _f.ManagerAsync(DevDataSeeder.MainLocationId);
        var item = await FirstItemAsync(manager);

        var res = await manager.PutAsync($"/api/v1/items/{item.Id}/image", Form(Jpeg, "image/jpeg"));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = (await res.Content.ReadFromJsonAsync<ItemDto>(ApiFactory.Json))!;
        Assert.NotNull(updated.ImageUrl);
        Assert.StartsWith($"/api/v1/items/{item.Id}/image?v=", updated.ImageUrl);

        // The list carries the URL too, and the bytes are public with immutable caching.
        var listed = (await manager.GetFromJsonAsync<PagedResult<ItemDto>>("/api/v1/items?pageSize=100", ApiFactory.Json))!.Data.Single(i => i.Id == item.Id);
        Assert.Equal(updated.ImageUrl, listed.ImageUrl);
        var img = await _f.CreateClient().GetAsync(updated.ImageUrl);
        img.StatusCode.ShouldBe(HttpStatusCode.OK);
        Assert.Equal("image/jpeg", img.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Jpeg, await img.Content.ReadAsByteArrayAsync());

        var del = await manager.DeleteAsync($"/api/v1/items/{item.Id}/image");
        del.StatusCode.ShouldBe(HttpStatusCode.OK);
        Assert.Null((await del.Content.ReadFromJsonAsync<ItemDto>(ApiFactory.Json))!.ImageUrl);
        (await _f.CreateClient().GetAsync($"/api/v1/items/{item.Id}/image")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Photo_upload_validates_role_type_size_and_item()
    {
        var manager = await _f.ManagerAsync(DevDataSeeder.MainLocationId);
        var item = await FirstItemAsync(manager);
        var url = $"/api/v1/items/{item.Id}/image";

        var cashier = await _f.CashierAsync();
        (await cashier.PutAsync(url, Form(Jpeg, "image/jpeg"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.PutAsync(url, Form(Jpeg, "image/svg+xml"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await manager.PutAsync(url, Form(Jpeg, "image/png"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // signature mismatch
        var big = new byte[2 * 1024 * 1024 + 1];
        Jpeg.CopyTo(big, 0);
        (await manager.PutAsync(url, Form(big, "image/jpeg"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await manager.PutAsync($"/api/v1/items/{Guid.NewGuid()}/image", Form(Jpeg, "image/jpeg"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
