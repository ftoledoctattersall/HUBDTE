using HUBDTE.Application.DocumentIngestion;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Text.Json;

namespace HUBDTE.Api.Controllers
{
    [ApiController]
    [Route("documents")]
    public class DocumentsController : ControllerBase
    {
        private readonly IDocumentIngestionService _service;

        public DocumentsController(IDocumentIngestionService service) { _service = service; }

        [SwaggerOperation(
            Summary = "Ingresa documento para procesamiento",
            Description = "Recibe JSON dinámico y lo registra en Outbox para generación de TXT y envío a Azurian."
        )]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [HttpPost]
        public async Task<IActionResult> Ingest([FromBody] JsonElement payload, CancellationToken ct)
        {
            Request.Headers.TryGetValue("X-Client-Token", out var provided);

            var result = await _service.IngestAsync(payload, provided.ToString(), ct);

            return StatusCode(result.HttpStatus, result.Body);
        }
    }
}