﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using CommonPluginsShared;
using CommonPlayniteShared.PluginLibrary.XboxLibrary.Models;
using SuccessStory.Models;
using CommonPluginsShared.Models;
using CommonPlayniteShared.PluginLibrary.XboxLibrary;
using CommonPluginsShared.Extensions;
using static CommonPluginsShared.PlayniteTools;
using static SuccessStory.Services.SuccessStoryDatabase;

namespace SuccessStory.Clients
{
    class XboxAchievementsFactory : IAchievementFactory
    {
        public void BuildClient(Dictionary<AchievementSource, GenericAchievements> Providers, Dictionary<AchievementSource, ISearchableManualAchievements> ManualSearchProviders)
        {
            Providers[AchievementSource.Xbox] = new XboxAchievements();
        }
    }
    class XboxAchievements : GenericAchievements
    {
        public override int CheckAchivementSourceRank(ExternalPlugin pluginType, SuccessStorySettings settings, Game game, bool ignoreSpecial = false)
        {
            if (pluginType == ExternalPlugin.None)
            {
                if (game.Source?.Name?.Contains("Xbox Game Pass", StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    return 100;
                }
                if (game.Source?.Name?.Contains("Microsoft Store", StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    return 100;
                }

                return 0;
            }

            if (pluginType == ExternalPlugin.XboxLibrary && settings.EnableXbox)
            {
                return 100;
            }

            return 0;
        }






        protected static XboxAccountClient _XboxAccountClient;
        internal static XboxAccountClient XboxAccountClient
        {
            get
            {
                if (_XboxAccountClient == null)
                {
                    _XboxAccountClient = new XboxAccountClient(
                        PluginDatabase.PlayniteApi, 
                        PluginDatabase.Paths.PluginUserDataPath + "\\..\\7e4fbb5e-2ae3-48d4-8ba0-6b30e7a4e287"
                    );
                }
                return _XboxAccountClient;
            }

            set
            {
                _XboxAccountClient = value;
            }
        }

        private static readonly string AchievementsBaseUrl = @"https://achievements.xboxlive.com/users/xuid({0})/achievements";
        private static readonly string TitleAchievementsBaseUrl = @"https://achievements.xboxlive.com/users/xuid({0})/titleachievements";


        public XboxAchievements() : base("Xbox", CodeLang.GetXboxLang(PluginDatabase.PlayniteApi.ApplicationSettings.Language))
        {
            TemporarySource = AchievementSource.Xbox;
        }


        public override GameAchievements GetAchievements(Game game)
        {
            GameAchievements gameAchievements = SuccessStory.PluginDatabase.GetDefault(game);
            List<Achievements> AllAchievements = new List<Achievements>();

            if (IsConnected())
            {
                try
                {
                    var authData = XboxAccountClient.GetSavedXstsTokens();
                    if (authData == null)
                    {
                        ShowNotificationPluginNoAuthenticate(resources.GetString("LOCSuccessStoryNotificationsXboxNotAuthenticate"), ExternalPlugin.XboxLibrary);
                        return gameAchievements;
                    }

                    AllAchievements = GetXboxAchievements(game, authData).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    ShowNotificationPluginError(ex);
                }
            }
            else
            {
                ShowNotificationPluginNoAuthenticate(resources.GetString("LOCSuccessStoryNotificationsXboxNotAuthenticate"), ExternalPlugin.XboxLibrary);
            }


            gameAchievements.Items = AllAchievements;


            // Set source link
            if (gameAchievements.HasAchievements)
            {
                gameAchievements.SourcesLink = new SourceLink
                {
                    GameName = game.Name,
                    Name = "Xbox",
                    Url = $"https://account.xbox.com/en-US/GameInfoHub?titleid={GetTitleId(game)}&selectedTab=achievementsTab&activetab=main:mainTab2"
                };
            }

            // Set rarity from Exophase
            if (gameAchievements.HasAchievements)
            {
                ExophaseAchievements exophaseAchievements = new ExophaseAchievements();
                exophaseAchievements.SetRarety(gameAchievements, AchievementSource.Xbox);
            }


            return gameAchievements;
        }


        #region Configuration
        public override bool ValidateConfiguration()
        {
            if (PlayniteTools.IsDisabledPlaynitePlugins("XboxLibrary"))
            {
                ShowNotificationPluginDisable(resources.GetString("LOCSuccessStoryNotificationsXboxDisabled"));
                return false;
            }
            else
            {
                if (CachedConfigurationValidationResult == null)
                {
                    CachedConfigurationValidationResult = IsConnected();

                    if (!(bool)CachedConfigurationValidationResult)
                    {
                        ShowNotificationPluginNoAuthenticate(resources.GetString("LOCSuccessStoryNotificationsXboxNotAuthenticate"), ExternalPlugin.XboxLibrary);
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
                CachedIsConnectedResult = XboxAccountClient.GetIsUserLoggedIn().GetAwaiter().GetResult();
                if (!(bool)CachedIsConnectedResult && File.Exists(XboxAccountClient.liveTokensPath))
                {
                    XboxAccountClient.RefreshTokens().GetAwaiter().GetResult();
                    CachedIsConnectedResult = XboxAccountClient.GetIsUserLoggedIn().GetAwaiter().GetResult();
                }
            }

            return (bool)CachedIsConnectedResult;
        }

        public override bool EnabledInSettings()
        {
            return PluginDatabase.PluginSettings.Settings.EnableXbox;
        }
        #endregion


        
        #region Xbox
        private string GetTitleId(Game game)
        {
            string titleId = string.Empty;
            if (game.GameId?.StartsWith("CONSOLE_") == true)
            {
                var consoleGameIdParts = game.GameId.Split('_');
                titleId = consoleGameIdParts[1];

                Common.LogDebug(true, $"{ClientName} - name: {game.Name} - gameId: {game.GameId} - titleId: {titleId}");
            }
            else if (!game.GameId.IsNullOrEmpty())
            {
                var libTitle = XboxAccountClient.GetTitleInfo(game.GameId).Result;
                titleId = libTitle.titleId;

                Common.LogDebug(true, $"{ClientName} - name: {game.Name} - gameId: {game.GameId} - titleId: {titleId}");
            }
            return titleId;
        }

        private async Task<TContent> GetSerializedContentFromUrl<TContent>(string url, AuthorizationData authData, string contractVersion) where TContent : class
        {
            Common.LogDebug(true, $"{ClientName} - url: {url}");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", Web.UserAgent);
                SetAuthenticationHeaders(client.DefaultRequestHeaders, authData, contractVersion);

                var response = client.GetAsync(url).Result;
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        logger.Warn($"{ClientName} - User is not authenticated - {response.StatusCode}");
                        PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                            $"{PluginDatabase.PluginName}-Xbox-notAuthenticate",
                            $"{PluginDatabase.PluginName}\r\n{resources.GetString("LOCSuccessStoryNotificationsXboxNotAuthenticate")}",
                            NotificationType.Error
                        ));
                    }
                    else
                    {
                        logger.Warn($"{ClientName} - Error on GetXboxAchievements() - {response.StatusCode}");
                        PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                            $"{PluginDatabase.PluginName}-Xbox-webError",
                            $"{PluginDatabase.PluginName}\r\nXbox achievements: {resources.GetString("LOCImportError")}",
                            NotificationType.Error
                        ));
                    }

