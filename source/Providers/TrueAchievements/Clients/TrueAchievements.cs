﻿using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using CommonPluginsShared;
using CommonPluginsShared.Extensions;
using Playnite.SDK;
using Playnite.SDK.Models;
using SuccessStory.Models;
using SuccessStory.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using static CommonPluginsShared.PlayniteTools;
using static SuccessStory.Services.SuccessStoryDatabase;

namespace SuccessStory.Clients
{
    class TrueAchievementsFactory : IAchievementFactory
    {
        public void BuildClient(Dictionary<string, GenericAchievements> Providers, Dictionary<string, ISearchableManualAchievements> ManualSearchProviders, Dictionary<string, IMetadataAugmentAchievements> AchievementMetadataAugmenters)
        {
            TrueAchievements tmp = new TrueAchievements();
            Providers[AchievementSource.True] = tmp;
            AchievementMetadataAugmenters[AchievementSource.True] = tmp;
        }
    }
    class TrueAchievements : GenericAchievements, IMetadataAugmentAchievements
    {
        public bool RefreshRarity(GameAchievements gameAchievements)
        {
            return false;
        }

        private bool SetEstimateTimeToUnlock(Game game, GameAchievements gameAchievements)
        {
            DateTime? lastUpdateS = gameAchievements.GetExtraHandlerDate("TrueSteam", "Time");
            DateTime? lastUpdateX = gameAchievements.GetExtraHandlerDate("TrueXbox", "Time");
            bool providerMatched = lastUpdateS.HasValue || lastUpdateX.HasValue;

            if ((lastUpdateS ?? lastUpdateX ?? DateTime.MinValue) < DateTime.UtcNow.AddDays(-1))
            {
                EstimateTimeToUnlock EstimateTimeSteam = new EstimateTimeToUnlock();
                EstimateTimeToUnlock EstimateTimeXbox = new EstimateTimeToUnlock();

                string IdSteam = null;
                string IdXbox = null;

                List<TrueAchievementSearch> ListGames = TrueAchievements.SearchGame(game, OriginData.Steam);
                if (ListGames.Count > 0)
                {
                    EstimateTimeSteam = TrueAchievements.GetEstimateTimeToUnlock(ListGames[0].GameUrl);
                    IdSteam = ListGames[0].GameUrl
                                          .Replace("https://truesteamachievements.com/game/", string.Empty)
                                          .Replace("/achievements", string.Empty);
                }

                ListGames = TrueAchievements.SearchGame(game, OriginData.Xbox);
                if (ListGames.Count > 0)
                {
                    EstimateTimeXbox = TrueAchievements.GetEstimateTimeToUnlock(ListGames[0].GameUrl);
                    IdXbox = ListGames[0].GameUrl
                                         .Replace("https://www.trueachievements.com/game/", string.Empty)
                                         .Replace("/achievements", string.Empty);
                }

                if (EstimateTimeSteam.DataCount >= EstimateTimeXbox.DataCount)
                {
                    Common.LogDebug(true, $"Get EstimateTimeSteam for {game.Name}");
                    gameAchievements.EstimateTime = EstimateTimeSteam;
                    providerMatched = true;
                    gameAchievements.SetExtraHandlerDate("TrueSteam", IdSteam, "Time");
                }
                else
                {
                    Common.LogDebug(true, $"Get EstimateTimeXbox for {game.Name}");
                    gameAchievements.EstimateTime = EstimateTimeXbox;
                    providerMatched = true;
                    gameAchievements.SetExtraHandlerDate("TrueXbox", IdXbox, "Time");
                }
            }

            return providerMatched;
        }
        public override GameAchievements GetAchievements(Game game)
        {
            throw new NotImplementedException();
        }

        public override bool ValidateConfiguration()
        {
            // The authentification is only for localised achievement
            return true;
        }
        public TrueAchievements() : base("True")
        {

        }



        internal static new readonly ILogger logger = LogManager.GetLogger();

        private static SuccessStoryDatabase PluginDatabase = SuccessStory.PluginDatabase;

        public static string XboxUrlSearch = @"https://www.trueachievements.com/searchresults.aspx?search={0}";
        public static string SteamUrlSearch = @"https://truesteamachievements.com/searchresults.aspx?search={0}";

        public enum OriginData
        {
            Steam, Xbox
        }


