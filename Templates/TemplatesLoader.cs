using System;
using System.Collections.Generic;
using System.IO;
using Scriban;
using Scriban.Runtime;

namespace TubeRepair_CSharp.Templates
{
    public class TemplatesLoader
    {
        private static readonly TemplatesLoader instance = new();
        public static TemplatesLoader Instance => instance;

        private readonly Dictionary<string, Template> _templateCache = new();

        private TemplatesLoader()
        {
            // Pre-load templates
            LoadTemplate("frontpage_feed.scriban");
            LoadTemplate("classic_featured.scriban");
            LoadTemplate("classic_search.scriban");
            LoadTemplate("search_results.scriban");
            LoadTemplate("comments.scriban");
            LoadTemplate("channel_playlists.scriban");
            LoadTemplate("playlist_videos.scriban");
            LoadTemplate("channel_info.scriban");
            LoadTemplate("search_results_channel.scriban");
            LoadTemplate("uploads.scriban");
        }

        private void LoadTemplate(string templateName)
        {
            try
            {
                string templatePath = Path.Combine("Templates", templateName);
                string templateContent = File.ReadAllText(templatePath);
                Template template = Template.Parse(templateContent);
                _templateCache[templateName] = template;
                Console.WriteLine($"Template loaded: {templateName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading template {templateName}: {ex.Message}");
            }
        }

        public string RenderTemplate(string templateName, object model)
        {
            if (!_templateCache.ContainsKey(templateName))
            {
                LoadTemplate(templateName);
            }

            if (!_templateCache.TryGetValue(templateName, out var template))
            {
                throw new InvalidOperationException($"Template {templateName} not found");
            }

            // Create script object with custom functions
            var scriptObject = new ScriptObject();

            // Add unix function as a custom function
            scriptObject.Import("unix", new Func<long, string>(Helpers.UnixToIso8601));

            // Import model properties into script object
            if (model != null)
            {
                // If model is a dictionary, import directly
                if (model is IDictionary<string, object?> dict)
                {
                    foreach (var kvp in dict)
                    {
                        scriptObject[kvp.Key] = kvp.Value;
                    }
                }
                else
                {
                    // Fallback: use Scriban's built-in import for other types
                    scriptObject.Import(model);
                }
            }

            // Create template context
            var context = new TemplateContext();
            context.PushGlobal(scriptObject);

            return template.Render(context);
        }
    }
}
