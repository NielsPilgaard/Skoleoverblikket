using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.Tenancy;

/// <summary>
/// Marks a controller or action as a feature of a paid module. Without the module,
/// <see cref="ModuleAccessFilter"/> rejects its POST/PUT/PATCH requests. GET and DELETE still work,
/// so a school that drops the module can read and clean up its data.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresModuleAttribute(SubscriptionModule module) : Attribute
{
	public SubscriptionModule Module { get; } = module;
}
