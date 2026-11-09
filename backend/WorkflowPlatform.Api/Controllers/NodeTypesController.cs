using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkflowPlatform.Api.Engine;

namespace WorkflowPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/node-types")]
public class NodeTypesController : ControllerBase
{
    private readonly NodeRegistry _registry;
    public NodeTypesController(NodeRegistry registry) => _registry = registry;

    /// <summary>Drives the frontend node palette and parameter forms (plan §6.1).</summary>
    [HttpGet]
    public ActionResult<IEnumerable<NodeDefinition>> Get() => Ok(_registry.Definitions);
}
