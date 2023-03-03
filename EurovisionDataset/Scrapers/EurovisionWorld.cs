﻿using System.Text;
using System.Text.RegularExpressions;
using EurovisionDataset.Data;
using Microsoft.Playwright;

namespace EurovisionDataset.Scrapers;

public abstract class EurovisionWorld
{
    protected const string ARTIST_KEY = "artist";
    protected const string SONG_KEY = "title";
    private const string URL = "https://eurovisionworld.com";
    private const int DELAY_REQUEST = 600; //ms
    private const int TOO_MANY_REQUESTS_DELAY = 4000; //ms

    public static async Task RemovePopUpAsync()
    {
        using PlaywrightScraper playwright = new PlaywrightScraper();
        await playwright.LoadPageAsync(URL, WaitUntilState.NetworkIdle);

        string selector = "popup_follow_close";
        IElementHandle popUpElement = await playwright.Page.QuerySelectorAsync(selector);

        if (popUpElement != null) await popUpElement.ClickAsync();
    }

    #region Contest

    public virtual async Task<Contest> GetContestAsync(int year)
    {
        Contest result = null;
        using PlaywrightScraper playwright = new PlaywrightScraper();
        string url = GetContestPageUrl(year);

        if (await LoadPageAsync(playwright, url))
        {
            result = await GetContestAsync(playwright, year);
        }

        return result;
    }

    protected abstract string GetContestPageUrl(int year);

    protected abstract Task<Contest> GetContestAsync(PlaywrightScraper playwright, int year);

    #endregion

    #region Contestant

    protected async Task GetContestantAsync(IElementHandle songLink, Contestant contestant)
    {
        using PlaywrightScraper playwright = new PlaywrightScraper();
        string tagName = (await songLink.GetPropertyAsync("tagName")).ToString().ToLower();
        if (tagName != "a") songLink = await songLink.QuerySelectorAsync("a");
        string url = await songLink.GetAttributeAsync("href");

        await LoadPageAsync(playwright, url);
        await GetContestantInfoAsync(playwright.Page, contestant);
    }

    protected async Task GetContestantInfoAsync(IPage page, Contestant contestant)
    {
        Dictionary<string, string> data = await GetNationalDataAsync(page); // Junior and Nationals
        if (data.Count == 0) data = await GetContestantDataAsync(page); // Senior
        if (!(data.ContainsKey(ARTIST_KEY) && data.ContainsKey(SONG_KEY))) 
            await GetArtistAndSongAsync(page, data);

        contestant.VideoUrls = await GetVideoUrlsAsync(page);
        contestant.Lyrics = await GetLyricsAsync(page, data);

        SetContestantData(data, contestant);
    }

    private async Task GetArtistAndSongAsync(IPage page, Dictionary<string, string> data)
    {
        IElementHandle artistAndSongElement = await page.QuerySelectorAsync(".mm h1") // Junior and Nationals
            ?? await page.QuerySelectorAsync("h1.mm"); // Senior

        string[] artistAndSong = (await artistAndSongElement.InnerTextAsync()).Split("\n")
            .Last().Split('-').Select(s => s.Trim()).ToArray();

        if (!data.ContainsKey(ARTIST_KEY)) data.Add(ARTIST_KEY, artistAndSong[0]);
        if (!data.ContainsKey(SONG_KEY)) data.Add(SONG_KEY, artistAndSong[1].Trim('\"'));
    }

    private async Task<IList<Lyrics>> GetLyricsAsync(IPage page, Dictionary<string, string> data)
    {
        IList<Lyrics> result = new List<Lyrics>();
        IReadOnlyList<IElementHandle> lyrics = await page.QuerySelectorAllAsync(".lyrics_div:not(.sticky)");

        if (!lyrics.IsNullOrEmpty())
        {
            foreach (IElementHandle lyric in lyrics)
            {
                IElementHandle title = await lyric.QuerySelectorAsync("h3");
                IReadOnlyList<IElementHandle> paragraphs = await lyric.QuerySelectorAllAsync("p");
                StringBuilder stringBuilder = new StringBuilder();

                for (int i = 0; i < paragraphs.Count; i++)
                {
                    IElementHandle paragraph = paragraphs[i];
                    string text = await paragraph.InnerTextFromHTMLAsync();
                    
                    stringBuilder.Append(text);
                    if (i < paragraphs.Count - 1) stringBuilder.Append("\r");
                }

                result.Add(new Lyrics()
                {
                    Languages = (await lyric.GetAttributeAsync("data-lyrics-version")).Split(", "),
                    Title = await (title?.InnerTextAsync()).ForAwait(),
                    Content = stringBuilder.ToString()
                });
            }
        }
        else if (data.TryGetValue("language", out string language))
        {
            result.Add(new Lyrics()
            {
                Languages = language.Split(", ")
            });
        }

        return result;
    }

