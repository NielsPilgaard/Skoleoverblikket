using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/rooms")]
[Authorize]
public sealed class RoomsController(RoomService rooms) : ControllerBase
{
	public record RoomDto(Guid Id, string Name, int? Capacity, string? Description);
	public record UpsertRoomRequest(
		[Required][StringLength(100, MinimumLength = 1)] string Name,
		int? Capacity,
		string? Description);

	[HttpGet]
	public async Task<ActionResult<List<RoomDto>>> GetAll(CancellationToken cancellationToken) =>
		Ok(await rooms.GetAllAsync(cancellationToken));

	[HttpGet("{id:guid}")]
	public async Task<ActionResult<RoomDto>> GetById(Guid id, CancellationToken cancellationToken) =>
		await rooms.GetByIdAsync(id, cancellationToken) is { } room ? Ok(room) : NotFound();

	[HttpPost]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<RoomDto>> Create([FromBody] UpsertRoomRequest req, CancellationToken cancellationToken)
	{
		var room = await rooms.CreateRoomAsync(req, cancellationToken);
		return CreatedAtAction(nameof(GetById), new { id = room.Id }, room);
	}

	[HttpPut("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<RoomDto>> Update(Guid id, [FromBody] UpsertRoomRequest req, CancellationToken cancellationToken) =>
		await rooms.UpdateRoomAsync(id, req, cancellationToken) is { } room ? Ok(room) : NotFound();

	[HttpDelete("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
		await rooms.DeleteRoomAsync(id, cancellationToken) ? NoContent() : NotFound();
}
