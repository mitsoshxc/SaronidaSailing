using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Web;

public static class DevelopmentMediaFiles
{
    public static IApplicationBuilder UseDevelopmentMediaFiles(this IApplicationBuilder app, string rootPath)
    {
        var contentTypes = new FileExtensionContentTypeProvider(new Dictionary<string, string> { [".webp"] = "image/webp" });

        return app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(rootPath),
            RequestPath = MediaFileNames.PublicUrlPrefix.TrimEnd('/'),
            ContentTypeProvider = contentTypes,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = context =>
                context.Context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable",
        });
    }
}
