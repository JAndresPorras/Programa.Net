using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pos.Tests.Infraestructura;

/// <summary>
/// Cliente HTTP que se comporta como el navegador de la cajera: guarda las
/// cookies, lee el token antifalsificacion de cada pagina y lo envia de vuelta.
/// No sigue redirecciones para poder comprobar 302 y 403 explicitamente.
/// </summary>
public sealed partial class PosClient
{
    public HttpClient Http { get; }

    private PosClient(HttpClient http) => Http = http;

    public static PosClient Anonymous(PosAppFactory app) =>
        new(app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        }));

    public static async Task<PosClient> LoginAsync(PosAppFactory app, string user = "admin", string pass = "Admin123*")
    {
        var client = Anonymous(app);
        var response = await client.PostFormAsync("/Cuenta/Ingresar", "/Cuenta/Ingresar", new()
        {
            ["UserName"] = user,
            ["Password"] = pass
        });
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"No se pudo ingresar como {user}: {(int)response.StatusCode}\n{Snippet(body)}");
        }
        return client;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    public async Task<string> TokenFromAsync(string pageUrl)
    {
        var html = await Http.GetStringAsync(pageUrl);
        var match = TokenRegex().Match(html);
        if (!match.Success) throw new InvalidOperationException($"La pagina {pageUrl} no tiene token antifalsificacion");
        return match.Groups[1].Value;
    }

    public async Task<HttpResponseMessage> PostFormAsync(string url, string tokenPage,
        Dictionary<string, string> fields, IEnumerable<KeyValuePair<string, string>>? extra = null)
    {
        var token = await TokenFromAsync(tokenPage);
        var pairs = fields.ToList();
        if (extra is not null) pairs.AddRange(extra);
        pairs.Add(new("__RequestVerificationToken", token));
        return await Http.PostAsync(url, new FormUrlEncodedContent(pairs));
    }

    /// <summary>Llamada JSON del punto de venta, con el token que trae la pantalla del POS.</summary>
    public async Task<JsonElement> PosAsync(string action, object? body = null)
    {
        var token = await TokenFromAsync("/");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Pos/{action}")
        {
            Content = JsonContent.Create(body ?? new { })
        };
        request.Headers.Add("RequestVerificationToken", token);
        var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"/Pos/{action} devolvio {(int)response.StatusCode}: {Snippet(text)}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public async Task<JsonElement> GetJsonAsync(string url)
    {
        var text = await Http.GetStringAsync(url);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public async Task<(HttpStatusCode Status, string Body)> GetAsync(string url)
    {
        var response = await Http.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static string Snippet(string text) => text.Length > 600 ? text[..600] + "..." : text;
}
