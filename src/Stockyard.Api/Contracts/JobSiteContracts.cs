using Stockyard.Domain;

namespace Stockyard.Api.Contracts;

public sealed record CreateJobSiteRequest(string? Code, string? Name);

public sealed record JobSiteResponse(Guid Id, string Code, string Name, bool Active)
{
    public static JobSiteResponse From(JobSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        return new(site.Id, site.Code, site.Name, site.Active);
    }
}
