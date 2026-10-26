using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/admin/tenants")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class SuperAdminTenantsController(SuperAdminTenantService tenants) : ControllerBase
{
	/// <param name="includeInternal">Also list internal schools, such as the smoke test school.</param>
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<TenantListItemDto>>> GetTenants(
		[FromQuery] bool includeInternal, CancellationToken cancellationToken) =>
		Ok(await tenants.ListAsync(includeInternal, cancellationToken));

	[HttpGet("{schoolId:guid}")]
	public async Task<ActionResult<TenantDetailDto>> GetTenant(Guid schoolId, CancellationToken cancellationToken) =>
		await tenants.GetAsync(schoolId, cancellationToken) is { } school ? Ok(school) : NotFound();
}
