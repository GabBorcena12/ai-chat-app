using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace AIChatApp.Gateway.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GatewayController : ControllerBase
    {
        private readonly ILogger<GatewayController> _logger;

        public GatewayController(ILogger<GatewayController> logger)
        {
            _logger = logger;
        }

        [HttpGet("Ping")]
        public ActionResult Ping()
        {
            return Ok("Pong");
        }
    }
}
