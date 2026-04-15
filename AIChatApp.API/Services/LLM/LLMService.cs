using AIChatApp.API.Model;
using AIChatApp.Core.Config;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using static System.Net.WebRequestMethods;

namespace AIChatApp.API.Services.LLM
{
    public class LLMService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<LLMService> _logger;
        private readonly LocalModelOptions _localModelOptions;

        public LLMService(
            HttpClient httpClient,
            ILogger<LLMService> logger,
            IOptions<LocalModelOptions> localModelOptions)
        {
            _httpClient = httpClient;
            _logger = logger;
            _localModelOptions = localModelOptions.Value;
        }

        public async Task<string> GetLLMResponse(string prompt)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var requestBody = new
                {
                    model = _localModelOptions.FileName,
                    prompt,
                    stream = false
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync("http://localhost:11434/api/generate", content);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadAsStringAsync();

                sw.Stop();

                if (sw.ElapsedMilliseconds > 4000)
                {
                    _logger.LogWarning("Model is slow | {Elapsed} ms", sw.ElapsedMilliseconds);
                }

                _logger.LogInformation(
                    "LLM SUCCESS | Latency: {Elapsed} ms | PromptLength: {Length}",
                    sw.ElapsedMilliseconds,
                    prompt.Length
                );

                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "LLM FAILED | Latency: {Elapsed} ms | PromptLength: {Length}",
                    sw.ElapsedMilliseconds,
                    prompt.Length
                );
                throw;
            }
        }
    }
}
