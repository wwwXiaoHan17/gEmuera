using System.Net;
using System.Text;
using GEmuera.Core.Agent.Llm;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>stub HttpMessageHandler：捕获请求 + 注入响应/异常。</summary>
sealed class StubHttpHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest;
    public string LastBody = "";
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond = _ =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        LastBody = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
        return await Respond(request);
    }
}

public class LlmProviderTests
{
    const string SampleOk = """
        {"choices":[{"message":{"role":"assistant","content":"你好，主人。"}}],
         "usage":{"total_tokens":128}}
        """;

    static OpenAiCompatProvider Make(StubHttpHandler stub)
        => new(new HttpClient(stub), "https://api.example.com/v1/", "test-key", "test-model");

    static LlmRequest SampleRequest() => new(
        new[] { new LlmChatMessage("system", "s"), new LlmChatMessage("user", "u") }, 256);

    [Fact]
    public async Task RequestShape_UsesBaseUrlHeaderAndBody()
    {
        var stub = new StubHttpHandler();
        stub.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(SampleOk, Encoding.UTF8, "application/json") });
        var provider = Make(stub);

        await provider.CompleteAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal("https://api.example.com/v1/chat/completions", stub.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", stub.LastRequest.Headers.GetValues("Authorization").Single());
        Assert.Contains("\"model\":\"test-model\"", stub.LastBody);
        Assert.Contains("\"max_tokens\":256", stub.LastBody);
        Assert.Contains("\"role\":\"system\"", stub.LastBody);
        Assert.Contains("\"role\":\"user\"", stub.LastBody);
    }

    [Fact]
    public async Task Success_ParsesContentAndTokens()
    {
        var stub = new StubHttpHandler();
        stub.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(SampleOk, Encoding.UTF8, "application/json") });

        var result = await Make(stub).CompleteAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("你好，主人。", result.Content);
        Assert.Equal(128, result.TokensUsed);
    }

    [Fact]
    public async Task Http500_ReturnsFailureNotThrow()
    {
        var stub = new StubHttpHandler();
        stub.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        { Content = new StringContent("boom", Encoding.UTF8, "text/plain") });

        var result = await Make(stub).CompleteAsync(SampleRequest(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("500", result.Error);
    }

    [Fact]
    public async Task Timeout_ReturnsFailureNotThrow()
    {
        var stub = new StubHttpHandler();
        stub.Respond = _ => Task.FromException<HttpResponseMessage>(new TaskCanceledException());

        var result = await Make(stub).CompleteAsync(SampleRequest(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("timeout", result.Error);
    }

    [Fact]
    public async Task EmptyRequest_ReturnsFailure()
    {
        var stub = new StubHttpHandler();
        var result = await Make(stub).CompleteAsync(
            new LlmRequest(Array.Empty<LlmChatMessage>(), 10), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("empty request", result.Error);
    }
}