    private async Task<IList<string>> GetVideoUrlsAsync(IPage page)
    {
        IList<string> result = new List<string>();
        IElementHandle moreVideosButton = await page.QuerySelectorAsync(".lyrics_more_videos_div");
        if (moreVideosButton != null) await moreVideosButton.ClickAsync();
        IReadOnlyList<IElementHandle> videoElements = await page.QuerySelectorAllAsync(".vid_ratio iframe");

        if (!videoElements.IsNullOrEmpty())
        {
            foreach (IElementHandle element in videoElements)
            {
                string videoUrl = await element.GetAttributeAsync("src");
                Regex regex = new Regex(@"\?");
                Match match = regex.Match(videoUrl);
                if (match.Success) videoUrl = videoUrl.Substring(0, match.Index);
                result.Add(videoUrl);
            }
        }

        return result;
    }

    protected virtual void SetContestantData(Dictionary<string, string> data, Contestant contestant)
    {
        if (data.TryGetValue(ARTIST_KEY, out string artist))
            contestant.Artist = artist;

        if (data.TryGetValue(SONG_KEY, out string song))
            contestant.Song = song;

        if (data.TryGetValue("backing", out string backings))
            contestant.Backings = backings.Split(", ");

        if (data.TryGetValue("dancer", out string dancers))
            contestant.Dancers = dancers.Split(", ");

        if (data.TryGetValue("lyricist", out string lyricists))
            contestant.Lyricists = lyricists.Split(", ");

        if (data.TryGetValue("songwriter", out string writers))
            contestant.Writers = writers.Split(", ");

        if (data.TryGetValue("composer", out string composers))
            contestant.Composers = composers.Split(", ");
        /*
        if (data.TryGetValue("conductor", out string conductor))
            contestant.Conductor = conductor;

        if (data.TryGetValue("stage director", out string stageDirector))
            contestant.StageDirector = stageDirector;*/
    }

    protected virtual async Task<Dictionary<string, string>> GetContestantDataAsync(IPage page)
    {
        Dictionary<string, string> result = new Dictionary<string, string>();
        IReadOnlyList<IElementHandle> elements = await page.QuerySelectorAllAsync("div.lyr_inf div div div");

        for (int i = 0; i < elements.Count; i += 2)
        {
            IElementHandle key = elements[i];
            IElementHandle value = elements[i + 1];

            await AddDataAsync(result, key, value);
        }

        string selector = ".people_wrap.mm .people_category";
        IReadOnlyList<IElementHandle> peopleTable = await page.QuerySelectorAllAsync(selector);

        foreach (IElementHandle element in peopleTable)
        {
            string key = await element.QuerySelectorAsync("h4.label")
                .ContinueWithResult(e => e.InnerTextAsync());

            string value = await element.QuerySelectorAllAsync("li b")
                .ContinueWithResult(a => a.Select(e => e.InnerTextAsync()))
                .ContinueWithResult(a => string.Join(", ", a));

            AddData(result, key, value);
        }

        return result;
    }

    #endregion

    protected async Task<bool> LoadPageAsync(PlaywrightScraper playwright, string url)
    {
        string absoluteUrl = URL + url;
        bool retry;
        IResponse response;

        do
        {
            retry = false;
            response = await playwright.LoadPageAsync(absoluteUrl, WaitUntilState.DOMContentLoaded);
            await Task.Delay(DELAY_REQUEST);

            if (response.Status == 429) // Too Many Requests
            {
                retry = true;
                await Task.Delay(TOO_MANY_REQUESTS_DELAY);
            }
        } 
        while (retry);

        return playwright.Page.Url.Equals(absoluteUrl, StringComparison.OrdinalIgnoreCase);
    }

    protected async Task<Dictionary<string, string>> GetNationalDataAsync(IPage page)
    {
        IReadOnlyList<IElementHandle> rows = await page.QuerySelectorAllAsync(".national_data tr");
        Dictionary<string, string> result = new Dictionary<string, string>();

        foreach (IElementHandle row in rows)
        {
            IReadOnlyList<IElementHandle> columns = await row.QuerySelectorAllAsync("td");
            IElementHandle key = columns[0];
            IElementHandle value = columns[1];

            await AddDataAsync(result, key, value);
        }

        return result;
    }

    protected async Task AddDataAsync(Dictionary<string, string> data, IElementHandle keyElement, IElementHandle valueElement)
    {
        string key = await keyElement.InnerTextAsync();
        string value = await valueElement.InnerTextAsync();

        AddData(data, key, value);
    }

    protected void AddData(Dictionary<string, string> data, string key, string value)
    {
        key = key.ToLower();

        if (key.EndsWith('s')) key = key.Substring(0, key.Length - 1);

        data.TryAdd(key, value);
    }
}