                    return null;
                }

                string cont = await response.Content.ReadAsStringAsync();
                Common.LogDebug(true, cont);

                return Serialization.FromJson<TContent>(cont);
            }
        }


        private async Task<List<Achievements>> GetXboxAchievements(Game game, AuthorizationData authorizationData)
        {
            var getAchievementMethods = new List<Func<Game, AuthorizationData, Task<List<Achievements>>>>
            {
                GetXboxOneAchievements,
                GetXbox360Achievements
            };

            if (game.Platforms.Any(p => p.SpecificationId == "xbox360"))
            {
                getAchievementMethods.Reverse();
            }

            foreach (var getAchievementsMethod in getAchievementMethods)
            {
                try
                { 
                    var result = await getAchievementsMethod.Invoke(game, authorizationData);
                    if (result != null && result.Any())
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("User is not authenticated", StringComparison.InvariantCultureIgnoreCase))
                    {
                        ShowNotificationPluginNoAuthenticate(resources.GetString("LOCSuccessStoryNotificationsXboxNotAuthenticate"), ExternalPlugin.XboxLibrary);
                    }
                    else
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets achievements for games that have come out on or since the Xbox One. This includes recent PC releases and Xbox Series X/S games.
        /// </summary>
        /// <param name="game"></param>
        /// <param name="authorizationData"></param>
        /// <returns></returns>
        private async Task<List<Achievements>> GetXboxOneAchievements(Game game, AuthorizationData authorizationData)
        {
            if (authorizationData is null)
                throw new ArgumentNullException(nameof(authorizationData));

            string xuid = authorizationData.DisplayClaims.xui[0].xid;

            Common.LogDebug(true, $"GetXboxAchievements() - name: {game.Name} - gameId: {game.GameId}");
            
            string titleId = GetTitleId(game);

            string url = string.Format(AchievementsBaseUrl, xuid) + $"?titleId={titleId}&maxItems=1000";
            if (titleId.IsNullOrEmpty())
            {
                url = string.Format(AchievementsBaseUrl, xuid) + "?maxItems=10000";
                logger.Warn($"{ClientName} - Bad request");
            }

            var response = await GetSerializedContentFromUrl<XboxOneAchievementResponse>(url, authorizationData, "2");

            List<XboxOneAchievement> relevantAchievements;
            if (titleId.IsNullOrEmpty())
            {
                relevantAchievements = response.achievements.Where(x => x.titleAssociations.First().name.IsEqual(game.Name, true)).ToList();
                Common.LogDebug(true, $"Not find with {game.GameId} for {game.Name} - {relevantAchievements.Count}");
            }
            else
            {
                relevantAchievements = response.achievements;
                Common.LogDebug(true, $"Find with {titleId} & {game.GameId} for {game.Name} - {relevantAchievements.Count}");
            }
            var achievements = relevantAchievements.Select(ConvertToAchievement).ToList();

            return achievements;
        }

        /// <summary>
        /// Gets achievements for Xbox 360 and Games for Windows Live
        /// </summary>
        /// <param name="game"></param>
        /// <param name="authorizationData"></param>
        /// <returns></returns>
        private async Task<List<Achievements>> GetXbox360Achievements(Game game, AuthorizationData authorizationData)
        {
            if (authorizationData is null)
                throw new ArgumentNullException(nameof(authorizationData));

            string xuid = authorizationData.DisplayClaims.xui[0].xid;

            Common.LogDebug(true, $"GetXbox360Achievements() - name: {game.Name} - gameId: {game.GameId}");

            string titleId = GetTitleId(game);

            if (titleId.IsNullOrEmpty())
            {
                Common.LogDebug(true, $"Couldn't find title ID for game name: {game.Name} - gameId: {game.GameId}");
                return new List<Achievements>();
            }

            // gets the player-unlocked achievements
            string unlockedAchievementsUrl = string.Format(AchievementsBaseUrl, xuid) + $"?titleId={titleId}&maxItems=1000";
            var getUnlockedAchievementsTask = GetSerializedContentFromUrl<Xbox360AchievementResponse>(unlockedAchievementsUrl, authorizationData, "1");

            // gets all of the game's achievements, but they're all marked as locked
            string allAchievementsUrl = string.Format(TitleAchievementsBaseUrl, xuid) + $"?titleId={titleId}&maxItems=1000";
            var getAllAchievementsTask = GetSerializedContentFromUrl<Xbox360AchievementResponse>(allAchievementsUrl, authorizationData, "1");

            await Task.WhenAll(getUnlockedAchievementsTask, getAllAchievementsTask);

            Dictionary<int, Xbox360Achievement> mergedAchievements = getUnlockedAchievementsTask.Result.achievements.ToDictionary(x => x.id);
            foreach (Xbox360Achievement a in getAllAchievementsTask.Result.achievements)
            {
                if (mergedAchievements.ContainsKey(a.id))
                    continue;

                mergedAchievements.Add(a.id, a);
            }

            var achievements = mergedAchievements.Values.Select(ConvertToAchievement).ToList();

            return achievements;
        }


        private static Achievements ConvertToAchievement(XboxOneAchievement xboxAchievement)
        {
            return new Achievements
            {
                ApiName = string.Empty,
                Name = xboxAchievement.name,
                Description = (xboxAchievement.progression.timeUnlocked == default(DateTime)) ? xboxAchievement.lockedDescription : xboxAchievement.description,
                IsHidden = xboxAchievement.isSecret,
                Percent = 100,
                DateUnlocked = xboxAchievement.progression.timeUnlocked,
                UrlLocked = string.Empty,
                UrlUnlocked = xboxAchievement.mediaAssets[0].url,
            };
        }

        private static Achievements ConvertToAchievement(Xbox360Achievement xboxAchievement)
        {
            bool unlocked = xboxAchievement.unlocked || xboxAchievement.unlockedOnline;

            return new Achievements
            {
                ApiName = string.Empty,
                Name = xboxAchievement.name,
                Description = unlocked ? xboxAchievement.lockedDescription : xboxAchievement.description,
                IsHidden = xboxAchievement.isSecret,
                Percent = 100,
                DateUnlocked = unlocked ? xboxAchievement.timeUnlocked : default(DateTime),
                UrlLocked = string.Empty,
                UrlUnlocked = $"https://image-ssl.xboxlive.com/global/t.{xboxAchievement.titleId:x}/ach/0/{xboxAchievement.imageId:x}",
                
            };
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="headers"></param>
        /// <param name="auth"></param>
        /// <param name="contractVersion">1 for Xbox 360 era API, 2 for Xbox One or Series X/S</param>
        private void SetAuthenticationHeaders(System.Net.Http.Headers.HttpRequestHeaders headers, AuthorizationData auth, string contractVersion)
        {
            headers.Add("x-xbl-contract-version", contractVersion);
            headers.Add("Authorization", $"XBL3.0 x={auth.DisplayClaims.xui[0].uhs};{auth.Token}");
            headers.Add("Accept-Language", LocalLang);
        }
        #endregion
    }


    public class PagingInfo
    {
        public string continuationToken { get; set; }
        public int totalRecords { get; set; }
    }


    #region Xbox One models
    public class XboxOneAchievement
    {
        public string id { get; set; }
        public string serviceConfigId { get; set; }
        public string name { get; set; }
        public List<TitleAssociations> titleAssociations { get; set; }
        public string progressState { get; set; }
        public Progression progression { get; set; }
        public List<MediaAssets> mediaAssets { get; set; }
        public List<string> platforms { get; set; }
        public bool isSecret { get; set; }
        public string description { get; set; }
        public string lockedDescription { get; set; }
        public string productId { get; set; }
        public string achievementType { get; set; }
        public string participationType { get; set; }
    }

    public class TitleAssociations
    {
        public string name { get; set; }
        public int id { get; set; }
    }

    public class Progression
    {
        public List<Requirements> requirements { get; set; }
        public DateTime timeUnlocked { get; set; }
    }

    public class Requirements
    {
        public string id { get; set; }
        public string current { get; set; }
        public string target { get; set; }
        public string operationType { get; set; }
        public string valueType { get; set; }
        public string ruleParticipationType { get; set; }
    }

    public class MediaAssets
    {
        public string name { get; set; }
        public string type { get; set; }
        public string url { get; set; }
    }

    public class XboxOneAchievementResponse
    {
        public List<XboxOneAchievement> achievements { get; set; }
        public PagingInfo pagingInfo { get; set; }
    }
    #endregion Xbox One models

    #region Xbox 360 models
    public class Xbox360Achievement
    {
        public int id { get; set; }
        public int titleId { get; set; }
        public string name { get; set; }
        public long sequence { get; set; }
        public int flags { get; set; }
        public bool unlockedOnline { get; set; }
        public bool unlocked { get; set; }
        public bool isSecret { get; set; }
        public int platform { get; set; }
        public int gamerscore { get; set; }
        public int imageId { get; set; }
        public string description { get; set; }
        public string lockedDescription { get; set; }
        public int type { get; set; }
        public bool isRevoked { get; set; }
        public DateTime timeUnlocked { get; set; }
    }

    public class Xbox360AchievementResponse
    {
        public List<Xbox360Achievement> achievements { get; set; }
        public PagingInfo pagingInfo { get; set; }
        public DateTime version { get; set; }
    }
    #endregion Xbox 360 models
}
