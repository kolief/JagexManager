using System.Net;
using System.Text;
using System.Text.Json;

namespace JagexManager.Tests;

internal sealed class Web : HttpMessageHandler
{
    public const string TokenPath = "/oauth2/token";
    public const string SessionPath = "/game-session/v1/sessions";
    public const string AccountsPath = "/game-session/v1/accounts";

    private readonly Dictionary<string, (HttpStatusCode Status, string Body, string Type)> replies = [];

    public List<(HttpRequestMessage Request, string Body)> Sent { get; } = [];

    public TaskCompletionSource? Gate { get; set; }

    public Web Reply(string path, object body, HttpStatusCode status = HttpStatusCode.OK, string type = "application/json")
    {
        replies[path] = (status, body as string ?? JsonSerializer.Serialize(body), type);
        return this;
    }

    public Jagex Jagex() => new(new HttpClient(this));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        Sent.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancel)));

        if (Gate is { } gate)
            await gate.Task.WaitAsync(cancel);

        return replies.TryGetValue(request.RequestUri!.AbsolutePath, out var reply)
            ? new HttpResponseMessage(reply.Status) { Content = new StringContent(reply.Body, Encoding.UTF8, reply.Type) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
