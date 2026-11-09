using Microsoft.AspNetCore.Authorization;
using SaronidaSailing.Web.Identity;

namespace SaronidaSailing.Web.Content;

public interface ICmsEditContext
{
    Task<bool> IsEditorAsync();
}

public sealed class CmsEditContext(IHttpContextAccessor httpContextAccessor, IAuthorizationService authorization)
    : ICmsEditContext
{
    private Task<bool>? _isEditor;

    public Task<bool> IsEditorAsync() => _isEditor ??= Evaluate();

    private async Task<bool> Evaluate()
    {
        var http = httpContextAccessor.HttpContext;
        if (http is null)
        {
            return false;
        }

        var result = await authorization.AuthorizeAsync(http.User, AdminPolicy.Name);
        if (!result.Succeeded)
        {
            return false;
        }

        // OnStarting runs after antiforgery's own "no-cache, no-store" header, so this value is the final one.
        http.Response.OnStarting(static state =>
        {
            ((HttpResponse)state).Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        }, http.Response);
        return true;
    }
}