        /// <summary>
        /// Search list game on truesteamachievements or trueachievements.
        /// </summary>
        /// <param name="game"></param>
        /// <param name="originData"></param>
        /// <returns></returns>
        public static List<TrueAchievementSearch> SearchGame(Game game, OriginData originData)
        {
            List<TrueAchievementSearch> ListSearchGames = new List<TrueAchievementSearch>();
            string Url;
            string UrlBase;
            if (originData == OriginData.Steam)
            {
                //TODO: Decide if editions should be removed here
                Url = string.Format(SteamUrlSearch, WebUtility.UrlEncode(PlayniteTools.NormalizeGameName(game.Name, true)));
                UrlBase = @"https://truesteamachievements.com";
            }
            else
            {
                //TODO: Decide if editions should be removed here
                Url = string.Format(XboxUrlSearch, WebUtility.UrlEncode(PlayniteTools.NormalizeGameName(game.Name, true)));
                UrlBase = @"https://www.trueachievements.com";
            }


            try
            {
                string WebData = string.Empty;
                using (var WebViewOffscreen = PluginDatabase.PlayniteApi.WebViews.CreateOffscreenView())
                {
                    WebViewOffscreen.NavigateAndWait(Url);
                    WebData = WebViewOffscreen.GetPageSource();
                }

                if (WebData.IsNullOrEmpty())
                {
                    logger.Warn($"No data from {Url}");
                    return ListSearchGames;
                }

                HtmlParser parser = new HtmlParser();
                IHtmlDocument htmlDocument = parser.Parse(WebData);

                if (WebData.IndexOf("There are no matching search results, please change your search terms") > -1)
                {
                    return ListSearchGames;
                }

                var SectionGames = htmlDocument.QuerySelector("#oSearchResults");

                if (SectionGames == null)
                {
                    string GameUrl = htmlDocument.QuerySelector("link[rel=\"canonical\"]")?.GetAttribute("href");
                    string GameImage = htmlDocument.QuerySelector("div.info img")?.GetAttribute("src");

                    ListSearchGames.Add(new TrueAchievementSearch
                    {
                        GameUrl = GameUrl,
                        GameName = game.Name,
                        GameImage = GameImage
                    });
                }
                else
                {
                    foreach (var SearchGame in SectionGames.QuerySelectorAll("tr"))
                    {
                        try
                        {
                            var GameInfos = SearchGame.QuerySelectorAll("td");
                            if (GameInfos.Count() > 2)
                            {
                                string GameUrl = UrlBase + GameInfos[0].QuerySelector("a")?.GetAttribute("href");
                                string GameName = GameInfos[1].QuerySelector("a")?.InnerHtml;
                                string GameImage = UrlBase + GameInfos[0].QuerySelector("a img")?.GetAttribute("src");

                                string ItemType = GameInfos[2].InnerHtml;

                                if (ItemType.IsEqual("game"))
                                {
                                    ListSearchGames.Add(new TrueAchievementSearch
                                    {
                                        GameUrl = GameUrl,
                                        GameName = GameName,
                                        GameImage = GameImage
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Common.LogError(ex, false, true, PluginDatabase.PluginName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return ListSearchGames;
        }


        /// <summary>
        /// Get the estimate time from game url on truesteamachievements or trueachievements.
        /// </summary>
        /// <param name="UrlTrueAchievement"></param>
        /// <returns></returns>
        public static EstimateTimeToUnlock GetEstimateTimeToUnlock(string UrlTrueAchievement)
        {
            EstimateTimeToUnlock EstimateTimeToUnlock = new EstimateTimeToUnlock();

            if (UrlTrueAchievement.IsNullOrEmpty())
            {
                logger.Warn($"No url for GetEstimateTimeToUnlock()");
                return EstimateTimeToUnlock;
            }

            try
            {
                string WebData = string.Empty;
                using (var WebViewOffscreen = PluginDatabase.PlayniteApi.WebViews.CreateOffscreenView())
                {
                    WebViewOffscreen.NavigateAndWait(UrlTrueAchievement);
                    WebData = WebViewOffscreen.GetPageSource();
                }

                if (WebData.IsNullOrEmpty())
                {
                    logger.Warn($"No data from {UrlTrueAchievement}");
                    return EstimateTimeToUnlock;
                }


                HtmlParser parser = new HtmlParser();
                IHtmlDocument htmlDocument = parser.Parse(WebData);

                int NumberDataCount = 0;
                foreach (var SearchElement in htmlDocument.QuerySelectorAll("div.game div.l1 div"))
                {
                    var Title = SearchElement.GetAttribute("title");

                    if (Title != null && (Title == "Maximum TrueAchievement" || Title == "Maximum TrueSteamAchievement"))
                    {
                        var data = SearchElement.InnerHtml;
                        int.TryParse(Regex.Replace(data, "[^0-9]", ""), out NumberDataCount);
                        break;
                    }
                }

                foreach (var SearchElement in htmlDocument.QuerySelectorAll("div.game div.l2 a"))
                {
                    var Title = SearchElement.GetAttribute("title");

                    if (Title != null && Title == "Estimated time to unlock all achievements")
                    {
                        string EstimateTime = SearchElement.InnerHtml
                            .Replace("<i class=\"fa fa-hourglass-end\"></i>", string.Empty)
                            .Replace("<i class=\"fa fa-clock-o\"></i>", string.Empty)
                            .Trim();

                        int EstimateTimeMin = 0;
                        int EstimateTimeMax = 0;
                        int index = 0;
                        foreach (var item in EstimateTime.Replace("h", string.Empty).Split('-'))
                        {
                            if (index == 0)
                            {
                                int.TryParse(item.Replace("+", string.Empty), out EstimateTimeMin);
                            }
                            else
                            {
                                int.TryParse(item, out EstimateTimeMax);
                            }

                            index++;
                        }

                        EstimateTimeToUnlock = new EstimateTimeToUnlock
                        {
                            DataCount = NumberDataCount,
                            EstimateTime = EstimateTime,
                            EstimateTimeMin = EstimateTimeMin,
                            EstimateTimeMax = EstimateTimeMax
                        };
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return EstimateTimeToUnlock;
        }

        public int GetAugmenterRank(string augmenter)
        {
            switch (augmenter)
            {
                case "Time":
                    return 2;
            }
            return 0;
        }
        public string[] GetAugmentTypes() => new string[] { "Time" };
        public string[] GetAugmentTypesManual() => GetAugmentTypes();

        public bool RefreshAugmenterMetadata(string augmenter, GameAchievements gameAchievements)
        {
            switch (augmenter)
            {
                case "Time":
                    {
                        Game game = PluginDatabase.PlayniteApi.Database.Games.Get(gameAchievements.Id);
                        return SetEstimateTimeToUnlock(game, gameAchievements);
                    }
            }
            return false;
        }
    }


    public class TrueAchievementSearch
    {
        public string GameUrl { get; set; }
        public string GameName { get; set; }
        public string GameImage { get; set; }
    }

    public class EstimateTimeToUnlock
    {
        public int DataCount { get; set; }
        public string EstimateTime { get; set; }
        public int EstimateTimeMin { get; set; }
        public int EstimateTimeMax { get; set; }
    }
}
