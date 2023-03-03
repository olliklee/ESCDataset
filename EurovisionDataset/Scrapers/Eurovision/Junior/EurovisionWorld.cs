﻿using EurovisionDataset.Data.Junior;
using Microsoft.Playwright;
using Lyrics = EurovisionDataset.Data.Lyrics;

namespace EurovisionDataset.Scrapers.Eurovision.Junior;

public class EurovisionWorld : EurovisionWorld2<Contest, Contestant>
{
    private const string CONTEST_ARENA_KEY = "location";
    private const string CONTEST_CITY_AND_COUNTRY_KEY = "city";
    private const string CONTEST_LOGO_KEY = "logo";

    private const string ROUND_DATE_KEY = "date";

    private const string LYRICS_LANGUAGES_KEY = "language";

    #region Contest

    protected override string GetContestPageUrl(int year)
    {
        return $"/junior-eurovision/{year}";
    }

    protected override async Task GetContestDataAsync(IPage page, Dictionary<string, string> data)
    {
        IReadOnlyList<IElementHandle> rows = await page.QuerySelectorAllAsync(".national_data tr");

        foreach (IElementHandle row in rows)
        {
            IReadOnlyList<IElementHandle> columns = await row.QuerySelectorAllAsync("td");
            string key = await columns[0].InnerTextAsync();
            string value = (await columns[1].InnerTextAsync()).Replace("\n", DATA_SEPARATOR);

            AddData(data, key, value);
        }

        IElementHandle logoElement = await page.QuerySelectorAsync("figure img");
        if (logoElement != null) AddData(data, "logo", await logoElement.GetAttributeAsync("src"));
    }

    protected override void SetContestData(Contest contest, Dictionary<string, string> data)
    {
        base.SetContestData(contest, data);

        if (data.TryGetValue(CONTEST_ARENA_KEY, out string arena))
            contest.Arena = arena;

        if (data.TryGetValue(CONTEST_CITY_AND_COUNTRY_KEY, out string cityAndCountry))
        {
            string[] cityAndCountryAux = cityAndCountry.Split(", ");
            contest.City = cityAndCountryAux[0];
            if (cityAndCountryAux.Length > 1)
                contest.Country = Utils.GetCountryCode(cityAndCountryAux[1]);
        }

        if (data.TryGetValue(CONTEST_LOGO_KEY, out string logo))
            contest.LogoUrl = logo;
    }

    #endregion

    #region Contestant

    protected override async Task<IElementHandle> GetContestantsTableAsync(IPage page)
    {
        return await page.QuerySelectorAsync(".national_table");
    }

    protected override async Task GetContestantDataAsync(IElementHandle row, IPage page, Dictionary<string, string> data)
    {
        await GetContestantData(page, data);

        if (data.TryGetValue(CONTESTANT_COUNTRY_KEY, out string country))
            AddData(data, CONTESTANT_COUNTRY_KEY, Utils.GetCountryCode(country));
    }

    private async Task GetContestantData(IPage page, Dictionary<string, string> data)
    {
        IReadOnlyList<IElementHandle> rows = await page.QuerySelectorAllAsync(".national_data tr");

        foreach (IElementHandle row in rows)
        {
            IReadOnlyList<IElementHandle> columns = await row.QuerySelectorAllAsync("td");
            string key = (await columns[0].InnerTextAsync()).Split('/').First();
            string value = await columns[1].InnerTextAsync();

            AddData(data, key, value);
        }
    }

    protected override async Task<IList<Lyrics>> GetLyricsAsync(IPage page, Dictionary<string, string> data)
    {
        IList<Lyrics> result = await base.GetLyricsAsync(page, data);

        if (result.Count == 0 && data.TryGetValue(LYRICS_LANGUAGES_KEY, out string language))
        {
            result.Add(new Lyrics()
            {
                Languages = language.Split(DATA_SEPARATOR)
            });
        }

        return result;
    }

    #endregion

    #region Round

    protected override async Task<IReadOnlyList<Data.Round>> GetRoundsAsync(PlaywrightScraper playwright, int year, Dictionary<string, string> contestData, IReadOnlyList<Contestant> contestants)
    {
        return new[] {
            new Round()
            {
                Name = "final",
                Date = contestData.GetValueOrDefault(ROUND_DATE_KEY),
                Performances = await GetPerformancesAsync(playwright.Page)
            }
        };
    }

    private async Task<IReadOnlyList<Performance>> GetPerformancesAsync(IPage page)
    {
        IElementHandle table = await GetContestantsTableAsync(page);
        IReadOnlyList<IElementHandle> headerColumns = await table.QuerySelectorAllAsync("thead tr:last-child th");
        string[] headers = await Task.WhenAll(headerColumns.Select(e =>
            e.InnerTextAsync().ContinueWithResult(s => s.ToLower())));

        IReadOnlyList<IElementHandle> rows = await table.QuerySelectorAllAsync("tbody tr");
        List<Performance> result = new List<Performance>(rows.Count);

        for (int i = 0; i < rows.Count; i++)
        {
            IElementHandle row = rows[i];
            IReadOnlyList<IElementHandle> columns = await row.QuerySelectorAllAsync("td");
            Performance performance = await GetPerformanceAsync(headers, columns);

            if (performance != null)
            {
                performance.ContestantId = i;
                result.Add(performance);
            }
        }

        return result;
    }

    private async Task<Performance> GetPerformanceAsync(string[] headers, IReadOnlyList<IElementHandle> columns)
    {
        Performance result = null;

        try
        {
            int place = int.Parse(await columns[0].InnerTextAsync());
            int running = int.Parse(await columns[columns.Count - 2].InnerTextAsync());

            result = new Performance()
            {
                Running = running,
                Place = place,
                Scores = await GetScoresAsync(headers, columns)
            };
        }
        catch(Exception e) 
        { 
        }
        
        return result;
    }

    private async Task<IReadOnlyList<Score>> GetScoresAsync(string[] headers, IReadOnlyList<IElementHandle> columns)
    {
        List<Score> result = new List<Score>();

        for (int i = 3; i < columns.Count - 2; i++)
        {
            string name = headers[i];
            if (name == "points") name = "total";
            int points = int.Parse(await columns[i].InnerTextAsync());

            result.Add(new Score() { Name = name, Points = points });
        }

        return result;
    }

    #endregion
}
