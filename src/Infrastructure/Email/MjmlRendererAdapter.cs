using System.Linq;
using Application.Abstractions;
using Microsoft.Extensions.Logging;
using Mjml.Net;

namespace Infrastructure.Email;

internal sealed class MjmlRendererAdapter(ILogger<MjmlRendererAdapter> logger) : Application.Abstractions.IMjmlRenderer
{
    private readonly MjmlRenderer _renderer = new();

    public string Render(string mjml)
    {
        (string html, ValidationErrors errors) = _renderer.Render(mjml, new MjmlOptions
        {
            Beautify = true
        });

        if (errors.Any())
        {
            foreach (object error in errors)
            {
                logger.LogError("MJML Error: {Error}", error.ToString());
            }
        }

        return html;
    }
}
