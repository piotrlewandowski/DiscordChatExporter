﻿using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DiscordChatExporter.Core.Internal;
using DiscordChatExporter.Core.Models;
using Scriban;
using Scriban.Runtime;

namespace DiscordChatExporter.Core.Services
{
    public class ExportService : IExportService
    {
        private readonly ISettingsService _settingsService;

        public ExportService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        private Template GetTemplate(ExportFormat format)
        {
            // Resource root namespace for all templates
            const string resourceRootNamespace = "DiscordChatExporter.Core.Resources.ExportService";

            if (format == ExportFormat.PlainText)
            {
                // Get template code
                var raw = Assembly.GetExecutingAssembly()
                    .GetManifestResourceString($"{resourceRootNamespace}.PlainText.Template.txt");

                // Parse
                return Template.Parse(raw);
            }

            if (format == ExportFormat.HtmlDark)
            {
                // Get css
                var sharedCss = Assembly.GetExecutingAssembly()
                    .GetManifestResourceString($"{resourceRootNamespace}.Html.Shared.css");
                var themeCss = Assembly.GetExecutingAssembly()
                    .GetManifestResourceString($"{resourceRootNamespace}.Html.DarkTheme.css");
                var css = sharedCss + Environment.NewLine + themeCss;

                // Get template code
                var raw = Assembly.GetExecutingAssembly()
                    .GetManifestResourceString($"{resourceRootNamespace}.Html.Template.html");
                
                // Inject css
                raw = raw.Replace("<style></style>", $"<style>{css}</style>");

                // Parse
                return Template.Parse(raw);
            }

            if (format == ExportFormat.HtmlLight)
            {
                // Get css
                var sharedCss = Assembly.GetExecutingAssembly()
                    .GetManifestResourceString($"{resourceRootNamespace}.Html.Shared.css");
                var themeCss = Assembly.GetExecutingAssembly()
                    .GetManifestResourceString($"{resourceRootNamespace}.Html.LightTheme.css");
                var css = sharedCss + Environment.NewLine + themeCss;

                // Get template code
                var raw = Assembly.GetExecutingAssembly()
                    .GetManifestResourceString($"{resourceRootNamespace}.Html.Template.html");

                // Inject css
                raw = raw.Replace("<style></style>", $"<style>{css}</style>");

                // Parse
                return Template.Parse(raw);
            }

            if (format == ExportFormat.Csv)
            {
                var raw = Assembly.GetExecutingAssembly()
                    .GetManifestResourceString($"{resourceRootNamespace}.Csv.Template.csv");
                return Template.Parse(raw);
            }

            throw new ArgumentOutOfRangeException(nameof(format));
        }

        public void Export(ExportFormat format, string filePath, ChannelChatLog log)
        {
            using (var output = File.CreateText(filePath))
            {
                // Get template
                var template = GetTemplate(format);

                // Create template context
                var context = new TemplateContext();
                context.MemberRenamer = m => m.Name;

                // Create script object
                var scriptObject = new ScriptObject();
                context.PushGlobal(scriptObject);

                // Configure output
                context.PushOutput(new TextWriterOutput(output));

                // Import model
                scriptObject.Import(log, context.MemberFilter, context.MemberRenamer);

                // Import functions
                scriptObject.Import(nameof(FormatDateTime), new Func<DateTime, string>(FormatDateTime));
                scriptObject.Import(nameof(FormatFileSize), new Func<long, string>(FormatFileSize));
                scriptObject.Import(nameof(FormatMessageContent),
                    new Func<Message, string>(m => FormatMessageContent(format, m)));

                // Render template
                template.Render(context);
            }
        }

        private string FormatDateTime(DateTime dateTime)
        {
            return dateTime.ToString(_settingsService.DateFormat);
        }

        private string FormatFileSize(long fileSize)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
            double size = fileSize;
            var unit = 0;

            while (size >= 1024)
            {
                size /= 1024;
                ++unit;
            }

            return $"{size:0.#} {units[unit]}";
        }

