﻿using BotDeScans.App.Services.ExternalClients;
using BotDeScans.App.Services.Publish;
using FluentResults;
using Google.Apis.Blogger.v3;
using Google.Apis.Blogger.v3.Data;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
namespace BotDeScans.App.Services;

public partial class GoogleBloggerService(
    ImageService imageService,
    BloggerClient bloggerClient,
    IConfiguration configuration)
{
    private const string BLOGGER_RELEASE_TEMPLATE_FILE_NAME = "blogger-template.html";

    private readonly BloggerService bloggerService = bloggerClient.Client;
    private readonly string? bloggerUrl = configuration.GetValue<string>("Blogger:Url");
    private readonly string? bloggerId = configuration.GetValue<string>("Blogger:Id");

    public async Task<Result<Post>> PostAsync(
        string title,
        string htmlContent,
        string label,
        string chapterNumber)
    {
        if (string.IsNullOrWhiteSpace(bloggerUrl))
            return Result.Fail("Blogger url is undefined.");

        if (string.IsNullOrWhiteSpace(bloggerId))
            return Result.Fail("Blogger id is undefined.");

        if (Uri.TryCreate(bloggerUrl, UriKind.Absolute, out var uri) is false)
            return Result.Fail("Unable to identify Blogger url as valid link.");

        var insertRequest = bloggerService.Posts.Insert(new Post()
        {
            Content = htmlContent,
            Title = title,
            Labels = new List<string> { label },
            Url = uri.Host +
                UrlPattern().Replace(title.ToLower().Replace(" ", "-"), "") + "-" +
                UrlPattern().Replace(chapterNumber.ToLower().Replace(" ", "-"), "")
        }, bloggerId);

        var post = await insertRequest.ExecuteAsync();
        return Result.Ok(post);
    }

    public async Task<Result<string>> GenerateHtmlAsync(PublishState publishState, CancellationToken cancellationToken)
    {
        var templateResult = await GetBloggerTemplateAsync();
        if (templateResult.IsFailed)
            return templateResult;

        var cover = await imageService.CreateBase64File(publishState.InternalData.CoverFilePath, 200, 300, cancellationToken);
        return ReplaceTemplateKeys(templateResult.Value, publishState, cover);
    }

    private static async Task<Result<string>> GetBloggerTemplateAsync()
    {
        try
        {
            using var streamReader = new StreamReader(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "config",
                BLOGGER_RELEASE_TEMPLATE_FILE_NAME));

            return Result.Ok(await streamReader.ReadToEndAsync());
        }
        catch
        {
            return Result.Fail(
                $"Unable to read blogger release template file: "
                + BLOGGER_RELEASE_TEMPLATE_FILE_NAME);
        }
    }

    private static string ReplaceTemplateKeys(string template, PublishState publishState, string cover)
    {
        var keyMaps = CreateReplacingTemplateKeyMaps(cover);
        foreach (var keyMap in keyMaps)
            template = template.Replace(keyMap.Key, keyMap.Value.Invoke(publishState));

        return template;
    }

    private static IDictionary<string, Func<PublishState, string>> CreateReplacingTemplateKeyMaps(string cover)
    {
        var mainKeyMaps = new Dictionary<string, Func<PublishState, string>>
        {
            { "##RELEASE_TITLE##",         state => state.Info.DisplayTitle },
            { "##CHAPTER_TITLE##",         state => state.Info.ChapterName ?? $"Capítulo {state.Info.ChapterNumber}" },
            { "##CHAPTER_NUMBER##",        state => state.Info.ChapterNumber },
            { "##VOLUME_NUMBER##",         state => state.Info.ChapterVolume ?? "?"},
            { "##MESSAGE##",               state => state.Info.Message?.Replace("\n", "<br>") ?? "" }, // todo: precisamos rever isso. O ideal é que seja uma lista de chave/valor reutilizável em todas mensagens da app

            { "##MEGA_ZIP_LINK##",         state => state.Links.MegaZip?? $"#" },
            { "##MEGA_PDF_LINK##",         state => state.Links.MegaPdf ?? $"#" },
            { "##BOX_ZIP_LINK##",          state => state.Links.BoxZip ?? $"#" },
            { "##BOX_PDF_LINK##",          state => state.Links.BoxPdf ?? $"#" },
            { "##GOOGLE_DRIVE_ZIP_LINK##", state => state.Links.DriveZip ?? $"#" },
            { "##GOOGLE_DRIVE_PDF_LINK##", state => state.Links.DrivePdf ?? $"#" },
            { "##MANGADEX_LINK##",         state => state.Links.MangaDexLink ?? $"#" },

            { "##BOX_PDF_READER##",        state => state.Links.BoxPdfReader ?? "" },

            { "##COVER_IMAGE##",           state => $"data:image/png;base64,{cover}" }
        };

        var allKeyMaps = new Dictionary<string, Func<PublishState, string>>();
        foreach (var keyMap in mainKeyMaps)
        {
            string existsKey() => $"##EXISTS_{keyMap.Key.TrimStart('#')}";
            string existsFunc(PublishState state) =>
                string.IsNullOrWhiteSpace(keyMap.Value.Invoke(state)) || keyMap.Value.Invoke(state) == "#"
                ? @"style=""display: none !important;"""
                : string.Empty;

            allKeyMaps.Add(keyMap.Key, keyMap.Value);
            allKeyMaps.Add(existsKey(), existsFunc);
        }

        return allKeyMaps;
    }

    [GeneratedRegex("[^0-9a-zA-Z-]+")]
    private static partial Regex UrlPattern();
}
