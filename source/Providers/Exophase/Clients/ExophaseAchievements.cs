using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using CommonPlayniteShared.Common;
using CommonPluginsShared;
using CommonPluginsShared.Extensions;
using CommonPluginsShared.Models;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using SuccessStory.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using static CommonPluginsShared.PlayniteTools;
using static SuccessStory.Services.SuccessStoryDatabase;

namespace SuccessStory.Clients
{
    public enum ExophasePlatform
    {
        Google_Play,
        Steam,
        PS3, PS4, PS5, PS_Vita,
        Retro,
        Xbox_One, Xbox_360, Xbox_Series, Windows_8, Windows_10, WP,
        Stadia,
        Origin,
        Blizzard,
        GOG,
        Ubisoft,
    }

    class ExophaseAchievementsFactory : IAchievementFactory
    {
        public void BuildClient(Dictionary<string, GenericAchievements> Providers, Dictionary<string, ISearchableManualAchievements> ManualSearchProviders, Dictionary<string, IMetadataAugmentAchievements> AchievementMetadataAugmenters)
        {
            ExophaseAchievements tmp = new ExophaseAchievements();
            Providers[AchievementSource.Exophase] = tmp;
            ManualSearchProviders[AchievementSource.Exophase] = tmp;
            AchievementMetadataAugmenters[AchievementSource.Exophase] = tmp;
        }
    }
    class ExophaseAchievements : GenericAchievements, ISearchableManualAchievements, IMetadataAugmentAchievements
    {
        internal static new ILogger logger => LogManager.GetLogger();

        private const string UrlExophaseSearch = @"https://api.exophase.com/public/archive/games?q={0}&sort=added";
        private const string UrlExophase = @"https://www.exophase.com";
        private readonly string UrlExophaseLogin = $"{UrlExophase}/login";
        private readonly string UrlExophaseLogout = $"{UrlExophase}/logout";
        private readonly string UrlExophaseAccount = $"{UrlExophase}/account";
        
        // used by new conversion of saved ID to achievement URLs
        private readonly string UrlExophaseAchievements = $"{UrlExophase}/game/{{0}}/achievements/";

        public ExophaseAchievements() : base("Exophase")
        {

        }


