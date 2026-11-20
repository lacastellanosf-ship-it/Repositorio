namespace Citaspp.Web.Client;

public static class ApiErrorReader
{
    public static async Task<string> ReadAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"No se pudo completar la operación ({(int)response.StatusCode}).";
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.GetString() ?? content;
            }

            if (document.RootElement.TryGetProperty("title", out var title))
            {
                return title.GetString() ?? content;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return content;
        }

        return content;
    }
}
