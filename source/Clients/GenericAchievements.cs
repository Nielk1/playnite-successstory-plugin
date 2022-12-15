﻿using Playnite.SDK;
using Playnite.SDK.Models;
using SuccessStory.Models;
using SuccessStory.Services;
using System;
using CommonPluginsShared.Extensions;
using CommonPluginsShared;
using System.IO;
using System.Collections.Generic;
using Playnite.SDK.Data;
using System.Text;
using System.Security.Principal;
using CommonPlayniteShared.Common;
using static CommonPluginsShared.PlayniteTools;
using static SuccessStory.Services.SuccessStoryDatabase;
using Playnite.SDK.Plugins;

namespace SuccessStory.Clients
{
    interface IAchievementFactory
    {
        void BuildClient(
            Dictionary<AchievementSource, GenericAchievements> Providers,
            Dictionary<AchievementSource, ISearchableManualAchievements> ManualSearchProviders,
            Dictionary<AchievementSource, IMetadataAugmentAchievements> AchievementMetadataAugmenters
        );
    }
    interface ISearchableManualAchievements
    {
        string ClientName { get; }
        string Glyph { get; }
        List<SearchResult> SearchGame(string Name);
        bool CanDoManualAchievements(Game game, GameAchievements gameAchievements);
        GameAchievements DoManualAchievements(Game game, GameAchievements gameAchievements);
        GameAchievements GetManualAchievements(Game game, SearchResult searchResult);
    }
    interface IMetadataAugmentAchievements
    {
        GameAchievements RefreshRarity(string sourceName, GameAchievements gameAchievements);
    }
    public abstract class GenericAchievements
    {
        // TODO: make this function also detect if a specific provider is already attached, which is critical for manual achivements
        public virtual int CheckAchivementSourceRank(ExternalPlugin pluginType, SuccessStorySettings settings, Game game, bool ignoreSpecial = false)
        {
            return 0;
        }
        public virtual bool SetEstimateTimeToUnlock(Game game, GameAchievements gameAchievements)
        {
            return false;
        }
        public virtual dynamic GetOneGameView(SuccessStorySettingsViewModel pluginSettings, Game gameMenu)
        {
            return null;
        }
        public virtual bool HasManualMenuOverride(IPlayniteAPI playniteApi, SuccessStorySettingsViewModel pluginSettings, Game gameMenu)
        {
            return false;
        }
        public virtual bool BuildManualMenuOverride(IPlayniteAPI playniteApi, SuccessStorySettingsViewModel pluginSettings, GameAchievements gameAchievements, Game gameMenu, List<GameMenuItem> gameMenuItems)
        {
            return false;
        }
        public virtual GameAchievements RefreshManualOverrideData(Game game)
        {
            return null;
        }






        internal static ILogger logger => LogManager.GetLogger();
        internal static IResourceProvider resources => new ResourceProvider();



        internal static SuccessStoryDatabase PluginDatabase => SuccessStory.PluginDatabase;

        protected bool? CachedConfigurationValidationResult { get; set; }
        protected bool? CachedIsConnectedResult { get; set; }

        public string ClientName { get; }
        protected string LocalLang { get; }
        protected string LocalLangShort { get; }

        protected string LastErrorId { get; set; }
        protected string LastErrorMessage { get; set; }

        internal string cookiesPath { get; }



        // TODO Must be removed when all store is refactored
        public GenericAchievements(string ClientName, string LocalLang = "", string LocalLangShort = "")
        {
            this.ClientName = ClientName;
            this.LocalLang = LocalLang;
            this.LocalLangShort = LocalLangShort;

            cookiesPath = Path.Combine(PluginDatabase.Paths.PluginUserDataPath, CommonPlayniteShared.Common.Paths.GetSafePathName($"{ClientName}.json"));
        }


        #region Achievements
        /// <summary>
        /// Get all achievements for a game.
        /// </summary>
        /// <param name="Id"></param>
        /// <returns></returns>
        /*public GameAchievements GetAchievements(Guid Id)
        {
            Game game = PluginDatabase.PlayniteApi.Database.Games.Get(Id);
            if (game == null)
            {
                return new GameAchievements();
            }

            return GetAchievements(game);
        }*/

        /// <summary>
        /// Get all achievements for a game.
        /// </summary>
        /// <param name="game"></param>
        /// <returns></returns>
        public abstract GameAchievements GetAchievements(Game game);
        #endregion