        private string FormatMessageContentPlainText(Message message)
        {
            var content = message.Content;

            // New lines
            content = content.Replace("\n", Environment.NewLine);

            // User mentions (<@id> and <@!id>)
            foreach (var mentionedUser in message.Mentions.Users)
                content = Regex.Replace(content, $"<@!?{mentionedUser.Id}>", $"@{mentionedUser}");

            // Channel mentions (<#id>)
            foreach (var mentionedChannel in message.Mentions.Channels)
                content = content.Replace($"<#{mentionedChannel.Id}>", $"#{mentionedChannel.Name}");

            // Role mentions (<@&id>)
            foreach (var mentionedRole in message.Mentions.Roles)
                content = content.Replace($"<@&{mentionedRole.Id}>", $"@{mentionedRole.Name}");

            // Custom emojis (<:name:id>)
            content = Regex.Replace(content, "<(:.*?:)\\d*>", "$1");

            return content;
        }

        private string FormatMessageContentHtml(Message message)
        {
            var content = message.Content;

            // Convert content markdown to HTML
            content = MarkdownProcessor.ToHtml(content);

            // Meta mentions (@everyone)
            content = content.Replace("@everyone", "<span class=\"mention\">@everyone</span>");

            // Meta mentions (@here)
            content = content.Replace("@here", "<span class=\"mention\">@here</span>");

            // User mentions (<@id> and <@!id>)
            foreach (var mentionedUser in message.Mentions.Users)
            {
                content = Regex.Replace(content, $"&lt;@!?{mentionedUser.Id}&gt;",
                    $"<span class=\"mention\" title=\"{mentionedUser.FullName.HtmlEncode()}\">" +
                    $"@{mentionedUser.Name.HtmlEncode()}" +
                    "</span>");
            }

            // Role mentions (<@&id>)
            foreach (var mentionedRole in message.Mentions.Roles)
            {
                content = content.Replace($"&lt;@&amp;{mentionedRole.Id}&gt;",
                    "<span class=\"mention\">" +
                    $"@{mentionedRole.Name.HtmlEncode()}" +
                    "</span>");
            }

            // Channel mentions (<#id>)
            foreach (var mentionedChannel in message.Mentions.Channels)
            {
                content = content.Replace($"&lt;#{mentionedChannel.Id}&gt;",
                    "<span class=\"mention\">" +
                    $"#{mentionedChannel.Name.HtmlEncode()}" +
                    "</span>");
            }

            // Custom emojis (<:name:id>)
            content = Regex.Replace(content, "&lt;(:.*?:)(\\d*)&gt;",
                "<img class=\"emoji\" title=\"$1\" src=\"https://cdn.discordapp.com/emojis/$2.png\" />");

            return content;
        }

        private string FormatMessageContentCsv(Message message)
        {
            var content = message.Content;

            // New lines
            content = content.Replace("\n", ", ");

            // Escape quotes
            content = content.Replace("\"", "\"\"");

            // Escape commas and semicolons
            if (content.Contains(",") || content.Contains(";"))
                content = $"\"{content}\"";

            // User mentions (<@id> and <@!id>)
            foreach (var mentionedUser in message.Mentions.Users)
                content = Regex.Replace(content, $"<@!?{mentionedUser.Id}>", $"@{mentionedUser}");

            // Channel mentions (<#id>)
            foreach (var mentionedChannel in message.Mentions.Channels)
                content = content.Replace($"<#{mentionedChannel.Id}>", $"#{mentionedChannel.Name}");

            // Role mentions (<@&id>)
            foreach (var mentionedRole in message.Mentions.Roles)
                content = content.Replace($"<@&{mentionedRole.Id}>", $"@{mentionedRole.Name}");

            // Custom emojis (<:name:id>)
            content = Regex.Replace(content, "<(:.*?:)\\d*>", "$1");

            return content;
        }

        private string FormatMessageContent(ExportFormat format, Message message)
        {
            if (format == ExportFormat.PlainText)
                return FormatMessageContentPlainText(message);

            if (format == ExportFormat.HtmlDark)
                return FormatMessageContentHtml(message);

            if (format == ExportFormat.HtmlLight)
                return FormatMessageContentHtml(message);

            if (format == ExportFormat.Csv)
                return FormatMessageContentCsv(message);

            throw new ArgumentOutOfRangeException(nameof(format));
        }
    }
}