﻿using System.Text;
using System.Text.RegularExpressions;
using EurovisionDataset.Data;
using Microsoft.Playwright;

namespace EurovisionDataset.Scrapers;

public abstract class EurovisionWorld
{
    protected const string URL = "https://eurovisionworld.com";

    public static async Task RemovePopUpAsync()
    {
        using PlaywrightScraper playwright = new PlaywrightScraper();
        await playwright.LoadPageAsync(URL, WaitUntilState.NetworkIdle);

        string selector = "popup_follow_close";
        IElementHandle popUpElement = await playwright.Page.QuerySelectorAsync(selector);

        if (popUpElement != null) await popUpElement.ClickAsync();
    }
}

public abstract class EurovisionWorld<TContest, TContestant> : EurovisionWorld
    where TContest : Contest, new()
    where TContestant : Contestant, new()
{
    protected const string CONTEST_PRESENTERS_KEY = "host";
    protected const string CONTEST_SLOGAN_KEY = "slogan";
    protected const string CONTEST_VOTING_KEY = "voting";

    protected const string CONTESTANT_COUNTRY_KEY = "country";
    protected const string CONTESTANT_ARTIST_KEY = "artist";
    protected const string CONTESTANT_SONG_KEY = "title";

    protected const string ROUND_DATETIME_KEY = "date";

    protected const string DATA_SEPARATOR = ", ";
    private const int DELAY_REQUEST = 800; //ms
    private const int TOO_MANY_REQUESTS_DELAY = 10000; //ms

    #region Contest

    public virtual async Task<TContest> GetContestAsync(int year)
    {
        TContest result;
        using PlaywrightScraper playwright = new PlaywrightScraper();
        string url = GetContestPageUrl(year);
        bool retry = false;

        do
        {
            if (retry) await Task.Delay(TOO_MANY_REQUESTS_DELAY);
            await LoadPageAsync(playwright, url);
            IReadOnlyList<IElementHandle> contestantsTableRows = await GetContestantsTableRowsAsync(playwright.Page);
            retry = contestantsTableRows.Count == 0;
        }
        while (retry);

        result = await GetContestAsync(playwright, year);

        return result;
    }

    protected abstract string GetContestPageUrl(int year);

    private async Task<TContest> GetContestAsync(PlaywrightScraper playwright, int year)
    {
        TContest result = new TContest() { Year = year };
        Dictionary<string, string> data = new Dictionary<string, string>();

        await GetContestDataAsync(playwright.Page, data);
        SetContestData(result, data);

        IReadOnlyList<TContestant> contestans = await GetContestantsAsync(playwright.Page, year);
        result.Contestants = contestans;
        result.Rounds = await GetRoundsAsync(playwright, year, data, contestans);

        return result;
    }

    protected abstract Task GetContestDataAsync(IPage page, Dictionary<string, string> data);

    protected virtual void SetContestData(TContest contest, Dictionary<string, string> data)
    {
        if (data.TryGetValue(CONTEST_PRESENTERS_KEY, out string presenters))
            contest.Presenters = SplitData(presenters);

        if (data.TryGetValue(CONTEST_SLOGAN_KEY, out string slogan))
            contest.Slogan = slogan;

        if (data.TryGetValue(CONTEST_VOTING_KEY, out string voting))
            contest.Voting = voting;
    }

    #endregion

    #region Contestant

    private async Task<IReadOnlyList<TContestant>> GetContestantsAsync(IPage page, int year)
    {
        List<TContestant> result = new List<TContestant>();
        IReadOnlyList<IElementHandle> rows = await GetContestantsTableRowsAsync(page);
        using PlaywrightScraper playwright = new PlaywrightScraper();

        for (int i = 0; i < rows.Count; i++)
        {
            IElementHandle row = rows[i];
            TContestant contestant = await GetContestantAsync(playwright, row, year);
            contestant.Id = i;

            result.Add(contestant);
        }

        return result;
    }

    protected abstract Task<IElementHandle> GetContestantsTableAsync(IPage page);

    private async Task<IReadOnlyList<IElementHandle>> GetContestantsTableRowsAsync(IPage page)
    {
        IElementHandle table = await GetContestantsTableAsync(page);

        return await table.QuerySelectorAllAsync("tbody tr");
    }

    protected async Task<TContestant> GetContestantAsync(PlaywrightScraper playwright, IElementHandle row, int year)
    {
        TContestant result = new TContestant();
        Dictionary<string, string> data = new Dictionary<string, string>();
        IPage page = await GoToContestantPageAsync(playwright, row);

        await GetContestantDataAsync(row, page, data);
        SetContestantData(result, data);

        result.Lyrics = await GetLyricsAsync(page, data);
        result.VideoUrls = await GetVideoUrlsAsync(page);

        return result;
    }

    private async Task<IPage> GoToContestantPageAsync(PlaywrightScraper playwright, IElementHandle row)
    {
        IReadOnlyList<IElementHandle> links = await row.QuerySelectorAllAsync("a");
        /*string tagName = (await songLink.GetPropertyAsync("tagName")).ToString().ToLower();
        if (tagName != "a") songLink = await songLink.QuerySelectorAsync("a");*/
        string url = await links[1].GetAttributeAsync("href");
        await LoadPageAsync(playwright, url);

        return playwright.Page;
    }

    protected abstract Task GetContestantDataAsync(IElementHandle row, IPage page, Dictionary<string, string> data);

    protected virtual void SetContestantData(TContestant contestant, Dictionary<string, string> data)
    {
        if (data.TryGetValue(CONTESTANT_COUNTRY_KEY, out string country))
            contestant.Country = country;

        if (data.TryGetValue(CONTESTANT_ARTIST_KEY, out string artist))
            contestant.Artist = artist;

        if (data.TryGetValue(CONTESTANT_SONG_KEY, out string song))
            contestant.Song = song;

        if (data.TryGetValue("backing", out string backings))
            contestant.Backings = SplitData(backings);

        if (data.TryGetValue("dancer", out string dancers))
            contestant.Dancers = SplitData(dancers);

        if (data.TryGetValue("lyricist", out string lyricists))
            contestant.Lyricists = SplitData(lyricists);

        if (data.TryGetValue("songwriter", out string writers))
            contestant.Writers = SplitData(writers);

        if (data.TryGetValue("composer", out string composers))
            contestant.Composers = SplitData(composers);
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

    protected virtual async Task<IList<Lyrics>> GetLyricsAsync(IPage page, Dictionary<string, string> data)
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
                    if (i < paragraphs.Count - 1) stringBuilder.Append("\n\n");
                }

                result.Add(new Lyrics()
                {
                    Languages = (await lyric.GetAttributeAsync("data-lyrics-version")).Split(DATA_SEPARATOR),
                    Title = await (title?.InnerTextAsync()).ForAwait(),
                    Content = stringBuilder.ToString()
                });
            }
        }

        return result;
    }

    #endregion

    #region Round

    protected abstract Task<IReadOnlyList<Round>> GetRoundsAsync(PlaywrightScraper playwright, int year, Dictionary<string, string> contestData, IReadOnlyList<TContestant> contestants);

    #endregion

    #region Utils

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

    protected void AddData(Dictionary<string, string> data, string key, string value)
    {
        key = key.ToLower();

        if (key.EndsWith('s')) key = key.Substring(0, key.Length - 1);

        if (!data.TryAdd(key, value)) data[key] = value;
    }

    protected string[] SplitData(string data)
    {
        return data.Replace(" and ", DATA_SEPARATOR).Split(DATA_SEPARATOR);
    }

    protected (DateOnly, TimeOnly?) GetDateAndTime(Dictionary<string, string> contestData)
    {
        string[] data = contestData[ROUND_DATETIME_KEY].Split(DATA_SEPARATOR);
        DateOnly date = DateOnly.Parse(data[0]);
        TimeOnly? time = null;

        if (data.Length == 2)
        {
            string timeData = data[1];

            Regex endTimeRegex = new Regex(@"-.*:[0-9]+ *"); // Para quitar la hora en la que acaba el certamen
            timeData = endTimeRegex.Replace(timeData, "");

            DateTime datetimeData = DateTime.Parse(timeData.Replace("CEST", "+2")
                .Replace("CET", "+1")
                .Replace("UTC", ""));

            time = TimeOnly.FromDateTime(datetimeData.ToUniversalTime());
        }

        return (date, time);

        /*
        Regex regex = new Regex(@"[0-9]*:"); //Para quitar la hora
        Match match = regex.Match(date);
        if (match.Success) date = date.Substring(0, match.Index);
        date = date.Trim();
        if (year >= 1963) date = $"{date} 21:00 CEST";
        */
        //return DateTime.Parse(date);
    }

    #endregion
}
