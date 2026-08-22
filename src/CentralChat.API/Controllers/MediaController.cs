using CentralChat.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CentralChat.API.Controllers;

[ApiController, Route("api/media"), Authorize(Policy = Permissions.MessagesView)]
public sealed class MediaController(IMediaService media, ICurrentUser current) : ControllerBase
{
    /// <summary>
    /// Streams stored media for a message. Authorisation is the conversation's, not the file's, so an
    /// agent can only read attachments on conversations they could already open.
    /// </summary>
    [HttpGet("{messageId:guid}")]
    public async Task<IActionResult> Get(Guid messageId, CancellationToken ct)
    {
        var content = await media.OpenAsync(messageId, current.Id, current.HasPermission(Permissions.TicketsAssign), ct);
        return File(content.Content, content.MimeType, content.FileName);
    }
}
