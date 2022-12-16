using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using CommonPlayniteShared.Common.Web;
using CommonPluginsShared;
using CommonPluginsShared.Extensions;
using CommonPluginsShared.Models;
using CommonPluginsStores.Steam;
using CommonPluginsStores.Steam.Models;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using SteamKit2;
using SuccessStory.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using static CommonPluginsShared.PlayniteTools;
using static SuccessStory.Services.SuccessStoryDatabase;
using PlayniteTools = CommonPluginsShared.PlayniteTools;

namespace SuccessStory.Clients
{
    class SteamAchievementsFactory : IAchievementFactory
    {
        public void BuildClient(Dictionary<AchievementSource, GenericAchievements> Providers, Dictionary<AchievementSource, ISearchableManualAchievements> ManualSearchProviders, Dictionary<AchievementSource, IMetadataAugmentAchievements> AchievementMetadataAugmenters)
        {
            SteamAchievements tmp = new SteamAchievements();
            Providers[AchievementSource.Steam] = tmp;
            ManualSearchProviders[AchievementSource.Steam] = tmp;
        }
    }
    /*class LocalAchievements : SteamAchievements
    {
        public LocalAchievements()
        {
            SetLocal();
        }
        public override int CheckAchivementSourceRank(ExternalPlugin pluginType, SuccessStorySettings settings, Game game, bool ignoreSpecial = false)
        {
            if (settings.EnableLocal)
            {
                return 1;
            }
            return 0;
        }
    }*/
    class SteamAchievements : GenericAchievements, ISearchableManualAchievements, IMetadataAugmentAchievements
    {
        protected static SteamApi _steamApi;
        internal static SteamApi steamApi
        {
            get
            {
                if (_steamApi == null)
                {
                    _steamApi = new SteamApi(PluginDatabase.PluginName);
                }
                return _steamApi;
            }

            set => _steamApi = value;
        }

        private IHtmlDocument HtmlDocument { get; set; } = null;

        //protected bool IsLocal { get; set; } = false;
        //private bool IsManual { get; set; } = false;
 
        private static string SteamId { get; set; } = string.Empty;
        private static string SteamApiKey { get; set; } = string.Empty;
        private static string SteamUser { get; set; } = string.Empty;

        private static string UrlProfil         => @"https://steamcommunity.com/my/profile";
        private static string UrlProfilById     => @"https://steamcommunity.com/profiles/{0}/stats/{1}?tab=achievements&l={2}";
        private static string UrlProfilByName   => @"https://steamcommunity.com/id/{0}/stats/{1}?tab=achievements&l={2}";

        private static string UrlAchievements   => @"https://steamcommunity.com/stats/{0}/achievements/?l={1}";

        private static string UrlSearch         => @"https://store.steampowered.com/search/?term={0}";


        public SteamAchievements() : base("Steam", CodeLang.GetSteamLang(PluginDatabase.PlayniteApi.ApplicationSettings.Language))
        {
        }


        #region Searchable Manual Achievements
        public string Glyph => "\uE906";
        public List<SearchResult> SearchGame(string Name)
        {
            List<SearchResult> ListSearchGames = new List<SearchResult>();

            string Url = string.Empty;
            try
            {
                Url = string.Format(UrlSearch, WebUtility.UrlEncode(Name));
                string DataSteamSearch = Web.DownloadStringData(Url).GetAwaiter().GetResult();
                IHtmlDocument htmlDocument = new HtmlParser().Parse(DataSteamSearch);

                int index = 0;
                foreach (var gameElem in htmlDocument.QuerySelectorAll(".search_result_row"))
                {
                    if (index == 10)
                    {
                        break;
                    }

                    var url = gameElem.GetAttribute("href");
                    var title = gameElem.QuerySelector(".title").InnerHtml;
                    var img = gameElem.QuerySelector(".search_capsule img").GetAttribute("src");
                    var releaseDate = gameElem.QuerySelector(".search_released").InnerHtml;
                    bool NoPrice = string.IsNullOrWhiteSpace(gameElem.QuerySelector(".search_price").InnerHtml.Trim());
                    if (gameElem.HasAttribute("data-ds-packageid"))
                    {
                        continue;
                    }

                    int.TryParse(gameElem.GetAttribute("data-ds-appid"), out int gameId);

                    int AchievementsCount = 0;
                    if (!PluginDatabase.PluginSettings.Settings.EnableSteamWithoutWebApi & IsConfigured())
                    {
                        if (gameId != 0)
                        {
                            try
                            {
                                using (dynamic steamWebAPI = WebAPI.GetInterface("ISteamUserStats", SteamApiKey))
                                {
                                    KeyValue SchemaForGame = steamWebAPI.GetSchemaForGame(appid: gameId, l: LocalLang);
                                    AchievementsCount = SchemaForGame.Children?.Find(x => x.Name == "availableGameStats")?.Children?.Find(x => x.Name == "achievements")?.Children?.Count ?? 0;
                                }
                            }
                            catch (WebException ex) when (ex.Response is HttpWebResponse && (ex.Response as HttpWebResponse).StatusCode == HttpStatusCode.Forbidden)
                            {
                                if (!NoPrice)
                                    throw;
                            }
                        }
                    }
                    else
                    {
                        DataSteamSearch = Web.DownloadStringData(string.Format(url, WebUtility.UrlEncode(Name))).GetAwaiter().GetResult();
                        IHtmlDocument htmlDocumentDetails = new HtmlParser().Parse(DataSteamSearch);

                        var AchievementsInfo = htmlDocumentDetails.QuerySelector("#achievement_block .block_title");
                        if (AchievementsInfo != null)
                        {
                            int.TryParse(Regex.Replace(AchievementsInfo.InnerHtml, "[^0-9]", ""), out AchievementsCount);
                        }
                    }

                    if (gameId != 0)
                    {
                        ListSearchGames.Add(new SearchResult
                        {
                            Name = WebUtility.HtmlDecode(title),
                            Url = url,
                            UrlImage = img,
                            AppId = gameId,
                            AchievementsCount = AchievementsCount
                        });
                    }

                    index++;
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, $"Error with SearchGame{Name} on {Url}", true, PluginDatabase.PluginName);
            }

            return ListSearchGames;
        }
        public bool CanDoManualAchievements(Game game, GameAchievements gameAchievements)
        {
            return gameAchievements.SourcesLink?.Name.IsEqual("steam") ?? false;
        }
        public GameAchievements DoManualAchievements(Game game, GameAchievements gameAchievements)
        {
            string str = gameAchievements.SourcesLink?.Url.Replace("https://steamcommunity.com/stats/", string.Empty).Replace("/achievements", string.Empty);
            int.TryParse(str, out int AppId);
            return GetAchievementsInternal(game, AppId, false);
        }
        public GameAchievements GetManualAchievements(Game game, SearchResult searchResult)
        {
            return GetAchievementsInternal(game, searchResult.AppId, false);
        }
        #endregion


