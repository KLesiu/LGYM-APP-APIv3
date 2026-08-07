using LgymApi.Infrastructure.Services;
using LgymApi.Application.Options;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LgymApi.Api.Configuration;

public static class LocalPhotoDevelopmentEndpoints
{
    public static IEndpointRouteBuilder MapLocalPhotoDevelopmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/dev/photos");
        group.AllowAnonymous();
        group.MapMethods(
            "/upload/{**storageKey}",
            [HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete, HttpMethods.Patch, HttpMethods.Head, HttpMethods.Options],
            UploadAsync);
        group.MapMethods(
            "/read/{**storageKey}",
            [HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete, HttpMethods.Patch, HttpMethods.Head, HttpMethods.Options],
            ReadAsync);
        return endpoints;
    }

    public static async Task<Results<NotFound, BadRequest, NoContent>> UploadAsync(
        string storageKey,
        HttpRequest request,
        LocalPhotoDevelopmentStore store,
        LocalPhotoDevelopmentUrlSigner urlSigner,
        PhotoStorageOptions options,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return TypedResults.NotFound();
        }

        if (!urlSigner.TryValidate(
                request,
                HttpMethods.Put,
                "/dev/photos/upload/",
                out var normalizedStorageKey))
        {
            return TypedResults.NotFound();
        }

        var contentType = request.ContentType?.Split(';', 2)[0].Trim();
        if (string.IsNullOrEmpty(contentType) ||
            !options.AllowedMimeTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest();
        }

        try
        {
            await store.SaveAsync(
                normalizedStorageKey,
                request.Body,
                options.MaxFileSizeBytes,
                cancellationToken);
        }
        catch (InvalidDataException)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.NoContent();
    }

    public static async Task<Results<NotFound, BadRequest, FileContentHttpResult>> ReadAsync(
        string storageKey,
        HttpRequest request,
        LocalPhotoDevelopmentStore store,
        LocalPhotoDevelopmentUrlSigner urlSigner,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return TypedResults.NotFound();
        }

        if (!urlSigner.TryValidate(
                request,
                HttpMethods.Get,
                "/dev/photos/read/",
                out var normalizedStorageKey))
        {
            return TypedResults.NotFound();
        }

        var fileBytes = await store.ReadAsync(normalizedStorageKey, cancellationToken);
        if (fileBytes == null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.File(fileBytes, store.ResolveContentType(normalizedStorageKey));
    }
}
