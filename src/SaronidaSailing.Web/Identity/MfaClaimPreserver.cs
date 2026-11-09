using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace SaronidaSailing.Web.Identity;

public static class MfaClaimPreserver
{
    public static Task PreserveAsync(SecurityStampRefreshingPrincipalContext context)
    {
        var amr = context.CurrentPrincipal?.FindFirst(AdminPolicy.MfaClaimType);
        var identity = context.NewPrincipal?.Identities.FirstOrDefault();
        if (amr is not null && identity is not null && !identity.HasClaim(amr.Type, amr.Value))
        {
            identity.AddClaim(new Claim(amr.Type, amr.Value));
        }

        return Task.CompletedTask;
    }
}