        public override int CheckAchivementSourceRank(ExternalPlugin pluginType, SuccessStorySettings settings, Game game, bool ignoreSpecial = false)
        {
            if (pluginType == ExternalPlugin.SteamLibrary && settings.EnableSteam)
            {
                return 100;
            }
            return 0;
        }



        public override GameAchievements GetAchievements(Game game)
        {
            int AppId = 0;
            int.TryParse(game.GameId, out AppId);
            return GetAchievementsInternal(game, AppId, true);
        }


        private GameAchievements GetAchievementsInternal(Game game, int AppId, bool ProgressOnSteam)
        {
            GameAchievements gameAchievements = SuccessStory.PluginDatabase.GetDefault(game);
            List<Achievements> AllAchievements = new List<Achievements>();
            List<GameStats> AllStats = new List<GameStats>();

            bool GetByWeb = false;

            // Get Steam configuration if exist.
            if (!IsConfigured())
            {
                return gameAchievements;
            }

            logger.Info($"GetAchievementsInternal() - ProgressOnSteam : {ProgressOnSteam}, EnableSteamWithoutWebApi: {PluginDatabase.PluginSettings.Settings.EnableSteamWithoutWebApi}, SteamIsPrivate: {PluginDatabase.PluginSettings.Settings.SteamIsPrivate}");

            if (AppId != 0 && ProgressOnSteam)
            {
                if (PluginDatabase.PluginSettings.Settings.EnableSteamWithoutWebApi || PluginDatabase.PluginSettings.Settings.SteamIsPrivate)
                {
                    AllAchievements = GetPlayerAchievementsByWebPage(AppId);
                    GetByWeb = true;
                }
                else
                {
                    VerifSteamUser();
                    if (SteamUser.IsNullOrEmpty())
                    {
                        logger.Warn("No Steam user");
                    }

                    AllAchievements = GetPlayerAchievementsByWebApi(AppId);
                    AllStats = GetUsersStatsByWebApi(AppId);
                }

                if (AllAchievements.Count > 0)
                {
                    Tuple<List<Achievements>, List<GameStats>> DataCompleted = GetSchemaForGame(AppId, AllAchievements, AllStats);

                    bool IsOK = GetByWeb ? GetByWeb : Web.DownloadFileImageTest(AllAchievements[0].UrlLocked).GetAwaiter().GetResult();
                    if (IsOK)
                    {
                        AllAchievements = DataCompleted.Item1;
                        AllStats = DataCompleted.Item2;


                        gameAchievements.Items = AllAchievements;
                        gameAchievements.ItemsStats = AllStats;


                        // Set source link
                        if (gameAchievements.HasAchievements)
                        {
                            gameAchievements.SourcesLink = new SourceLink
                            {
                                GameName = steamApi.GetGameName(AppId),
                                Name = "Steam",
                                Url = string.Format(UrlProfilById, SteamId, AppId, LocalLang)
                            };
                            gameAchievements.Handlers = new HashSet<AchievementHandler>() { new AchievementHandler("Steam", AppId.ToString()) };
                        }
                    }
                }

                // Set progression
                if (gameAchievements.HasAchievements)
                {
                    gameAchievements.Items = GetProgressionByWeb(gameAchievements.Items, string.Format(UrlProfilById, SteamId, AppId, LocalLang));
                }
            }
            else
            {
                Common.LogDebug(true, $"GetAchievementsLocal()");

                if (PluginDatabase.PluginSettings.Settings.EnableSteamWithoutWebApi)
                {
                    logger.Warn($"Option without API key is enabled");
                }
                else if (SteamApiKey.IsNullOrEmpty())
                {
                    logger.Warn($"No Steam API key");
                }
                else
                {
                    if (AppId == 0) // we have to go the emulator route because we don't have an AppID, so something strange is going on
                    {
                        SteamEmulators se = new SteamEmulators(PluginDatabase.PluginSettings.Settings.LocalPath);
                        var temp = se.GetAchievementsLocal(game, SteamApiKey, 0, true);
                        AppId = se.GetSteamId(); // TODO: this logic is poor, but I don't use a Steam Emulator so I can't test it

                        if (temp.Items.Count > 0)
                        {
                            for (int i = 0; i < temp.Items.Count; i++)
                            {
                                AllAchievements.Add(new Achievements
                                {
                                    Name = temp.Items[i].Name,
                                    ApiName = temp.Items[i].ApiName,
                                    Description = temp.Items[i].Description,
                                    UrlUnlocked = temp.Items[i].UrlUnlocked,
                                    UrlLocked = temp.Items[i].UrlLocked,
                                    DateUnlocked = temp.Items[i].DateUnlocked
                                });
                            }

                            gameAchievements.Items = AllAchievements;
                            gameAchievements.ItemsStats = temp.ItemsStats;


                            // Set source link
                            if (gameAchievements.HasAchievements)
                            {
                                gameAchievements.SourcesLink = new SourceLink
                                {
                                    GameName = steamApi.GetGameName(AppId),
                                    Name = "Steam",
                                    Url = $"https://steamcommunity.com/stats/{AppId}/achievements"
                                };
                                gameAchievements.Handlers = new HashSet<AchievementHandler>() { new AchievementHandler("Steam", AppId.ToString()) };
                            }
                        }
                    }
                    else
                    {
                        using (dynamic steamWebAPI = WebAPI.GetInterface("ISteamUserStats", SteamApiKey))
                        {
                            KeyValue SchemaForGame = steamWebAPI.GetSchemaForGame(appid: AppId, l: LocalLang);

                            try
                            {
                                foreach (KeyValue AchievementsData in SchemaForGame.Children?.Find(x => x.Name == "availableGameStats").Children?.Find(x => x.Name == "achievements").Children)
                                {
                                    Achievements ach = AllAchievements.Find(x => x.ApiName.IsEqual(AchievementsData.Name));
                                    if (ach == null)
                                    {
                                        AllAchievements.Add(new Achievements
                                        {
                                            Name = AchievementsData.Children.Find(x => x.Name.IsEqual("displayName")).Value,
                                            ApiName = AchievementsData.Name,
                                            Description = AchievementsData.Children.Find(x => x.Name.IsEqual("description"))?.Value,
                                            UrlUnlocked = AchievementsData.Children.Find(x => x.Name.IsEqual("icon")).Value,
                                            UrlLocked = AchievementsData.Children.Find(x => x.Name.IsEqual("icongray")).Value,
                                            IsHidden = AchievementsData.Children.Find(x => x.Name.IsEqual("hidden")).Value == "1",
                                            DateUnlocked = default(DateTime),
                                        });
                                    }
                                    else
                                    {
                                        ach.Name = AchievementsData.Children.Find(x => x.Name.IsEqual("displayName")).Value;
                                        ach.Description = AchievementsData.Children.Find(x => x.Name.IsEqual("description"))?.Value ?? ach.Description;
                                        ach.UrlUnlocked = AchievementsData.Children.Find(x => x.Name.IsEqual("icon")).Value;
                                        ach.UrlLocked = AchievementsData.Children.Find(x => x.Name.IsEqual("icongray")).Value;
                                        ach.IsHidden = AchievementsData.Children.Find(x => x.Name.IsEqual("hidden")).Value == "1";
                                        ach.DateUnlocked = ach.DateUnlocked ?? default(DateTime); // not sure this can happen but let's keep the safety
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Common.LogError(ex, false, $"Error on AchievementsData({AppId}, {LocalLang})", true, PluginDatabase.PluginName);
                            }

                            /*try
                            {
                                var availableGameStats = SchemaForGame.Children.Find(x => x.Name.IsEqual("availableGameStats"));

                                if (availableGameStats != null)
                                {
                                    var stats = availableGameStats.Children.Find(x => x.Name.IsEqual("stats"));

                                    if (stats != null)
                                    {
                                        var ListStatsData = stats.Children;
                                        foreach (KeyValue StatsData in ListStatsData)
                                        {
                                            if (AllStats.Find(x => x.Name.IsEqual(StatsData.Name)) == null)
                                            {
                                                double.TryParse(StatsData.Children.Find(x => x.Name.IsEqual("defaultvalue")).Value, out double ValueStats);

                                                AllStats.Add(new GameStats
                                                {
                                                    Name = StatsData.Name,
                                                    DisplayName = StatsData.Children.Find(x => x.Name.IsEqual("displayName")).Value,
                                                    Value = ValueStats
                                                });
                                            }
                                            else
                                            {
                                                AllStats.Find(x => x.Name.IsEqual(StatsData.Name)).DisplayName = StatsData.Children.Find(x => x.Name.IsEqual("displayName")).Value;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        logger.Info($"No Steam stats for {AppId}");
                                    }
                                }
                                else
                                {
                                    logger.Info($"No Steam stats for {AppId}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Common.LogError(ex, false, $"Error on AvailableGameStats({AppId}, {LocalLang})", true, PluginDatabase.PluginName);
                            }*/

                            gameAchievements.Items = AllAchievements;
                            //gameAchievements.ItemsStats = AllStats;

                            if (gameAchievements.HasAchievements)
                            {
                                gameAchievements.SourcesLink = new SourceLink
                                {
                                    GameName = steamApi.GetGameName(AppId),
                                    Name = "Steam",
                                    Url = $"https://steamcommunity.com/stats/{AppId}/achievements"
                                };
                                gameAchievements.Handlers = new HashSet<AchievementHandler>() { new AchievementHandler("Steam", AppId.ToString()) };
                            }
                        }
                    }
                }
            }

            // Set rarity
            if (gameAchievements.HasAchievements)
            {
                if (ProgressOnSteam && (PluginDatabase.PluginSettings.Settings.EnableSteamWithoutWebApi || PluginDatabase.PluginSettings.Settings.SteamIsPrivate))
                {
                    try
                    {
                        gameAchievements.Items = GetGlobalAchievementPercentagesForAppByWebPage(AppId, gameAchievements.Items);
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }
                else
                {
                    try
                    {
                        gameAchievements.Items = GetGlobalAchievementPercentagesForAppByWebApi(AppId, gameAchievements.Items);
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }
            }

            // Set missing description
            if (gameAchievements.HasAchievements)
            {
                if (ProgressOnSteam)
                {
                    gameAchievements.Items.ForEach(x =>
                    {
                        if (x.IsHidden && x.Description.IsNullOrEmpty())
                        {
                            x.Description = FindHiddenDescription(AppId, x.Name);
                        }
                    });
                }
                /*
                ExophaseAchievements exophaseAchievements = new ExophaseAchievements();
                exophaseAchievements.SetMissingDescription(gameAchievements, AchievementSourceOld.Steam);
                */
            }

            return gameAchievements;
        }



        #region Configuration
        public override bool ValidateConfiguration()
        {
            if (PlayniteTools.IsDisabledPlaynitePlugins("SteamLibrary"))
            {
                ShowNotificationPluginDisable(resources.GetString("LOCSuccessStoryNotificationsSteamDisabled"));
                return false;
            }
            else
            {
                if (CachedConfigurationValidationResult == null)
                {
                    if (!IsConfigured())
                    {
                        ShowNotificationPluginNoConfiguration(resources.GetString("LOCSuccessStoryNotificationsSteamBadConfig"));
                        CachedConfigurationValidationResult = false;
                    }

                    if (!PluginDatabase.PluginSettings.Settings.SteamIsPrivate && !CheckIsPublic())
                    {
                        ShowNotificationPluginNoPublic(resources.GetString("LOCSuccessStoryNotificationsSteamPrivate"));
                        CachedConfigurationValidationResult = false;
                    }

                    if (PluginDatabase.PluginSettings.Settings.SteamIsPrivate && !IsConnected())
                    {
                        ResetCachedIsConnectedResult();
                        Thread.Sleep(2000);
                        if (PluginDatabase.PluginSettings.Settings.SteamIsPrivate && !IsConnected())
                        {
                            ShowNotificationPluginNoAuthenticate(resources.GetString("LOCSuccessStoryNotificationsSteamNoAuthenticate"), PlayniteTools.ExternalPlugin.SteamLibrary);
                            CachedConfigurationValidationResult = false;
                        }
                    }
                    
                    if (CachedConfigurationValidationResult == null)
                    {
                        CachedConfigurationValidationResult = true;
                    }

                    if (!(bool)CachedConfigurationValidationResult)
                    {
                        ShowNotificationPluginErrorMessage();
                    }
                }
                else if (!(bool)CachedConfigurationValidationResult)
                {
                    ShowNotificationPluginErrorMessage();
                }

                return (bool)CachedConfigurationValidationResult;
            }
        }

        public override bool IsConnected()
        {
            if (CachedIsConnectedResult == null)
            {
                if (IsConfigured())
                {
                    string ProfileById = $"https://steamcommunity.com/profiles/{SteamId}";
                    string ProfileByName = $"https://steamcommunity.com/id/{SteamUser}";

                    CachedIsConnectedResult = IsProfileConnected(ProfileById) || IsProfileConnected(ProfileByName);
                }
            }

            return (bool)CachedIsConnectedResult;
        }

        public override bool IsConfigured()
        {
            if (SteamId.IsNullOrEmpty() || SteamApiKey.IsNullOrEmpty() || SteamUser.IsNullOrEmpty())
            {
                try
                {
                    if (File.Exists(PluginDatabase.Paths.PluginUserDataPath + "\\..\\CB91DFC9-B977-43BF-8E70-55F46E410FAB\\config.json"))
                    {
                        dynamic SteamConfig = Serialization.FromJsonFile<dynamic>(PluginDatabase.Paths.PluginUserDataPath + "\\..\\CB91DFC9-B977-43BF-8E70-55F46E410FAB\\config.json");
                        SteamId = (string)SteamConfig["UserId"];
                        SteamApiKey = (string)SteamConfig["ApiKey"];
                        SteamUser = steamApi.GetSteamUsers()?.First()?.PersonaName;                       
                    }
                    else
                    {
                        ShowNotificationPluginNoConfiguration(resources.GetString("LOCSuccessStoryNotificationsSteamBadConfig1"));
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    ShowNotificationPluginError(ex);
                    return false;
                }
            }

            SteamUserAndSteamIdByWeb();

            if (PluginDatabase.PluginSettings.Settings.EnableSteamWithoutWebApi)
            {                
                if (SteamUser.IsNullOrEmpty())
                {
                    ShowNotificationPluginNoConfiguration(resources.GetString("LOCSuccessStoryNotificationsSteamBadConfig2"));
                    return false;
                }
            }
            else
            {
                if (SteamId.IsNullOrEmpty() || SteamApiKey.IsNullOrEmpty())
                {
                    ShowNotificationPluginNoConfiguration(resources.GetString("LOCSuccessStoryNotificationsSteamBadConfig1"));
                    return false;
                }
            }

            return true;
        }

        public override bool EnabledInSettings()
        {
            //if (IsLocal)
            //{
            //    return PluginDatabase.PluginSettings.Settings.EnableLocal;
            //}
            //else
            //{
                return PluginDatabase.PluginSettings.Settings.EnableSteam;
            //}
        }
        #endregion


        //public void SetLocal()
        //{
        //    IsLocal = true;
        //}

        //public void SetManual()
        //{
        //    IsManual = true;
        //}


        #region Steam


        private void SteamUserAndSteamIdByWeb()
        {
            if (SteamUser.IsNullOrEmpty() || SteamId.IsNullOrEmpty())
            {
                List<HttpCookie> cookies = GetCookies();
                string ResultWeb = Web.DownloadStringData(UrlProfil, cookies).GetAwaiter().GetResult();

                if (SteamUser.IsNullOrEmpty())
                {
                    HtmlParser parser = new HtmlParser();
                    IHtmlDocument htmlDocument = parser.Parse(ResultWeb);

                    var el = htmlDocument.QuerySelector(".actual_persona_name");
                    if (el != null)
                    {
                        SteamUser = el.InnerHtml;
                    }
                }

                if (SteamId.IsNullOrEmpty())
                {
                    int index = ResultWeb.IndexOf("g_steamID = ");
                    if (index > -1)
                    {
                        ResultWeb = ResultWeb.Substring(index + "g_steamID  = ".Length);

                        index = ResultWeb.IndexOf("g_strLanguage =");
                        ResultWeb = ResultWeb.Substring(0, index).Trim();

                        ResultWeb = ResultWeb.Substring(0, ResultWeb.Length - 1).Trim();

                        SteamId = Regex.Replace(ResultWeb, @"[^\d]", string.Empty);
                    }
                }
            }
        }


        private void VerifSteamUser()
        {
            if (PluginDatabase.PluginSettings.Settings.EnableSteamWithoutWebApi)
            {
                return;
            }

            if (SteamApiKey.IsNullOrEmpty())
            {
                logger.Warn($"No Steam API key");
                return;
            }

            try
            {
                using (dynamic steamWebAPI = WebAPI.GetInterface("ISteamUser", SteamApiKey))
                {
                    KeyValue PlayerSummaries = steamWebAPI.GetPlayerSummaries(steamids: SteamId);
                    string personaname = (string)PlayerSummaries["players"]["player"].Children[0].Children.Find(x => x.Name == "personaname").Value;

                    if (personaname != SteamUser)
                    {
                        logger.Warn($"Steam user is different {SteamUser} != {personaname}");
                        SteamUser = personaname;
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }


        public bool CheckIsPublic()
        {
            if (IsConfigured())
            {
                string ProfileById = $"https://steamcommunity.com/profiles/{SteamId}";
                string ProfileByName = $"https://steamcommunity.com/id/{SteamUser}";

                return IsProfilePublic(ProfileById) || IsProfilePublic(ProfileByName);
            }

            return false;
        }

        private bool IsProfilePublic(string profilePageUrl)
        {
            try
            {
                string ResultWeb = HttpDownloader.DownloadString(profilePageUrl);
                IHtmlDocument HtmlDoc = new HtmlParser().Parse(ResultWeb);

                //this finds the Games link on the right side of the profile page. If that's public then so are achievements.
                var gamesPageLink = HtmlDoc.QuerySelector(@".profile_item_links a[href$=""/games/?tab=all""]");
                return gamesPageLink != null;
            }
            catch (WebException ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
                return false;
            }
        }

        private bool IsProfileConnected(string profilePageUrl)
        {
            try
            {
                List<HttpCookie> cookies = GetCookies();
                string ResultWeb = Web.DownloadStringData(profilePageUrl, cookies).GetAwaiter().GetResult();

                //this finds the Games link on the right side of the profile page. If that's public then so are achievements.
                IHtmlDocument HtmlDoc = new HtmlParser().Parse(ResultWeb);
                var gamesPageLink = HtmlDoc.QuerySelector(@".profile_item_links a[href$=""/games/?tab=all""]");
                if (gamesPageLink != null)
                {
                    return true;
                }

                using (IWebView WebViewOffscreen = API.Instance.WebViews.CreateOffscreenView())
                {
                    WebViewOffscreen.NavigateAndWait(profilePageUrl);
                    ResultWeb = WebViewOffscreen.GetPageSource();
                    cookies = WebViewOffscreen.GetCookies()?.Where(x => x?.Domain?.Contains("steam") ?? false)?.ToList() ?? new List<HttpCookie>();
                }

                //this finds the Games link on the right side of the profile page. If that's public then so are achievements.
                HtmlDoc = new HtmlParser().Parse(ResultWeb);
                gamesPageLink = HtmlDoc.QuerySelector(@".profile_item_links a[href$=""/games/?tab=all""]");
                if (gamesPageLink != null)
                {
                    SetCookies(cookies);
                    return true;
                }

                return false;
            }
            catch (WebException ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
                return false;
            }
        }


        private List<GameStats> GetUsersStatsByWebApi(int AppId)
        {
            List<GameStats> AllStats = new List<GameStats>();

            if (PluginDatabase.PluginSettings.Settings.EnableSteamWithoutWebApi)
            {
                return AllStats;
            }

            if (SteamApiKey.IsNullOrEmpty())
            {
                logger.Warn($"No Steam API key");
                return AllStats;
            }

            try
            {
                using (dynamic steamWebAPI = WebAPI.GetInterface("ISteamUserStats", SteamApiKey))
                {
                    KeyValue UserStats = steamWebAPI.GetUserStatsForGame(steamid: SteamId, appid: AppId, l: LocalLang);

                    if (UserStats != null && UserStats.Children != null)
                    {
                        var UserStatsData = UserStats.Children.Find(x => x.Name == "stats");
                        if (UserStatsData != null)
                        {
                            foreach (KeyValue StatsData in UserStatsData.Children)
                            {
                                double.TryParse(StatsData.Children.First().Value.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator), out double ValueStats);

                                AllStats.Add(new GameStats
                                {
                                    Name = StatsData.Name,
                                    DisplayName = string.Empty,
                                    Value = ValueStats
                                });
                            }
                        }
                    }
                }
            }
            // TODO With recent SteamKit
            //catch (WebAPIRequestException wex)
            //{
            //    if (wex.StatusCode == HttpStatusCode.Forbidden)
            //    {
            //        _PlayniteApi.Notifications.Add(new NotificationMessage(
            //            $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-PrivateProfil",
            //            $"{PluginDatabase.PluginName} - Steam profil is private",
            //            NotificationType.Error
            //        ));
            //        logger.Warn("Steam profil is private");
            //    }
            //    else
            //    {
            //        Common.LogError(wex, false, $"Error on GetUsersStats({SteamId}, {AppId}, {LocalLang})");
            //    }
            //}
            catch (WebException ex)
            {
                if (ex.Status == WebExceptionStatus.ProtocolError)
                {
                    if (ex.Response is HttpWebResponse response)
                    {
                        if (response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                                $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-PrivateProfil",
                                $"{PluginDatabase.PluginName}\r\n{resources.GetString("LOCSuccessStoryNotificationsSteamPrivate")}",
                                NotificationType.Error,
                                () => Process.Start(@"https://steamcommunity.com/my/edit/settings")
                            ));
                            logger.Warn("Steam profil is private");

                            // TODO https://github.com/Lacro59/playnite-successstory-plugin/issues/76
                            Common.LogError(ex, false, $"Error on GetUsersStats({SteamId}, {AppId}, {LocalLang})", true, PluginDatabase.PluginName);
                        }
                    }
                    else
                    {
                        // no http status code available
                        Common.LogError(ex, false, $"Error on GetUsersStats({SteamId}, {AppId}, {LocalLang})", true, PluginDatabase.PluginName);
                    }
                }
                else
                {
                    // no http status code available
                    Common.LogError(ex, false, $"Error on GetUsersStats({SteamId}, {AppId}, {LocalLang})", true, PluginDatabase.PluginName);
                }
            }

            return AllStats;
        }

        private List<Achievements> GetPlayerAchievementsByWebApi(int AppId)
        {
            List<Achievements> AllAchievements = new List<Achievements>();

            if (PluginDatabase.PluginSettings.Settings.EnableSteamWithoutWebApi)
            {
                return AllAchievements;
            }

            if (SteamApiKey.IsNullOrEmpty())
            {
                logger.Warn($"No Steam API key");
                return AllAchievements;
            }

            try
            {
                using (dynamic steamWebAPI = WebAPI.GetInterface("ISteamUserStats", SteamApiKey))
                {
                    KeyValue PlayerAchievements = steamWebAPI.GetPlayerAchievements(steamid: SteamId, appid: AppId, l: LocalLang);

                    if (PlayerAchievements != null && PlayerAchievements.Children != null)
                    {
                        var PlayerAchievementsData = PlayerAchievements.Children.Find(x => x.Name == "achievements");
                        if (PlayerAchievementsData != null)
                        {
                            foreach (KeyValue AchievementsData in PlayerAchievementsData.Children)
                            {
                                int.TryParse(AchievementsData.Children.Find(x => x.Name == "unlocktime").Value, out int unlocktime);
                                bool achieved = int.Parse(AchievementsData.Children.Find(x => x.Name == "achieved").Value) == 1;

                                AllAchievements.Add(new Achievements
                                {
                                    ApiName = AchievementsData.Children.Find(x => x.Name == "apiname").Value,
                                    Name = AchievementsData.Children.Find(x => x.Name == "name").Value,
                                    Description = AchievementsData.Children.Find(x => x.Name == "description").Value,
                                    DateUnlocked = achieved ? new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(unlocktime).ToLocalTime() : default(DateTime)
                                });
                            }
                        }
                    }
                }
            }
            // TODO With recent SteamKit
            //catch (WebAPIRequestException wex)
            //{
            //    if (wex.StatusCode == HttpStatusCode.Forbidden)
            //    {
            //        _PlayniteApi.Notifications.Add(new NotificationMessage(
            //            $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-PrivateProfil",
            //            $"{PluginDatabase.PluginName} - Steam profil is private",
            //            NotificationType.Error
            //        ));
            //        logger.Warn("Steam profil is private");
            //    }
            //    else
            //    {
            //        Common.LogError(wex, false, $"Error on GetPlayerAchievements({SteamId}, {AppId}, {LocalLang})");
            //    }
            //}
            catch (WebException ex)
            {
                if (ex != null && ex.Status == WebExceptionStatus.ProtocolError)
                {
                    if (ex.Response is HttpWebResponse response)
                    {
                        if (response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                                $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-PrivateProfil",
                                $"{PluginDatabase.PluginName}\r\n{resources.GetString("LOCSuccessStoryNotificationsSteamPrivate")}",
                                NotificationType.Error,
                                () => Process.Start(@"https://steamcommunity.com/my/edit/settings")
                            ));
                            logger.Warn("Steam profil is private");

                            // TODO https://github.com/Lacro59/playnite-successstory-plugin/issues/76
                            Common.LogError(ex, false, $"Error on GetPlayerAchievements({SteamId}, {AppId}, {LocalLang})", true, PluginDatabase.PluginName);
                        }
                    }
                    else
                    {
                        // no http status code available
                        Common.LogError(ex, false, $"Error on GetPlayerAchievements({SteamId}, {AppId}, {LocalLang})", true, PluginDatabase.PluginName);
                    }
                }
                else
                {
                    // no http status code available
                    Common.LogError(ex, false, $"Error on GetPlayerAchievements({SteamId}, {AppId}, {LocalLang})", true, PluginDatabase.PluginName);
                }
            }

            return AllAchievements;
        }

        private Tuple<List<Achievements>, List<GameStats>> GetSchemaForGame(int AppId, List<Achievements> AllAchievements, List<GameStats> AllStats)
        {
            try
            {
                if (PluginDatabase.PluginSettings.Settings.EnableSteamWithoutWebApi)
                {
                    return Tuple.Create(AllAchievements, AllStats);
                }

                if (SteamApiKey.IsNullOrEmpty())
                {
                    logger.Warn($"No Steam API key");
                    return Tuple.Create(AllAchievements, AllStats);
                }

                using (dynamic steamWebAPI = WebAPI.GetInterface("ISteamUserStats", SteamApiKey))
                {
                    KeyValue SchemaForGame = steamWebAPI.GetSchemaForGame(appid: AppId, l: LocalLang);

                    try
                    {
                        foreach (KeyValue AchievementsData in SchemaForGame.Children?.Find(x => x.Name == "availableGameStats").Children?.Find(x => x.Name == "achievements").Children)
                        {
                            AllAchievements.Find(x => x.ApiName.IsEqual(AchievementsData.Name)).IsHidden = AchievementsData.Children?.Find(x => x.Name.IsEqual("hidden")).Value == "1";
                            AllAchievements.Find(x => x.ApiName.IsEqual(AchievementsData.Name)).UrlUnlocked = AchievementsData.Children?.Find(x => x.Name.IsEqual("icon")).Value;
                            AllAchievements.Find(x => x.ApiName.IsEqual(AchievementsData.Name)).UrlLocked = AchievementsData.Children?.Find(x => x.Name.IsEqual("icongray")).Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, $"Error on AchievementsData({AppId}, {LocalLang})", true, PluginDatabase.PluginName);
                    }

                    try
                    {
                        var availableGameStats = SchemaForGame.Children.Find(x => x.Name.IsEqual("availableGameStats"));

                        if (availableGameStats != null)
                        {
                            var stats = availableGameStats.Children.Find(x => x.Name.IsEqual("stats"));

                            if (stats != null)
                            {
                                var ListStatsData = stats.Children;
                                foreach (KeyValue StatsData in ListStatsData)
                                {
                                    if (AllStats.Find(x => x.Name.IsEqual(StatsData.Name)) == null)
                                    {
                                        double.TryParse(StatsData.Children.Find(x => x.Name.IsEqual("defaultvalue")).Value, out double ValueStats);

                                        AllStats.Add(new GameStats
                                        {
                                            Name = StatsData.Name,
                                            DisplayName = StatsData.Children.Find(x => x.Name.IsEqual("displayName")).Value,
                                            Value = ValueStats
                                        });
                                    }
                                    else
                                    {
                                        AllStats.Find(x => x.Name.IsEqual(StatsData.Name)).DisplayName = StatsData.Children.Find(x => x.Name.IsEqual("displayName")).Value;
                                    }
                                }
                            }
                            else
                            {
                                logger.Info($"No Steam stats for {AppId}");
                            }
                        }
                        else
                        {
                            logger.Info($"No Steam stats for {AppId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, $"Error on AvailableGameStats({AppId}, {LocalLang})", true, PluginDatabase.PluginName);
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, $"Error on GetSchemaForGame({AppId}, {LocalLang})", true, PluginDatabase.PluginName);
            }

            return Tuple.Create(AllAchievements, AllStats);
        }


        // TODO Use "profileurl" in "ISteamUser"
        // TODO Utility after updated GetAchievementsByWeb() 
        private string FindHiddenDescription(int AppId, string DisplayName, bool TryByName = false)
        {
            string url = string.Empty;
            string ResultWeb = string.Empty;
            bool noData = true;

            // Get data
            if (HtmlDocument == null)
            {
                if (!TryByName)
                {
                    Common.LogDebug(true, $"FindHiddenDescription() for {SteamId} - {AppId}");
                    url = string.Format(UrlProfilById, SteamId, AppId, LocalLang);
                    try
                    {
                        List<HttpCookie> cookies = GetCookies();
                        ResultWeb = Web.DownloadStringData(url, cookies).GetAwaiter().GetResult();
                    }
                    catch (WebException ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }
                else
                {
                    Common.LogDebug(true, $"FindHiddenDescription() for {SteamUser} - {AppId}");
                    url = string.Format(UrlProfilByName, SteamUser, AppId, LocalLang);
                    try
                    {
                        List<HttpCookie> cookies = GetCookies();
                        ResultWeb = Web.DownloadStringData(url, cookies).GetAwaiter().GetResult();
                    }
                    catch (WebException ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }

                if (!ResultWeb.IsNullOrEmpty())
                {
                    HtmlParser parser = new HtmlParser();
                    HtmlDocument = parser.Parse(ResultWeb);

                    if (HtmlDocument.QuerySelectorAll("div.achieveRow").Length != 0)
                    {
                        noData = false;
                    }
                }

                if (!TryByName && noData)
                {
                    HtmlDocument = null;
                    return FindHiddenDescription(AppId, DisplayName, TryByName = true);
                }
                else if (noData)
                {
                    return string.Empty;
                }
            }

            // Find the achievement description
            if (HtmlDocument != null)
            {
                foreach (var achieveRow in HtmlDocument.QuerySelectorAll("div.achieveRow"))
                {
                    try
                    {
                        if (achieveRow.QuerySelector("h3").InnerHtml.IsEqual(DisplayName))
                        {
                            string TempDescription = achieveRow.QuerySelector("h5").InnerHtml;

                            if (TempDescription.Contains("steamdb_achievement_spoiler"))
                            {
                                TempDescription = achieveRow.QuerySelector("h5 span").InnerHtml;
                                return WebUtility.HtmlDecode(TempDescription.Trim());
                            }
                            else
                            {
                                return WebUtility.HtmlDecode(TempDescription.Trim());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Get achievement earn percentage by other users.
        /// </summary>
        /// <remarks>
        /// If using SteamWithoutWebApi do nothing.
        /// If no SteamAPIKey, do nothing.
        /// Use SteamWebApi's ISteamUserStats to gather percentages.
        /// </remarks>
        /// <param name="AppId"></param>
        /// <param name="AllAchievements"></param>
        /// <returns></returns>
        public List<Achievements> GetGlobalAchievementPercentagesForAppByWebApi(int AppId, List<Achievements> AllAchievements)
        {
            if (PluginDatabase.PluginSettings.Settings.EnableSteamWithoutWebApi)
            {
                return AllAchievements;
            }

            if (SteamApiKey.IsNullOrEmpty())
            {
                logger.Warn($"No Steam API key");
                return AllAchievements;
            }

            try
            {
                using (dynamic steamWebAPI = WebAPI.GetInterface("ISteamUserStats", SteamApiKey))
                {
                    KeyValue GlobalAchievementPercentagesForApp = steamWebAPI.GetGlobalAchievementPercentagesForApp(gameid: AppId);
                    foreach (KeyValue AchievementPercentagesData in GlobalAchievementPercentagesForApp["achievements"]["achievement"].Children)
                    {
                        string ApiName = AchievementPercentagesData.Children.Find(x => x.Name == "name")?.Value;

                        // just fix the number crudely since we know it won't ever contain a digit group seperator, just a decimal ones
                        float.TryParse(AchievementPercentagesData.Children.Find(x => x.Name == "percent")?.Value?.Replace("%", string.Empty).Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out float Percent);

                        Common.LogDebug(true, $"{AppId} - ApiName: {ApiName} - Percent: {Percent}");

                        if (AllAchievements.Find(x => x.ApiName == ApiName) != null)
                        {
                            AllAchievements.Find(x => x.ApiName == ApiName).Percent = Percent;
                        }
                        else
                        {
                            logger.Warn($"not find for {AppId} - ApiName: {ApiName} - Percent: {Percent}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, $"Error on GetGlobalAchievementPercentagesForApp({SteamId}, {AppId}, {LocalLang})", true, PluginDatabase.PluginName);
            }

            return AllAchievements;
        }

        /// <summary>
        /// Get achievement earn percentage by other users.
        /// </summary>
        /// <remarks>
        /// Uses public HTML achivement page.
        /// <param name="AppId"></param>
        /// <param name="AllAchievements"></param>
        /// <returns></returns>
        private List<Achievements> GetGlobalAchievementPercentagesForAppByWebPage(int AppId, List<Achievements> AllAchievements)
        {
            string url = string.Empty;
            string ResultWeb = string.Empty;
            bool noData = true;
            HtmlDocument = null;

            // Get data
            if (HtmlDocument == null)
            {
                Common.LogDebug(true, $"GetGlobalAchievementPercentagesForAppByWeb() for {SteamId} - {AppId}");

                url = string.Format(UrlAchievements, AppId, LocalLang);
                try
                {
                    ResultWeb = HttpDownloader.DownloadString(url, Encoding.UTF8);
                }
                catch (WebException ex)
                {
                    Common.LogError(ex, false, true, PluginDatabase.PluginName);
                }

                if (!ResultWeb.IsNullOrEmpty())
                {
                    HtmlParser parser = new HtmlParser();
                    HtmlDocument = parser.Parse(ResultWeb);

                    if (HtmlDocument.QuerySelectorAll("div.achieveRow").Length != 0)
                    {
                        noData = false;
                    }
                }

                if (noData)
                {
                    return AllAchievements;
                }
            }


            // Find the achievement description
            if (HtmlDocument != null)
            {
                foreach (var achieveRow in HtmlDocument.QuerySelectorAll("div.achieveRow"))
                {
                    try
                    {
                        string Name = string.Empty;
                        if (achieveRow.QuerySelector("h3") != null)
                        {
                            Name = WebUtility.HtmlDecode(achieveRow.QuerySelector("h3").InnerHtml.Trim());
                        }

                        float Percent = 0;
                        if (achieveRow.QuerySelector(".achievePercent") != null)
                        {
                            // just fix the number crudely since we know it won't ever contain a digit group seperator, just a decimal ones
                            Percent = float.Parse(achieveRow.QuerySelector(".achievePercent").InnerHtml.Replace("%", string.Empty).Replace(",", "."), CultureInfo.InvariantCulture);
                        }

                        AllAchievements.Find(x => x.Name.IsEqual(Name)).Percent = Percent;
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }
            }

            return AllAchievements;
        }


        private List<Achievements> GetPlayerAchievementsByWebPage(int AppId)
        {
            List<Achievements> achievements = new List<Achievements>();
            string url = string.Empty;

            // Get data
            url = string.Format(UrlProfilById, SteamId, AppId, LocalLang);
            achievements = GetAchievementsByWeb(achievements, url);

            if (achievements.Count == 0)
            {
                url = string.Format(UrlProfilByName, SteamUser, AppId, LocalLang);
                achievements = GetAchievementsByWeb(achievements, url);
            }

            return achievements;
        }

        private List<Achievements> GetAchievementsByWeb(List<Achievements> Achievements, string Url, bool isRetry = false)
        {
            string ResultWeb = string.Empty;

            try
            {
                Url = Url + "&panorama=please";
                List<HttpCookie> cookies = GetCookies();
                ResultWeb = Web.DownloadStringData(Url, cookies, string.Empty, true).GetAwaiter().GetResult();
                //using (var WebViewOffscreen = API.Instance.WebViews.CreateOffscreenView())
                //{
                //    Url = Url + "&panorama=please";
                //    WebViewOffscreen.NavigateAndWait(Url);
                //    ResultWeb = WebViewOffscreen.GetPageSource();
                //
                //    string CurrentUrl = WebViewOffscreen.GetCurrentAddress();
                //    if (CurrentUrl != Url)
                //    {
                //        var urlParams = Url.Split('?').ToList();
                //        if (urlParams.Count == 2)
                //        {
                //            Url = CurrentUrl + "?" + urlParams[1];
                //        }
                //
                //        WebViewOffscreen.NavigateAndWait(Url);
                //        ResultWeb = WebViewOffscreen.GetPageSource();
                //    }
                //}

                if (ResultWeb.IndexOf("var g_rgAchievements = ") > -1)
                {
                    int index = ResultWeb.IndexOf("var g_rgAchievements = ");
                    ResultWeb = ResultWeb.Substring(index + "var g_rgAchievements = ".Length);

                    index = ResultWeb.IndexOf("var g_rgLeaderboards");
                    ResultWeb = ResultWeb.Substring(0, index).Trim();

                    ResultWeb = ResultWeb.Substring(0, ResultWeb.Length - 1).Trim();

                    dynamic dataByWeb = Serialization.FromJson<dynamic>(ResultWeb);

                    dynamic OpenData = dataByWeb["open"];
                    foreach (dynamic dd in OpenData)
                    {
                        string stringData = Serialization.ToJson(dd.Value);
                        SteamAchievementData steamAchievementData = Serialization.FromJson<SteamAchievementData>(stringData);
                        Achievements.Add(new Achievements
                        {
                            Name = WebUtility.HtmlDecode(steamAchievementData.Name.Trim()),
                            ApiName = steamAchievementData.RawName.Trim(),
                            Description = WebUtility.HtmlDecode(steamAchievementData.Desc.Trim()),
                            UrlUnlocked = steamAchievementData.IconClosed.Trim(),
                            UrlLocked = steamAchievementData.IconClosed.Trim(),
                            DateUnlocked = default(DateTime),
                            IsHidden = steamAchievementData.Hidden,
                            Percent = 100
                        });
                    }

                    dynamic ClosedData = dataByWeb["closed"];
                    foreach (dynamic dd in ClosedData)
                    {
                        string stringData = Serialization.ToJson(dd.Value);
                        SteamAchievementData steamAchievementData = Serialization.FromJson<SteamAchievementData>(stringData);
                        Achievements.Add(new Achievements
                        {
                            Name = WebUtility.HtmlDecode(steamAchievementData.Name.Trim()),
                            ApiName = steamAchievementData.RawName.Trim(),
                            Description = WebUtility.HtmlDecode(steamAchievementData.Desc.Trim()),
                            UrlUnlocked = steamAchievementData.IconClosed.Trim(),
                            UrlLocked = steamAchievementData.IconClosed.Trim(),
                            DateUnlocked = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(steamAchievementData.UnlockTime).ToLocalTime(),
                            IsHidden = steamAchievementData.Hidden,
                            Percent = 100
                        });
                    }
                }
                //else if (ResultWeb.IndexOf("achieveRow") > -1)
                //{
                //    Url = Url.Replace("&panorama=please", string.Empty).Replace($"l={LocalLang}", "l=english");
                //    ResultWeb = Web.DownloadStringData(Url, GetCookies(), string.Empty, true).GetAwaiter().GetResult();
                //    IHtmlDocument htmlDocument = new HtmlParser().Parse(ResultWeb);
                //    var achieveRow_English = htmlDocument.QuerySelectorAll(".achieveRow");
                //
                //    htmlDocument = new HtmlParser().Parse(ResultWeb);
                //    int idx = 0;
                //    foreach(var el in htmlDocument.QuerySelectorAll(".achieveRow"))
                //    {
                //        string UrlUnlocked = el.QuerySelector(".achieveImgHolder img")?.GetAttribute("src") ?? string.Empty;
                //        string Name = el.QuerySelector(".achieveTxtHolder h3").GetAttribute("src");
                //        string Description = el.QuerySelector(".achieveTxtHolder h5").GetAttribute("src");
                //
                //        DateTime DateUnlocked = default(DateTime);
                //        string stringDateUnlocked = achieveRow_English[idx].QuerySelector(".achieveUnlockTime")?.InnerHtml ?? string.Empty;
                //        if (!stringDateUnlocked.IsNullOrEmpty())
                //        {
                //            stringDateUnlocked = stringDateUnlocked.Replace("Unlocked", string.Empty).Trim();
                //            DateTime.TryParseExact(stringDateUnlocked, "dd MMM, yyyy @ h:mmtt", new CultureInfo("en-US"), DateTimeStyles.None, out DateUnlocked);
                //        }
                //
                //        Achievements.Add(new Achievements
                //        {
                //            Name = WebUtility.HtmlDecode(Name),
                //            ApiName = string.Empty,
                //            Description = WebUtility.HtmlDecode(Description),
                //            UrlUnlocked = UrlUnlocked,
                //            UrlLocked = string.Empty,
                //            DateUnlocked = DateUnlocked,
                //            IsHidden = false,
                //            Percent = 100
                //        });
                //
                //        idx++;
                //    }
                //}
                else
                {
                    Common.LogDebug(true, $"No achievement data on {Url}");
                    if (!isRetry)
                    {
                        return GetProgressionByWeb(Achievements, Url, true);
                    }
                }
            }
            catch (WebException ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return Achievements;
        }

        private List<Achievements> GetProgressionByWeb(List<Achievements> Achievements, string Url, bool isRetry = false)
        {
            string ResultWeb = string.Empty;
            try
            {
                Url = Url + "&panorama=please";
                List<HttpCookie> cookies = GetCookies();
                ResultWeb = Web.DownloadStringData(Url, cookies, string.Empty, true).GetAwaiter().GetResult();
                //using (var WebViewOffscreen = API.Instance.WebViews.CreateOffscreenView())
                //{
                //    Url = Url + "&panorama=please";
                //    WebViewOffscreen.NavigateAndWait(Url);
                //    ResultWeb = WebViewOffscreen.GetPageSource();
                //
                //    string CurrentUrl = WebViewOffscreen.GetCurrentAddress();
                //    if (CurrentUrl != Url)
                //    {
                //        var urlParams = Url.Split('?').ToList();
                //        if (urlParams.Count == 2)
                //        {
                //            Url = CurrentUrl + "?" + urlParams[1];
                //        }
                //
                //        WebViewOffscreen.NavigateAndWait(Url);
                //        ResultWeb = WebViewOffscreen.GetPageSource();
                //    }
                //}

                int index = ResultWeb.IndexOf("var g_rgAchievements = ");
                if (index > -1)
                {
                    ResultWeb = ResultWeb.Substring(index + "var g_rgAchievements = ".Length);

                    index = ResultWeb.IndexOf("var g_rgLeaderboards");
                    ResultWeb = ResultWeb.Substring(0, index).Trim();

                    ResultWeb = ResultWeb.Substring(0, ResultWeb.Length - 1).Trim();

                    dynamic dataByWeb = Serialization.FromJson<dynamic>(ResultWeb);
                    if (dataByWeb == null)
                    {
                        logger.Warn($"No g_rgAchievements data");
                        return Achievements;
                    }

                    dynamic OpenData = dataByWeb["open"];
                    foreach (dynamic dd in OpenData)
                    {
                        string stringData = Serialization.ToJson(dd.Value);
                        SteamAchievementData steamAchievementData = Serialization.FromJson<SteamAchievementData>(stringData);

                        if (!(steamAchievementData.Progress is string))
                        {
                            double.TryParse(steamAchievementData.Progress["min_val"].ToString(), out double min);
                            double.TryParse(steamAchievementData.Progress["max_val"].ToString(), out double max);
                            double.TryParse(steamAchievementData.Progress["currentVal"].ToString(), out double val);

                            var finded = Achievements.Find(x => x.ApiName.IsEqual(steamAchievementData.RawName));
                            if (finded != null)
                            {
                                finded.Progression = new AchProgression
                                {
                                    Min = min,
                                    Max = max,
                                    Value = val,
                                };
                            }
                        }
                    }

                    dynamic ClosedData = dataByWeb["closed"];
                    foreach (dynamic dd in ClosedData)
                    {
                        string stringData = Serialization.ToJson(dd.Value);
                        SteamAchievementData steamAchievementData = Serialization.FromJson<SteamAchievementData>(stringData);

                        if (!(steamAchievementData.Progress is string))
                        {
                            double.TryParse(steamAchievementData.Progress["min_val"].ToString(), out double min);
                            double.TryParse(steamAchievementData.Progress["max_val"].ToString(), out double max);
                            double.TryParse(steamAchievementData.Progress["currentVal"].ToString(), out double val);

                            var finded = Achievements.Find(x => x.ApiName.IsEqual(steamAchievementData.RawName));
                            if (finded != null)
                            {
                                finded.Progression = new AchProgression
                                {
                                    Min = min,
                                    Max = max,
                                    Value = val,
                                };
                            }
                        }
                    }
                }
                else
                {
                    Common.LogDebug(true, $"No achievement data on {Url}");
                    if (!isRetry)
                    {
                        return GetProgressionByWeb(Achievements, Url, true);
                    }
                }
            }
            catch (WebException ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return Achievements;
        }

        #endregion


        #region Errors
        public virtual void ShowNotificationPluginNoPublic(string Message)
        {
            logger.Warn($"{ClientName} user is not public");

            PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-nopublic",
                $"{PluginDatabase.PluginName}\r\n{Message}",
                NotificationType.Error,
                () => 
                {
                    ResetCachedConfigurationValidationResult();
                    ResetCachedIsConnectedResult();
                    PluginDatabase.Plugin.OpenSettingsView();
                }
            ));
        }
        #endregion




        public GameAchievements RefreshRarity(string sourceName, GameAchievements gameAchievements)
        {
            if (sourceName == "steam")
            {
                int.TryParse(Regex.Match(gameAchievements.SourcesLink.Url, @"\d+").Value, out int AppId);
                if (AppId != 0)
                {
                    if (IsConfigured())
                    {
                        gameAchievements.Items = GetGlobalAchievementPercentagesForAppByWebApi(AppId, gameAchievements.Items);
                    }
                    else
                    {
                        logger.Warn($"No Steam config");
                    }
                }
            }
            return gameAchievements;
        }
    }
}
