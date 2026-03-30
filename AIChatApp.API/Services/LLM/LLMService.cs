using AIChatApp.API.Model;
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

        public LLMService(HttpClient httpClient, ILogger<LLMService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> GetLLMResponse(string prompt)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                // Example JSON for llama.cpp or ctransformers HTTP server
                var requestBody = new
                {
                    model = "meta-llama-3.1-8b-instruct-q4_k_m.gguf",
                    prompt = prompt,
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

                // ✅ Log latency
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