        #region Searchable Manual Achievements
        public string Glyph => "\uEA56";
        public List<SearchResult> SearchGame(string Name)
        {
            List<SearchResult> ListSearchGames = new List<SearchResult>();
            try
            {
                string UrlSearch = string.Format(UrlExophaseSearch, WebUtility.UrlEncode(Name));

                string StringJsonResult = Web.DownloadStringData(UrlSearch).GetAwaiter().GetResult();
                if (StringJsonResult == "{\"success\":true,\"games\":false}")
                {
                    logger.Warn($"No Exophase result for {Name}");
                    return ListSearchGames;
                }

                ExophaseSearchResult exophaseScheachResult = Serialization.FromJson<ExophaseSearchResult>(StringJsonResult);

                var ListExophase = exophaseScheachResult?.games?.list;
                if (ListExophase != null)
                {
                    ListSearchGames = ListExophase.Select(x => new SearchResult
                    {
                        Url = x.endpoint_awards,
                        Name = x.title,
                        UrlImage = x.images.o,
                        Platforms = x.platforms.Select(p => p.name).ToList(),
                        AchievementsCount = x.total_awards
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, $"Error on SearchGame({Name})", true, PluginDatabase.PluginName);
            }

            return ListSearchGames;
        }
        public bool CanDoManualAchievements(Game game, GameAchievements gameAchievements)
        {
            return gameAchievements.SourcesLink?.Name.IsEqual("exophase") ?? false;
        }
        public GameAchievements DoManualAchievements(Game game, GameAchievements gameAchievements)
        {
            SearchResult searchResult = new SearchResult
            {
                Url = gameAchievements.SourcesLink?.Url
            };
            return GetManualAchievementsInternal(game, searchResult, false);
        }
        public GameAchievements ApplyAchievementsFromSearchGame(Game game, SearchResult searchResult)
        {
            return GetManualAchievementsInternal(game, searchResult, false);
        }
        #endregion




        protected static IWebView _WebViewOffscreen;
        internal static IWebView WebViewOffscreen
        {
            get
            {
                if (_WebViewOffscreen == null)
                {
                    _WebViewOffscreen = PluginDatabase.PlayniteApi.WebViews.CreateOffscreenView();
                }
                return _WebViewOffscreen;
            }

            set
            {
                _WebViewOffscreen = value;
            }
        }



        // TODO: needed once manuals can refresh
        public override GameAchievements GetAchievements(Game game)
        {
            throw new NotImplementedException();
        }


        private GameAchievements GetAchievementsInternal(Game game, string url)
        {
            return GetManualAchievementsInternal(game, new SearchResult { Name = game.Name, Url = url });
        }
        private GameAchievements GetManualAchievementsInternal(Game game, SearchResult searchResult, bool IsRetry = false)
        {
            GameAchievements gameAchievements = SuccessStory.PluginDatabase.GetDefault(game);
            List<Achievements> AllAchievements = new List<Achievements>();

            try
            {
                string DataExophase = Web.DownloadStringData(searchResult.Url, GetCookies()).GetAwaiter().GetResult();

                HtmlParser parser = new HtmlParser();
                IHtmlDocument htmlDocument = parser.Parse(DataExophase);

                AllAchievements = new List<Achievements>();
                var SectionAchievements = htmlDocument.QuerySelectorAll("ul.achievement, ul.trophy, ul.challenge");

                if (SectionAchievements == null || SectionAchievements.Count() == 0)
                {
                    logger.Warn($"Problem with {searchResult.Url}");
                    if (!IsRetry)
                    {
                        return GetManualAchievementsInternal(game, searchResult, true);
                    }
                }
                else
                {
                    foreach (var Section in SectionAchievements)
                    {
                        foreach (var SearchAchievements in Section.QuerySelectorAll("li"))
                        {
                            try
                            {
                                string sFloat = SearchAchievements.GetAttribute("data-average")
                                    .Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator)
                                    .Replace(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);

                                float.TryParse(sFloat, out float Percent);

                                string UrlUnlocked = SearchAchievements.QuerySelector("img").GetAttribute("src");
                                string Name = WebUtility.HtmlDecode(SearchAchievements.QuerySelector("a").InnerHtml);
                                string Description = WebUtility.HtmlDecode(SearchAchievements.QuerySelector("div.award-description p").InnerHtml);
                                bool IsHidden = SearchAchievements.GetAttribute("class").IndexOf("secret") > -1;

                                AllAchievements.Add(new Achievements
                                {
                                    Name = Name,
                                    UrlUnlocked = UrlUnlocked,
                                    Description = Description,
                                    DateUnlocked = default(DateTime),
                                    Percent = Percent
                                });
                            }
                            catch (Exception ex)
                            {
                                Common.LogError(ex, false, true, PluginDatabase.PluginName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }


            gameAchievements.Items = AllAchievements;


            // Set source link
            if (gameAchievements.HasAchievements)
            {
                gameAchievements.SourcesLink = new SourceLink
                {
                    GameName = searchResult.Name,
                    Name = "Exophase",
                    Url = searchResult.Url
                };
                try
                {
                    string str = searchResult?.Url
                            ?.Replace("https://www.exophase.com/game/", string.Empty)
                            ?.Replace("/achievements/", string.Empty);
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        gameAchievements.Handler = new MainAchievementHandler("Exophase", str);
                    }
                }
                catch { }
            }


            return gameAchievements;
        }


        #region Configuration
        public override bool ValidateConfiguration()
        {
            // The authentification is only for localised achievement
            return true;
        }
        public override bool IsConnected()
        {
            if (CachedIsConnectedResult == null)
            {
                CachedIsConnectedResult = GetIsUserLoggedIn();
            }

            return (bool)CachedIsConnectedResult;
        }
        public override bool EnabledInSettings()
        {
            // No necessary activation
            return true;
        }
        #endregion


        #region Exophase
        public void Login()
        {
            FileSystem.DeleteFile(cookiesPath);
            ResetCachedIsConnectedResult();

            using (var WebView = PluginDatabase.PlayniteApi.WebViews.CreateView(600, 600))
            {
                WebView.LoadingChanged += (s, e) =>
                {
                    string address = WebView.GetCurrentAddress();
                    if (address.Contains(UrlExophaseAccount) && !address.Contains(UrlExophaseLogout))
                    {
                        CachedIsConnectedResult = true;
                        WebView.Close();
                    }
                };

                WebView.DeleteDomainCookies(".exophase.com");
                WebView.Navigate(UrlExophaseLogin);
                WebView.OpenDialog();
            }

            List<HttpCookie> httpCookies = Serialization.GetClone(WebViewOffscreen.GetCookies().Where(x => x.Domain.IsEqual(".exophase.com")).ToList());
            SetCookies(httpCookies);
            WebViewOffscreen.DeleteDomainCookies(".exophase.com");
            WebViewOffscreen.Dispose();
        }

        private bool GetIsUserLoggedIn()
        {
            string DataExophase = Web.DownloadStringData(UrlExophaseAccount, GetCookies()).GetAwaiter().GetResult();
            return DataExophase.Contains("column-username", StringComparison.InvariantCultureIgnoreCase);
        }



        // TODO: use the new sound handler check, or hell get rid of the parm entirely and use the gameAchievements object instead and eliminate the paramater
        private string GetAchievementsPageUrl(GameAchievements gameAchievements, MainAchievementHandler source)
        {
            bool UsedSplit = false;

            if (gameAchievements.Handler.Name == "Exophase" && !string.IsNullOrWhiteSpace(gameAchievements.Handler.Id))
            {
                return string.Format(UrlExophaseAchievements, gameAchievements.Handler.Id);
            }

            string extraId = gameAchievements.GetExtraHandlerId("Exophase");
            if (!string.IsNullOrWhiteSpace(extraId))
            {
                return string.Format(UrlExophaseAchievements, extraId);
            }

            string sourceLinkName = gameAchievements.SourcesLink?.Name;
            if (sourceLinkName == "Exophase")
            {
                return gameAchievements.SourcesLink.Url;
            }

            var searchResults = SearchGame(gameAchievements.Name);
            if (searchResults.Count == 0)
            {
                logger.Warn($"No game found for {gameAchievements.Name} in GetAchievementsPageUrl()");

                searchResults = SearchGame(PlayniteTools.NormalizeGameName(gameAchievements.Name));
                if (searchResults.Count == 0)
                {
                    logger.Warn($"No game found for {PlayniteTools.NormalizeGameName(gameAchievements.Name)} in GetAchievementsPageUrl()");

                    searchResults = SearchGame(Regex.Match(gameAchievements.Name, @"^.*(?=[:-])").Value);
                    UsedSplit = true;
                    if (searchResults.Count == 0)
                    {
                        logger.Warn($"No game found for {Regex.Match(gameAchievements.Name, @"^.*(?=[:-])").Value} in GetAchievementsPageUrl()");
                        return null;
                    }
                }
            }

            string normalizedGameName = UsedSplit ? PlayniteTools.NormalizeGameName(Regex.Match(gameAchievements.Name, @"^.*(?=[:-])").Value) : PlayniteTools.NormalizeGameName(gameAchievements.Name);
            var searchResult = searchResults.Find(x => PlayniteTools.NormalizeGameName(x.Name) == normalizedGameName && PlatformAndProviderMatch(x, gameAchievements, source));

            if (searchResult == null)
            {
                logger.Warn($"No matching game found for {gameAchievements.Name} in GetAchievementsPageUrl()");
            }

            return searchResult?.Url;
        }


        /// <summary>
        /// Set achievement rarity via Exophase web scraping.
        /// </summary>
        /// <param name="gameAchievements"></param>
        /// <param name="source"></param>
        public bool SetRarity(GameAchievements gameAchievements, MainAchievementHandler source)
        {
            DateTime? lastUpdate = gameAchievements.GetExtraHandlerDate("Exophase", "Rarity");

            string achievementsUrl = GetAchievementsPageUrl(gameAchievements, source);
            if (achievementsUrl.IsNullOrEmpty())
            {
                logger.Warn($"No Exophase (rarity) url find for {gameAchievements.Name} - {gameAchievements.Id}");
                return false;
            }

            bool providerMatched = false;

            try
            {
                string str = achievementsUrl
                    ?.Replace("https://www.exophase.com/game/", string.Empty)
                    ?.Replace("/achievements/", string.Empty);
                if (!string.IsNullOrWhiteSpace(str))
                {

                    //DateTime? lastUpdate = gameAchievements.GetExtraHandlerDate("Exophase", str, "Rarity");
                    providerMatched = lastUpdate.HasValue; // if there's a date here, we know we've had this data before and thus we are the winner
                    if ((lastUpdate ?? DateTime.MinValue) < DateTime.UtcNow.AddDays(-1))
                    {

                        GameAchievements exophaseAchievements = GetAchievementsInternal(
                            PluginDatabase.PlayniteApi.Database.Games.Get(gameAchievements.Id),
                            achievementsUrl
                        );

                        exophaseAchievements.Items.ForEach(y =>
                        {
                            var achievement = gameAchievements.Items.Find(x => x.Name.Equals(y.Name, StringComparison.InvariantCultureIgnoreCase));
                            if (achievement == null)
                            {
                                achievement = gameAchievements.Items.Find(x => x.ApiName != null && x.ApiName.Equals(y.Name, StringComparison.InvariantCultureIgnoreCase));
                            }

                            if (achievement != null)
                            {
                                achievement.Percent = y.Percent;
                            }
                            else
                            {
                                logger.Warn($"No Exophase (rarity) matching achievements found for {gameAchievements.Name} - {gameAchievements.Id} - {y.Name} in {achievementsUrl}");
                            }
                        });

                        gameAchievements.SetExtraHandlerDate("Exophase", str, "Rarity");

                        PluginDatabase.AddOrUpdate(gameAchievements);

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return providerMatched;
        }


        public bool SetMissingDescription(GameAchievements gameAchievements, MainAchievementHandler source)
        {
            string achievementsUrl = GetAchievementsPageUrl(gameAchievements, source);
            if (achievementsUrl.IsNullOrEmpty())
            {
                logger.Warn($"No Exophase (description) url find for {gameAchievements.Name} - {gameAchievements.Id}");
                return false;
            }

            try
            {
                GameAchievements exophaseAchievements = GetAchievementsInternal(
                    PluginDatabase.PlayniteApi.Database.Games.Get(gameAchievements.Id),
                    achievementsUrl
                );

                exophaseAchievements.Items.ForEach(y =>
                {
                    var achievement = gameAchievements.Items.Find(x => x.Name.Equals(y.Name, StringComparison.InvariantCultureIgnoreCase));
                    if (achievement == null)
                    {
                        achievement = gameAchievements.Items.Find(x => x.ApiName.Equals(y.Name, StringComparison.InvariantCultureIgnoreCase));
                    }

                    if (achievement != null)
                    {
                        if (achievement.Description.IsNullOrEmpty())
                        {
                            achievement.Description = y.Description;
                        }
                    }
                    else
                    {
                        logger.Warn($"No Exophase (description) matching achievements found for {gameAchievements.Name} - {gameAchievements.Id} - {y.Name} in {achievementsUrl}");
                    }
                });

                PluginDatabase.AddOrUpdate(gameAchievements);

                // if we got any data at all and none of the descriptions are blank now, say success and we stop, no need for others
                if (exophaseAchievements.Count > 0 && !gameAchievements.Items.Any(x => string.IsNullOrWhiteSpace(x.Description)))
                    return true;
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="exophaseGame"></param>
        /// <param name="playniteGame"></param>
        /// <param name="achievementSource"></param>
        /// <returns></returns>
        private static bool PlatformAndProviderMatch(SearchResult exophaseGame, GameAchievements playniteGame, MainAchievementHandler achievementSource)
        {
            switch (achievementSource.Name)
            {
                //PC: match service
                case "Steam":
                    return exophaseGame.Platforms.Contains("Steam", StringComparer.InvariantCultureIgnoreCase);
                case "GOG":
                    return exophaseGame.Platforms.Contains("GOG", StringComparer.InvariantCultureIgnoreCase);
                case "EA":
                    return exophaseGame.Platforms.Contains("Electronic Arts", StringComparer.InvariantCultureIgnoreCase);
                case "RetroAchievements":
                    return exophaseGame.Platforms.Contains("Retro", StringComparer.InvariantCultureIgnoreCase);
                case "Overwatch":
                case "Starcraft 2":
                case "Wow":
                    return exophaseGame.Platforms.Contains("Blizzard", StringComparer.InvariantCultureIgnoreCase);

                //Console: match platform
                case "PSN":
                case "Xbox":
                case "RPCS3":
                    return PlatformsMatch(exophaseGame, playniteGame);

                //case Services.SuccessStoryDatabase.AchievementSource.None:
                //case Services.SuccessStoryDatabase.AchievementSource.Local:
                default:
                    return false;
            }
        }

        private static Dictionary<string, string[]> PlaynitePlatformSpecificationIdToExophasePlatformName = new Dictionary<string, string[]>
        {
            { "xbox360", new[]{"Xbox 360"} },
            { "xbox_one", new[]{"Xbox One"} },
            { "xbox_series", new[]{"Xbox Series"} },
            { "xbox_game_pass", new []{"Windows 8", "Windows 10", "Windows 11", "GFWL", "Xbox 360", "Xbox One", "Xbox Series" } },
            { "pc_windows", new []{"Windows 8", "Windows 10", "Windows 11" /* future proofing */, "GFWL"} },
            { "sony_playstation3", new[]{"PS3"} },
            { "sony_playstation4", new[]{"PS4"} },
            { "sony_playstation5", new[]{"PS5"} },
            { "sony_vita", new[]{"PS Vita"} },
        };

        private static bool PlatformsMatch(SearchResult exophaseGame, GameAchievements playniteGame)
        {
            foreach (var playnitePlatform in playniteGame.Platforms)
            {
                string[] exophasePlatformNames;
                string sourceName = PluginDatabase.PlayniteApi.Database.Games.Get(playniteGame.Id).Source?.Name;
                if (sourceName == "Xbox Game Pass")
                {
                    if (!PlaynitePlatformSpecificationIdToExophasePlatformName.TryGetValue("xbox_game_pass", out exophasePlatformNames))
                        continue;
                }
                else
                {
                    if (!PlaynitePlatformSpecificationIdToExophasePlatformName.TryGetValue(playnitePlatform.SpecificationId, out exophasePlatformNames))
                        continue; //there are no natural matches between default Playnite platform name and Exophase platform name, so give up if it's not in the dictionary
                }

                if (exophaseGame.Platforms.IntersectsExactlyWith(exophasePlatformNames))
                    return true;
            }
            return false;
        }
        #endregion




        private bool GetHiddenDescriptions(GameAchievements gameAchievements)
        {
            if (!gameAchievements.HasAchievements)
                return false;
            if (!gameAchievements.Items.Any(x => string.IsNullOrWhiteSpace(x.Description)))
                return false;
            return SetMissingDescription(gameAchievements, gameAchievements.Handler);
        }
        private bool RefreshRarity(GameAchievements gameAchievements)
        {
            return SetRarity(gameAchievements, gameAchievements.Handler);
        }

        public int GetAugmenterRank(string augmenter)
        {
            switch (augmenter)
            {
                case "Rarity":
                    return 1;
                case "HiddenDescription":
                    return 2;
            }
            return 0;
        }
        public string[] GetAugmentTypes() => new string[] { "Rarity", "HiddenDescription" };
        public string[] GetAugmentTypesManual() => GetAugmentTypes();

        public bool RefreshAugmenterMetadata(string augmenter, GameAchievements gameAchievements)
        {
            switch (augmenter)
            {
                case "Rarity":
                    return RefreshRarity(gameAchievements);
                case "HiddenDescription":
                    return GetHiddenDescriptions(gameAchievements);
            }
            return false;
        }
    }
}