        #region Configuration
        /// <summary>
        /// Override to validate service-specific config and display error messages to the user
        /// </summary>
        /// <param name="playniteAPI"></param>
        /// <param name="plugin"></param>
        /// <returns>false when there are errors, true if everything's good</returns>
        public abstract bool ValidateConfiguration();


        public virtual bool IsConnected()
        {
            return false;
        }

        public virtual bool IsConfigured()
        {
            return false;
        }

        public virtual bool EnabledInSettings()
        {
            return false;
        }
        #endregion


        public virtual void ResetCachedConfigurationValidationResult()
        {
            CachedConfigurationValidationResult = null;
        }

        public virtual void ResetCachedIsConnectedResult()
        {
            CachedIsConnectedResult = null;
        }


        #region Cookies
        internal List<HttpCookie> GetCookies()
        {
            if (File.Exists(cookiesPath))
            {
                try
                {
                    return Serialization.FromJson<List<HttpCookie>>(
                        Encryption.DecryptFromFile(
                            cookiesPath,
                            Encoding.UTF8,
                            WindowsIdentity.GetCurrent().User.Value));
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, "Failed to load saved cookies");
                }
            }

            return null;
        }

        internal void SetCookies(List<HttpCookie> httpCookies)
        {
            FileSystem.CreateDirectory(Path.GetDirectoryName(cookiesPath));
            Encryption.EncryptToFile(
                cookiesPath,
                Serialization.ToJson(httpCookies),
                Encoding.UTF8,
                WindowsIdentity.GetCurrent().User.Value);
        }
        #endregion


        #region Errors
        public virtual void ShowNotificationPluginDisable(string Message)
        {
            LastErrorId = $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-disabled";
            LastErrorMessage = Message;
            logger.Warn($"{ClientName} is enable then disabled in Playnite");

            PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-disabled",
                $"{PluginDatabase.PluginName}\r\n{Message}",
                NotificationType.Error
            ));
        }

        public virtual void ShowNotificationPluginNoAuthenticate(string Message, ExternalPlugin PluginSource)
        {
            LastErrorId = $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-noauthenticate";
            LastErrorMessage = Message;
            logger.Warn($"{ClientName} user is not authenticated");

            PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-noauthenticate",
                $"{PluginDatabase.PluginName}\r\n{Message}",
                NotificationType.Error,
                () =>
                {
                    try
                    {
                        var plugin = API.Instance.Addons.Plugins.Find(x => x.Id == PlayniteTools.GetPluginId(PluginSource));
                        if (plugin != null)
                        {
                            plugin.OpenSettingsView();

                            foreach (var achievementProvider in SuccessStoryDatabase.AchievementProviders.Values)
                            {
                                achievementProvider.ResetCachedConfigurationValidationResult();
                                achievementProvider.ResetCachedIsConnectedResult();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }
                }
            ));
        }

        public virtual void ShowNotificationPluginNoConfiguration(string Message)
        {
            LastErrorId = $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-noconfig";
            LastErrorMessage = Message;
            logger.Warn($"{ClientName} is not configured");

            PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                $"{PluginDatabase.PluginName}-{ClientName.RemoveWhiteSpace()}-noconfig",
                $"{PluginDatabase.PluginName}\r\n{Message}",
                NotificationType.Error,
                () => PluginDatabase.Plugin.OpenSettingsView()
            ));
        }

        public virtual void ShowNotificationPluginError(Exception ex)
        {
            Common.LogError(ex, false, $"{ClientName}", true, PluginDatabase.PluginName);
        }

        public virtual void ShowNotificationPluginWebError(Exception ex, string Url)
        {
            Common.LogError(ex, false, $"{ClientName} - Failed to load {Url}", true, PluginDatabase.PluginName);
        }


        public virtual void ShowNotificationPluginErrorMessage()
        {
            if (!LastErrorMessage.IsNullOrEmpty())
            {
                PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                    LastErrorId,
                    $"{PluginDatabase.PluginName}\r\n{LastErrorMessage}",
                    NotificationType.Error
                ));
            }
        }

        internal int GetRankOfThisSource(ExternalPlugin externalPlugin, SuccessStorySettings settings, Game game)
        {
            return 0;
        }
        #endregion
    }
}
